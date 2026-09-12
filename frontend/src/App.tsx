import { useState, useEffect, useRef } from 'react';
import { PillarNavigation } from './components/layout/PillarNavigation';
import { Header } from './components/layout/Header';
import { MetricStrip } from './components/dashboard/MetricStrip';
import { SensorGrid } from './components/dashboard/SensorGrid';
import { api } from './services/api';

function App() {
  const [activePillar, setActivePillar] = useState<'ingestion' | 'command' | 'topology'>('ingestion');
  const [devices, setDevices] = useState<any[]>([]);
  const [selectedDevice, setSelectedDevice] = useState<any>(null);
  const simIntervalRef = useRef<number | null>(null);

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
    return () => {
      clearInterval(interval);
      if (simIntervalRef.current) clearInterval(simIntervalRef.current);
    };
  }, []);

  const handleBurst = () => {
    console.log("Simulating burst load...");
    // Simulate active telemetry stream
    if (simIntervalRef.current) clearInterval(simIntervalRef.current);
    simIntervalRef.current = window.setInterval(async () => {
      for (const d of devices) {
        await api.ingestFloatTelemetry({
          macAddress: d.device.macAddress,
          value: 400 + (Math.random() * 20 - 10),
          metricUnit: 'W'
        });
      }
    }, 2000);
  };

  const handleInjectSpike = async () => {
    if (devices.length === 0) return;
    const randomDevice = devices[Math.floor(Math.random() * devices.length)].device;
    await api.ingestFloatTelemetry({
      macAddress: randomDevice.macAddress,
      value: 900 + Math.random() * 50,
      metricUnit: 'W'
    });
  };

  const handleDropHeartbeat = () => {
    if (simIntervalRef.current) {
      clearInterval(simIntervalRef.current);
      simIntervalRef.current = null;
    }
  };

  const handleReset = () => {
    // Note: To properly reset baseline, we'd need a backend endpoint or restart. 
    // We just stop simulation for now.
    handleDropHeartbeat();
  };

  return (
    <div className="min-h-screen bg-zinc-950 text-zinc-100 flex flex-col font-sans selection:bg-zinc-800">
      <Header 
        onRegisterClick={() => console.log("Open Registration Modal")} 
        onBurst={handleBurst}
        onInjectSpike={handleInjectSpike}
        onDropHeartbeat={handleDropHeartbeat}
        onReset={handleReset}
      />

      <main className="flex-1 p-4">
        <PillarNavigation activePillar={activePillar} setActivePillar={setActivePillar} />

        {activePillar === 'ingestion' && (
          <div className="max-w-7xl mx-auto mt-6">
            <MetricStrip devices={devices} />
            <SensorGrid devices={devices} onOpenDrawer={setSelectedDevice} />
          </div>
        )}
      </main>
    </div>
  );
}

export default App;
