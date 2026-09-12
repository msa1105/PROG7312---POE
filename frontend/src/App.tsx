import React, { useState } from 'react';
import { PillarNavigation } from './components/PillarNavigation';
import { DeviceRegistration } from './components/DeviceRegistration';
import { Activity } from 'lucide-react';

function App() {
  const [activePillar, setActivePillar] = useState<'ingestion' | 'command' | 'topology'>('ingestion');

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
                <DeviceRegistration onRegistered={() => console.log('Refetch devices...')} />
              </div>
              <div className="lg:col-span-3">
                <div className="bg-slate-800/30 border border-slate-700/50 rounded-xl p-6 h-full min-h-[400px] flex items-center justify-center">
                  <p className="text-slate-500">Telemetry Dashboard implementation goes here...</p>
                </div>
              </div>
            </div>
          )}
        </div>
      </main>
    </div>
  );
}

export default App;
