import { useState, useEffect } from 'react';
import { PillarNavigation } from './components/layout/PillarNavigation';
import { Header } from './components/layout/Header';
import { MetricStrip } from './components/dashboard/MetricStrip';
import { SensorGrid } from './components/dashboard/SensorGrid';
import { TopologyTree } from './components/topology/TopologyTree';
import { ValidationConsole } from './components/topology/ValidationConsole';
import { RegisterDeviceModal } from './components/modals/RegisterDeviceModal';
import { DiagnosticDrawer } from './components/triage/DiagnosticDrawer';
import { api } from './services/api';

function App() {
  const [activePillar, setActivePillar] = useState<'ingestion' | 'command' | 'topology'>('ingestion');
  const [devices, setDevices] = useState<any[]>([]);
  const [selectedDevice, setSelectedDevice] = useState<any>(null);
  const [isRegisterOpen, setIsRegisterOpen] = useState(false);

  // Temporary drawer rendering helper
  const renderDrawer = () => {
    if (!selectedDevice) return null;
    return <DiagnosticDrawer deviceData={selectedDevice} onClose={() => setSelectedDevice(null)} />;
  };

  const fetchDevices = async () => {
    try {
      const data = await api.getAllDevices();
      setDevices(data);
    } catch (e) {
      console.error(e);
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

  return (
    <div className="min-h-screen bg-zinc-950 text-zinc-100 flex flex-col font-sans selection:bg-zinc-800">
      <Header 
        onRegisterClick={() => setIsRegisterOpen(true)} 
        onBurst={handleBurst}
        onInjectSpike={handleInjectSpike}
        onDropHeartbeat={handleDropHeartbeat}
        onReset={handleReset}
      />

      <main className="flex-1 p-4">
        <PillarNavigation activePillar={activePillar} setActivePillar={setActivePillar} />

        {activePillar === 'ingestion' && (
          <div className="max-w-[1600px] mx-auto mt-6 grid grid-cols-1 lg:grid-cols-4 gap-6">
            {/* Left Column: Topology */}
            <div className="flex flex-col gap-6">
              <div className="flex-1 min-h-[300px]">
                <TopologyTree />
              </div>
              <div className="flex-1 min-h-[300px]">
                <ValidationConsole />
              </div>
            </div>

            {/* Right Column: Telemetry Matrix */}
            <div className="lg:col-span-3">
              <MetricStrip devices={devices} />
              <SensorGrid devices={devices} onOpenDrawer={setSelectedDevice} />
            </div>
          </div>
        )}
      </main>
      
      {renderDrawer()}
      {isRegisterOpen && (
        <RegisterDeviceModal 
          onClose={() => setIsRegisterOpen(false)} 
          onSuccess={fetchDevices} 
        />
      )}
    </div>
  );
}

export default App;
