import { Activity, RadioTower, History, Lock } from 'lucide-react';
import { clsx } from 'clsx';

type PillarProps = {
  activePillar: 'ingestion' | 'command' | 'topology';
  setActivePillar: (pillar: 'ingestion' | 'command' | 'topology') => void;
};

export const PillarNavigation = ({ activePillar, setActivePillar }: PillarProps) => {
  return (
    <div className="flex flex-col md:flex-row gap-4 p-4 max-w-7xl mx-auto">
      {/* Pillar 1: Ingestion */}
      <div 
        className={clsx(
          "flex-1 p-6 rounded-sm border transition-all cursor-pointer relative overflow-hidden",
          activePillar === 'ingestion' 
            ? "border-zinc-500 bg-zinc-900 shadow-[0_0_10px_rgba(161,161,170,0.1)]" 
            : "border-zinc-800 bg-zinc-950 hover:bg-zinc-900"
        )}
        onClick={() => setActivePillar('ingestion')}
      >
        <div className="flex items-center gap-3 mb-2">
          <Activity className={activePillar === 'ingestion' ? "text-zinc-100" : "text-zinc-500"} size={20} />
          <h2 className={clsx("text-sm font-semibold uppercase tracking-wider", activePillar === 'ingestion' ? "text-zinc-100" : "text-zinc-500")}>
            Sensor Data Ingestion & Telemetry
          </h2>
        </div>
        <p className="text-xs text-zinc-400">
          Monitor multi-typed telemetry, register devices, and manage real-time streams.
        </p>
      </div>

      {/* Pillar 2: Command Stream (Disabled) */}
      <div 
        className="flex-1 p-6 rounded-sm border border-zinc-900 bg-zinc-950/50 cursor-not-allowed relative opacity-60 group"
      >
        <div className="absolute top-2 right-2 flex items-center gap-1 px-2 py-1 bg-zinc-900 text-[10px] text-zinc-500 rounded border border-zinc-800 uppercase tracking-wide">
          <Lock size={10} /> Part 2 Milestone
        </div>
        <div className="flex items-center gap-3 mb-2">
          <History className="text-zinc-600" size={20} />
          <h2 className="text-sm font-semibold uppercase tracking-wider text-zinc-600">
            Real-Time Command Stream
          </h2>
        </div>
        <p className="text-xs text-zinc-600">
          Dispatch actuation commands and view historical logs.
        </p>
        
        {/* Hover Tooltip */}
        <div className="absolute inset-0 bg-zinc-950/90 flex items-center justify-center opacity-0 group-hover:opacity-100 transition-opacity">
          <span className="text-xs text-zinc-400">Two-way actuation and command streaming will be provisioned in Part 2.</span>
        </div>
      </div>

      {/* Pillar 3: Topology (Disabled) */}
      <div 
        className="flex-1 p-6 rounded-sm border border-zinc-900 bg-zinc-950/50 cursor-not-allowed relative opacity-60 group"
      >
        <div className="absolute top-2 right-2 flex items-center gap-1 px-2 py-1 bg-zinc-900 text-[10px] text-zinc-500 rounded border border-zinc-800 uppercase tracking-wide">
          <Lock size={10} /> PoE Final Milestone
        </div>
        <div className="flex items-center gap-3 mb-2">
          <RadioTower className="text-zinc-600" size={20} />
          <h2 className="text-sm font-semibold uppercase tracking-wider text-zinc-600">
            Network Topology & Mesh
          </h2>
        </div>
        <p className="text-xs text-zinc-600">
          Visualize physical deployments and dynamic routing paths.
        </p>

        {/* Hover Tooltip */}
        <div className="absolute inset-0 bg-zinc-950/90 flex items-center justify-center opacity-0 group-hover:opacity-100 transition-opacity text-center px-4">
          <span className="text-xs text-zinc-400">Dynamic mesh hop routing and packet delivery graphs will be provisioned in final PoE.</span>
        </div>
      </div>
    </div>
  );
};
