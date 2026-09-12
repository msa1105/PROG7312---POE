import React, { useState, useEffect } from 'react';
import { PillarNavigation } from './components/PillarNavigation';
import { DeviceRegistration } from './components/DeviceRegistration';
import { TelemetryDashboard } from './components/TelemetryDashboard';
import { TopologyValidatorView } from './components/TopologyValidatorView';
import { TelemetrySimulator } from './components/TelemetrySimulator';
import { Activity } from 'lucide-react';
import { api } from './services/api';

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
    <div className="min-h-screen bg-slate-900 text-slate-100 flex flex-col font-sans">
      <header className="border-b border-slate-800 bg-slate-900/80 backdrop-blur-md sticky top-0 z-50 px-6 py-4">
        <div className="max-w-7xl mx-auto flex items-center justify-between">
          <div className="flex items-center gap-2">
            <div className="bg-blue-600 p-1.5 rounded-lg">
              <Activity size={20} className="text-white" />
            </div>
            <h1 className="text-xl font-bold bg-gradient-to-r from-blue-400 to-indigo-400 bg-clip-text text-transparent">
              Smart-X Gateway
            </h1>
          </div>
          <div className="text-xs font-mono text-slate-500 flex gap-4">
            <span>NET: 10.0</span>
            <span className="text-emerald-500">SYS: ONLINE</span>
          </div>
        </div>
      </header>

      <main className="flex-1 p-6">
        <div className="max-w-7xl mx-auto space-y-8">
          <PillarNavigation activePillar={activePillar} setActivePillar={setActivePillar} />

          {activePillar === 'ingestion' && (
            <div className="grid grid-cols-1 lg:grid-cols-4 gap-6">
              <div className="lg:col-span-1 space-y-6">
                <DeviceRegistration onRegistered={fetchDevices} />
                <TelemetrySimulator devices={devices} />
                <TopologyValidatorView />
              </div>
              <div className="lg:col-span-3">
                <TelemetryDashboard devices={devices} />
              </div>
            </div>
          )}
        </div>
      </main>
    </div>
  );
}

export default App;
