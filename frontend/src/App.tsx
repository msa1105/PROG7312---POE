import { useState, useEffect, useCallback } from 'react';
import { GatewayLandingView } from './components/layout/GatewayLandingView';
import { PillarNavigation } from './components/layout/PillarNavigation';
import { Header } from './components/layout/Header';
import { MetricStrip } from './components/dashboard/MetricStrip';
import { SensorGrid, type SensorHistory } from './components/dashboard/SensorGrid';
import { TopologyTree } from './components/topology/TopologyTree';
import { ValidationConsole } from './components/topology/ValidationConsole';
import { RegisterDeviceModal } from './components/modals/RegisterDeviceModal';
import { DiagnosticDrawer } from './components/triage/DiagnosticDrawer';
import { IncidentTriageCenter } from './components/triage/IncidentTriageCenter';
import { Network, Terminal, Activity, Filter, X } from 'lucide-react';
import { api } from './services/api';
import type { DeviceView } from './types/telemetry';
import { isWithinZone } from './utils/telemetry';

const SPIKE_TARGET_MAC = "24:6F:28:9A:12:03";
const HEARTBEAT_TARGET_MAC = "24:6F:28:9A:12:01";
const HISTORY_LENGTH = 30;

function App() {
  const [viewMode, setViewMode] = useState<'landing' | 'dashboard'>('landing');
  const [activePillar, setActivePillar] = useState<'ingestion' | 'command' | 'topology'>('ingestion');
  const [topologyTab, setTopologyTab] = useState<'tree' | 'console'>('tree');
  const [devices, setDevices] = useState<DeviceView[]>([]);
  const [fetchedAt, setFetchedAt] = useState(() => Date.now());
  const [history, setHistory] = useState<SensorHistory>({});
  const [isBackendOnline, setIsBackendOnline] = useState(true);
  const [selectedMac, setSelectedMac] = useState<string | null>(null);
  const [zoneFilter, setZoneFilter] = useState<string | null>(null);
  const [isRegisterOpen, setIsRegisterOpen] = useState(false);
  const [showTriageDeck, setShowTriageDeck] = useState(false);
  const [reportRequest, setReportRequest] = useState<{ id: number; mac: string } | null>(null);

  const fetchDevices = useCallback(async () => {
    try {
      const data = await api.getAllDevices();
      setDevices(data);
      setFetchedAt(Date.now());
      setIsBackendOnline(true);

      // Sparkline history: append each new packet once (deduplicated by packet id)
      setHistory(prev => {
        const next: SensorHistory = { ...prev };
        for (const view of data) {
          const snapshot = view.latestTelemetry;
          const mac = view.device.macAddress;
          if (!snapshot || typeof snapshot.value !== 'number') continue;
          const entry = next[mac] ?? { lastPacketId: null, values: [] };
          if (entry.lastPacketId === snapshot.packetId) continue;
          next[mac] = { lastPacketId: snapshot.packetId, values: [...entry.values, snapshot.value].slice(-HISTORY_LENGTH) };
        }
        return next;
      });
    } catch (e) {
      console.error('Failed to fetch devices:', e);
      setIsBackendOnline(false);
    }
  }, []);

  useEffect(() => {
    fetchDevices();
    const interval = setInterval(fetchDevices, 2000);
    return () => clearInterval(interval);
  }, [fetchDevices]);

  const handleBurst = async () => {
    const result = await api.simulationBurst();
    await fetchDevices();
    return `${result.processedCount.toLocaleString()} packets ingested in ${result.elapsedMs}ms via Jagged Buffer`;
  };

  const handleInjectSpike = async () => {
    await api.simulationSpike(SPIKE_TARGET_MAC, 4.5);
    await fetchDevices();
    return `+4.5σ spike scheduled on ${SPIKE_TARGET_MAC}`;
  };

  const handleDropHeartbeat = async () => {
    await api.simulationDropHeartbeat(HEARTBEAT_TARGET_MAC);
    await fetchDevices();
    return `Heartbeat dropped on ${HEARTBEAT_TARGET_MAC} (lease expires after 2 missed windows)`;
  };

  const handleReset = async () => {
    await api.simulationReset();
    setHistory({});
    await fetchDevices();
    return 'Simulation baseline reset and fleet re-seeded';
  };

  const handleReportIncident = (mac: string) => {
    setReportRequest({ id: Date.now(), mac });
    setShowTriageDeck(true);
    setSelectedMac(null);
  };

  const selectedDevice = selectedMac ? devices.find(d => d.device.macAddress === selectedMac) ?? null : null;
  const visibleDevices = zoneFilter ? devices.filter(d => isWithinZone(d.device.deploymentLocation, zoneFilter)) : devices;
  const activeAlertCount = devices.filter(d => d.activeAlerts.length > 0).length;

  return (
    <div className="min-h-screen bg-zinc-950 text-zinc-100 flex flex-col font-sans selection:bg-zinc-800">
      {viewMode === 'landing' ? (
        <GatewayLandingView
          onEnterIngestion={() => setViewMode('dashboard')}
          registeredDeviceCount={devices.length}
        />
      ) : (
        <>
          <Header
            onReturnToLanding={() => setViewMode('landing')}
            onRegisterClick={() => setIsRegisterOpen(true)}
            onBurst={handleBurst}
            onInjectSpike={handleInjectSpike}
            onDropHeartbeat={handleDropHeartbeat}
            onReset={handleReset}
            onToggleTriage={() => setShowTriageDeck(!showTriageDeck)}
            activeTriageCount={activeAlertCount}
            isBackendOnline={isBackendOnline}
          />

          <main className="flex-1 p-4 max-w-[1600px] w-full mx-auto">
            <PillarNavigation activePillar={activePillar} setActivePillar={setActivePillar} />

            {/* Dynamic Incident Triage & Engagement Deck */}
            {showTriageDeck && (
              <div className="mt-4 mb-6 animate-in slide-in-from-top duration-300">
                <IncidentTriageCenter
                  key={reportRequest?.id ?? 'deck'}
                  initialReportMac={reportRequest?.mac}
                  openReportOnMount={reportRequest !== null}
                  knownMacs={devices.map(d => d.device.macAddress)}
                />
              </div>
            )}

            {activePillar === 'ingestion' && (
              <div className="mt-4 space-y-6">
                {/* Full Width Metric Strip across top */}
                <MetricStrip />

                {/* Main Content Grid */}
                <div className="grid grid-cols-1 xl:grid-cols-12 gap-6 items-start">
                  {/* Left: Sensor Ingestion Matrix (8 cols on XL) */}
                  <div className="xl:col-span-8">
                    <div className="flex items-center justify-between mb-3 border-b border-zinc-800/80 pb-2">
                      <div className="text-xs font-mono font-bold uppercase tracking-wider text-zinc-300 flex items-center gap-2">
                        <Activity size={14} className="text-emerald-400" />
                        Live Ingestion Matrix ({visibleDevices.length} Edge Nodes)
                      </div>
                      <div className="text-[11px] font-mono text-zinc-500 hidden sm:block">
                        Click any node card for Deep Packet Triage &amp; C# Struct Operations
                      </div>
                    </div>

                    {zoneFilter && (
                      <div className="mb-3 flex items-center justify-between gap-2 px-3 py-1.5 bg-zinc-900 border border-zinc-700 rounded-sm text-[11px] font-mono text-zinc-300">
                        <span className="flex items-center gap-2 truncate">
                          <Filter size={12} className="text-cyan-400 shrink-0" />
                          Zone filter: <strong className="text-zinc-100 truncate">{zoneFilter}</strong>
                        </span>
                        <button onClick={() => setZoneFilter(null)} className="flex items-center gap-1 text-zinc-400 hover:text-zinc-100 cursor-pointer shrink-0">
                          <X size={12} /> Clear
                        </button>
                      </div>
                    )}

                    <SensorGrid
                      devices={visibleDevices}
                      history={history}
                      fetchedAt={fetchedAt}
                      onOpenDrawer={setSelectedMac}
                      onRefresh={fetchDevices}
                      onReportIncident={handleReportIncident}
                    />
                  </div>

                  {/* Right: Topology & Audit Console with Tabs (4 cols on XL) */}
                  <div className="xl:col-span-4 flex flex-col gap-3">
                    <div className="flex items-center justify-between bg-zinc-950 p-1.5 rounded border border-zinc-800 text-xs font-mono">
                      <div className="flex gap-1.5">
                        <button
                          onClick={() => setTopologyTab('tree')}
                          className={`flex items-center gap-1.5 px-3 py-1.5 rounded transition-colors cursor-pointer ${
                            topologyTab === 'tree'
                              ? 'bg-zinc-800 text-zinc-100 font-bold border border-zinc-700'
                              : 'text-zinc-500 hover:text-zinc-300'
                          }`}
                        >
                          <Network size={13} />
                          <span>Mesh Tree</span>
                        </button>
                        <button
                          onClick={() => setTopologyTab('console')}
                          className={`flex items-center gap-1.5 px-3 py-1.5 rounded transition-colors cursor-pointer ${
                            topologyTab === 'console'
                              ? 'bg-zinc-800 text-zinc-100 font-bold border border-zinc-700'
                              : 'text-zinc-500 hover:text-zinc-300'
                          }`}
                        >
                          <Terminal size={13} />
                          <span>Audit Console</span>
                        </button>
                      </div>
                      <span className="text-[10px] font-mono text-zinc-500 uppercase pr-1">
                        {topologyTab === 'tree' ? 'Recursive Hierarchy' : 'JSON Validator'}
                      </span>
                    </div>

                    <div className="w-full">
                      {topologyTab === 'tree' ? (
                        <div className="min-h-[440px]">
                          <TopologyTree
                            selectedZone={zoneFilter}
                            onSelectZone={setZoneFilter}
                            onSelectDevice={setSelectedMac}
                          />
                        </div>
                      ) : (
                        <div className="min-h-[440px]">
                          <ValidationConsole />
                        </div>
                      )}
                    </div>
                  </div>
                </div>
              </div>
            )}
          </main>

          {selectedDevice && (
            <DiagnosticDrawer
              deviceData={selectedDevice}
              allDevices={devices}
              fetchedAt={fetchedAt}
              onClose={() => setSelectedMac(null)}
              onRefresh={fetchDevices}
              onReportIncident={handleReportIncident}
              onSelectDevice={setSelectedMac}
            />
          )}

          {isRegisterOpen && (
            <RegisterDeviceModal
              existingMacs={devices.map(d => d.device.macAddress)}
              onClose={() => setIsRegisterOpen(false)}
              onSuccess={fetchDevices}
            />
          )}
        </>
      )}
    </div>
  );
}

export default App;
