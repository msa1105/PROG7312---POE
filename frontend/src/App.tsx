import { useState, useEffect } from 'react';
import { GatewayLandingView } from './components/layout/GatewayLandingView';
import { PillarNavigation } from './components/layout/PillarNavigation';
import { Header } from './components/layout/Header';
import { MetricStrip } from './components/dashboard/MetricStrip';
import { SensorGrid } from './components/dashboard/SensorGrid';
import { TopologyTree } from './components/topology/TopologyTree';
import { ValidationConsole } from './components/topology/ValidationConsole';
import { RegisterDeviceModal } from './components/modals/RegisterDeviceModal';
import { DiagnosticDrawer } from './components/triage/DiagnosticDrawer';
import { IncidentTriageCenter } from './components/triage/IncidentTriageCenter';
import { Network, Terminal, Activity } from 'lucide-react';
import { api } from './services/api';

function App() {
  const [viewMode, setViewMode] = useState<'landing' | 'dashboard'>('landing');
  const [activePillar, setActivePillar] = useState<'ingestion' | 'command' | 'topology'>('ingestion');
  const [topologyTab, setTopologyTab] = useState<'tree' | 'console'>('tree');
  const [devices, setDevices] = useState<any[]>([]);
  const [selectedDevice, setSelectedDevice] = useState<any>(null);
  const [isRegisterOpen, setIsRegisterOpen] = useState(false);
  const [showTriageDeck, setShowTriageDeck] = useState(false);
  const [targetIncidentMac, setTargetIncidentMac] = useState<string | undefined>(undefined);

  const fetchDevices = async () => {
    try {
      const data = await api.getAllDevices();
      setDevices(data);
    } catch (e) {
      console.error('Failed to fetch devices:', e);
    }
  };

  useEffect(() => {
    fetchDevices();
    const interval = setInterval(fetchDevices, 2000);
    return () => clearInterval(interval);
  }, []);

  const handleBurst = async () => {
    await api.simulationBurst();
    await fetchDevices();
  };

  const handleInjectSpike = async () => {
    await api.simulationSpike("24:6F:28:9A:12:03", 4.5);
    await fetchDevices();
  };

  const handleDropHeartbeat = async () => {
    await api.simulationDropHeartbeat("24:6F:28:9A:12:01");
    await fetchDevices();
  };

  const handleReset = async () => {
    await api.simulationReset();
    await fetchDevices();
  };

  const handleReportIncidentFromDrawer = (mac: string) => {
    setTargetIncidentMac(mac);
    setShowTriageDeck(true);
    setSelectedDevice(null);
  };

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
            activeTriageCount={devices.filter(d => d.latestTelemetry?.isAnomaly).length}
          />

          <main className="flex-1 p-4 max-w-[1600px] w-full mx-auto">
            <PillarNavigation activePillar={activePillar} setActivePillar={setActivePillar} />

            {/* Dynamic Incident Triage & Engagement Deck */}
            {showTriageDeck && (
              <div className="mt-4 mb-6 animate-in slide-in-from-top duration-300">
                <IncidentTriageCenter 
                  activeDeviceMac={targetIncidentMac}
                  onRefreshDevices={fetchDevices}
                />
              </div>
            )}

            {activePillar === 'ingestion' && (
              <div className="mt-4 space-y-6">
                {/* Full Width Metric Strip across top */}
                <MetricStrip devices={devices} />

                {/* Main Content Grid */}
                <div className="grid grid-cols-1 xl:grid-cols-12 gap-6 items-start">
                  {/* Left: Sensor Ingestion Matrix (8 cols on XL) */}
                  <div className="xl:col-span-8">
                    <div className="flex items-center justify-between mb-3 border-b border-zinc-800/80 pb-2">
                      <div className="text-xs font-mono font-bold uppercase tracking-wider text-zinc-300 flex items-center gap-2">
                        <Activity size={14} className="text-emerald-400" />
                        Live Ingestion Matrix ({devices.length} Edge Nodes)
                      </div>
                      <div className="text-[11px] font-mono text-zinc-500 hidden sm:block">
                        Click any node card for Deep Packet Triage &amp; C# Struct Operations
                      </div>
                    </div>
                    <SensorGrid devices={devices} onOpenDrawer={setSelectedDevice} />
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
                          <TopologyTree />
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
              onClose={() => setSelectedDevice(null)} 
              onRefresh={fetchDevices}
              onReportIncident={handleReportIncidentFromDrawer}
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
