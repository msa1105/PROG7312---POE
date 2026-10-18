import { NavLink } from 'react-router-dom';
import { Activity, RadioTower, History, Lock } from 'lucide-react';
import { clsx } from 'clsx';
import { useGatewayStore } from '../../state/gatewayContext';

const pillarClass = ({ isActive }: { isActive: boolean }) => clsx(
  "flex-1 p-6 rounded-sm border transition-all cursor-pointer relative overflow-hidden block",
  isActive
    ? "border-zinc-500 bg-zinc-900 shadow-[0_0_10px_rgba(161,161,170,0.1)]"
    : "border-zinc-800 bg-zinc-950 hover:bg-zinc-900"
);

/**
 * Module navigation. NavLink keeps the active pillar in sync with the URL, so browser back/forward,
 * refresh and deep links all land on the right module.
 */
export const PillarNavigation = () => {
  const { commandStreamSearch } = useGatewayStore();
  return (
    <nav aria-label="Gateway modules" className="flex flex-col md:flex-row gap-4 p-4 max-w-[1600px] mx-auto">
      {/* Pillar 1: Ingestion */}
      <NavLink to="/ingestion" className={pillarClass}>
        {({ isActive }) => (
          <>
            <div className="flex items-center gap-3 mb-2">
              <Activity className={isActive ? "text-zinc-100" : "text-zinc-500"} size={20} />
              <h2 className={clsx("text-sm font-semibold uppercase tracking-wider", isActive ? "text-zinc-100" : "text-zinc-500")}>
                Sensor Data Ingestion & Telemetry
              </h2>
            </div>
            <p className="text-xs text-zinc-400">
              Monitor multi-typed telemetry, register devices, and manage real-time streams.
            </p>
          </>
        )}
      </NavLink>

      {/* Pillar 2: Command Stream (Part 2) */}
      <NavLink to={`/commands${commandStreamSearch}`} className={pillarClass}>
        {({ isActive }) => (
          <>
            <div className="absolute top-2 right-2 flex items-center gap-1 px-2 py-1 bg-emerald-950/70 text-[10px] text-emerald-400 rounded border border-emerald-800 uppercase tracking-wide">
              Part 2 • Live
            </div>
            <div className="flex items-center gap-3 mb-2">
              <History className={isActive ? "text-zinc-100" : "text-zinc-500"} size={20} />
              <h2 className={clsx("text-sm font-semibold uppercase tracking-wider", isActive ? "text-zinc-100" : "text-zinc-500")}>
                Real-Time Command Stream
              </h2>
            </div>
            <p className="text-xs text-zinc-400">
              Dispatch actuation commands, undo them, and act on predictive suggestions.
            </p>
          </>
        )}
      </NavLink>

      {/* Pillar 3: Topology (Disabled) */}
      <div
        className="flex-1 p-6 rounded-sm border border-zinc-900 bg-zinc-950/50 cursor-not-allowed relative opacity-60 group"
        aria-disabled="true"
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
    </nav>
  );
};
