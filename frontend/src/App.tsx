import { useState, useEffect } from 'react';
import { PillarNavigation } from './components/layout/PillarNavigation';
import { Header } from './components/layout/Header';
import { api } from './services/api';
// Temporarily import existing dashboard logic, this will be extracted in next steps
import { TelemetryDashboard } from './components/TelemetryDashboard';

function App() {
  const [activePillar, setActivePillar] = useState<'ingestion' | 'command' | 'topology'>('ingestion');
  const [devices, setDevices] = useState<any[]>([]);

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

  return (
    <div className="min-h-screen bg-zinc-950 text-zinc-100 flex flex-col font-sans selection:bg-zinc-800">
      <Header onRegisterClick={() => console.log("Open Registration Modal")} />

      <main className="flex-1 p-4">
        <PillarNavigation activePillar={activePillar} setActivePillar={setActivePillar} />

        {activePillar === 'ingestion' && (
          <div className="max-w-7xl mx-auto mt-6">
            <TelemetryDashboard devices={devices} />
          </div>
        )}
      </main>
    </div>
  );
}

export default App;
