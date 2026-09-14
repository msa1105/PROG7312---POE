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
import { api } from './services/api';

function App() {
  const [viewMode, setViewMode] = useState<'landing' | 'dashboard'>('landing');
  const [activePillar, setActivePillar] = useState<'ingestion' | 'command' | 'topology'>('ingestion');
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
    setSelectedDevice(null); // Close drawer to show triage deck
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

          <main className="flex-1 p-4">
            <PillarNavigation activePillar={activePillar} setActivePillar={setActivePillar} />

            {/* Dynamic Incident Triage & Engagement Deck */}
            {showTriageDeck && (
              <div className="max-w-[1600px] mx-auto mt-4 mb-6 animate-in slide-in-from-top duration-300">
                <IncidentTriageCenter 
                  activeDeviceMac={targetIncidentMac}
                  onRefreshDevices={fetchDevices}
                />
              </div>
            )}

            {/* HCI Research Strategy Theoretical Alignment Ribbon */}
            <div className="max-w-[1600px] mx-auto px-4 py-2 mt-2 bg-zinc-900/40 border border-zinc-800/80 rounded-sm flex flex-wrap items-center justify-between gap-2 text-[11px] font-mono text-zinc-400">
              <div className="flex items-center gap-2">
                <span className="text-zinc-500 uppercase tracking-widest font-bold">Research Engagement Strategy:</span>
                <span className="text-emerald-400 font-bold">Selective Visual Highlighting &amp; Hierarchical Progressive Disclosure</span>
                <span className="text-zinc-500 hidden md:inline">(Endsley 1995 • Shneiderman 1996 • Sweller 1988)</span>
              </div>
              <div className="flex items-center gap-3 text-zinc-500">
                <span>Reliability Benchmarking (Sailer 2017)</span>
                <span>•</span>
                <span>Actionable HITL Triage (Parasuraman 2000)</span>
              </div>
            </div>

            {activePillar === 'ingestion' && (
              <div className="max-w-[1600px] mx-auto mt-6 grid grid-cols-1 lg:grid-cols-4 gap-6">
                {/* Left Column: Recursive Topology & Validation Console */}
                <div className="flex flex-col gap-6">
                  <div className="flex-1 min-h-[300px]">
                    <TopologyTree />
                  </div>
                  <div className="flex-1 min-h-[300px]">
                    <ValidationConsole />
                  </div>
                </div>

                {/* Right Column: Ingestion Matrix & Sparkline Telemetry */}
                <div className="lg:col-span-3">
                  <MetricStrip devices={devices} />
                  <SensorGrid devices={devices} onOpenDrawer={setSelectedDevice} />
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
