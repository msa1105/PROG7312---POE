import React from 'react';
import { Activity, RadioTower, History } from 'lucide-react';
import { clsx } from 'clsx';

type PillarProps = {
  activePillar: 'ingestion' | 'command' | 'topology';
  setActivePillar: (pillar: 'ingestion' | 'command' | 'topology') => void;
};

export const PillarNavigation: React.FC<PillarProps> = ({ activePillar, setActivePillar }) => {
  return (
    <div className="flex flex-col md:flex-row gap-4 p-4 max-w-6xl mx-auto">
      {/* Pillar 1: Ingestion */}
      <div 
        className={clsx(
          "flex-1 p-6 rounded-xl border transition-all cursor-pointer relative overflow-hidden",
          activePillar === 'ingestion' 
            ? "border-blue-500 bg-slate-800/50 shadow-[0_0_15px_rgba(59,130,246,0.2)]" 
            : "border-slate-700 bg-slate-900/50 hover:bg-slate-800"
        )}
        onClick={() => setActivePillar('ingestion')}
      >
        <div className="flex items-center gap-3 mb-3">
          <Activity className="text-blue-400" size={24} />
          <h2 className="text-lg font-semibold text-slate-100">Sensor Data Ingestion & Telemetry</h2>
        </div>
        <p className="text-sm text-slate-400">
          Monitor multi-typed telemetry, register devices, and manage real-time streams.
        </p>
      </div>

      {/* Pillar 2: Command Stream (Disabled) */}
      <div 
        className="flex-1 p-6 rounded-xl border border-slate-800 bg-slate-900/30 opacity-70 cursor-not-allowed relative"
        title="Disabled — Available in Part 2"
      >
        <div className="absolute top-2 right-2 px-2 py-1 bg-slate-800 text-xs text-slate-400 rounded-md border border-slate-700">
          Disabled — Available in Part 2
        </div>
        <div className="flex items-center gap-3 mb-3">
          <History className="text-slate-500" size={24} />
          <h2 className="text-lg font-semibold text-slate-500">Real-Time Command Stream & History</h2>
        </div>
        <p className="text-sm text-slate-500">
          Dispatch actuation commands and view historical execution logs.
        </p>
      </div>

      {/* Pillar 3: Topology (Disabled) */}
      <div 
        className="flex-1 p-6 rounded-xl border border-slate-800 bg-slate-900/30 opacity-70 cursor-not-allowed relative"
        title="Disabled — Available in Final PoE"
      >
        <div className="absolute top-2 right-2 px-2 py-1 bg-slate-800 text-xs text-slate-400 rounded-md border border-slate-700">
          Disabled — Available in Final PoE
        </div>
        <div className="flex items-center gap-3 mb-3">
          <RadioTower className="text-slate-500" size={24} />
          <h2 className="text-lg font-semibold text-slate-500">Network Topology & Mesh Routing</h2>
        </div>
        <p className="text-sm text-slate-500">
          Visualize physical device deployments and dynamic mesh routing paths.
        </p>
      </div>
    </div>
  );
};
