import { Search, Power, PowerOff, Loader2, ShieldAlert, X } from 'lucide-react';
import { clsx } from 'clsx';
import type { DeviceView } from '../../types/telemetry';
import type { SensorHistory } from '../../state/gatewayContext';
import { Sparkline } from '../dashboard/Sparkline';
import { categoryLabel, formatValue } from '../../utils/telemetry';
import { ALERT_FILTERS, matchesFilters, type AlertStateFilter } from '../../utils/commandFilters';

const CATEGORY_FILTERS: { value: string; label: string }[] = [
  { value: 'all', label: 'All' },
  { value: '0', label: 'Environmental' },
  { value: '1', label: 'Power' },
  { value: '2', label: 'Actuator' },
];

type DeviceControlPanelProps = {
  devices: DeviceView[];
  history: SensorHistory;
  query: string;
  category: string;
  alertState: AlertStateFilter;
  selectedMac: string | null;
  highlightedMacs: Set<string>;
  onQueryChange: (q: string) => void;
  onCategoryChange: (c: string) => void;
  onAlertStateChange: (a: AlertStateFilter) => void;
  onSelect: (mac: string) => void;
  onCommand: (targets: { macAddress: string; targetState: boolean }[], label?: string) => void;
};

export const DeviceControlPanel = ({
  devices, history, query, category, alertState, selectedMac, highlightedMacs,
  onQueryChange, onCategoryChange, onAlertStateChange, onSelect, onCommand
}: DeviceControlPanelProps) => {
  const filtered = devices.filter(d => matchesFilters(d, query, category, alertState));
  const openValves = devices.filter(d => d.device.category === 2 && d.reportedState === true);


  return (
    <section className="bg-zinc-900 border border-zinc-800 rounded-sm p-4 font-mono text-xs space-y-3" aria-label="Device controls">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-zinc-300 font-bold uppercase tracking-wider">Live Devices &amp; Manual Overrides</h3>
        <button
          disabled={openValves.length === 0}
          onClick={() => onCommand(openValves.map(v => ({ macAddress: v.device.macAddress, targetState: false })), 'Shut down all valves')}
          className="flex items-center gap-1 px-2.5 py-1 bg-rose-950/60 hover:bg-rose-900 border border-rose-800 text-rose-200 rounded-sm disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer"
          title="Turns every open actuator OFF as one undoable batch"
        >
          <ShieldAlert size={12} /> Shut down all valves ({openValves.length})
        </button>
      </div>

      {/* Search + filters */}
      <div className="flex flex-col gap-2">
        <label className="relative block">
          <span className="sr-only">Search devices</span>
          <Search size={13} className="absolute left-2.5 top-2.5 text-zinc-500" />
          <input
            value={query}
            onChange={e => onQueryChange(e.target.value)}
            placeholder="Search by name, zone or MAC (e.g. solenoid, Rack B, 12:05)"
            className="w-full bg-zinc-950 border border-zinc-800 focus:border-zinc-500 rounded-sm pl-8 pr-8 py-2 text-zinc-200 outline-none"
          />
          {query && (
            <button onClick={() => onQueryChange('')} className="absolute right-2 top-2 text-zinc-500 hover:text-zinc-200 cursor-pointer" aria-label="Clear search">
              <X size={14} />
            </button>
          )}
        </label>
        <div className="flex flex-wrap items-center gap-1.5">
          <span className="text-[10px] text-zinc-500 uppercase mr-1">Category</span>
          {CATEGORY_FILTERS.map(f => (
            <button key={f.value} onClick={() => onCategoryChange(f.value)}
              className={clsx('px-2 py-0.5 rounded border text-[10px] cursor-pointer', category === f.value ? 'bg-zinc-800 border-zinc-600 text-zinc-100' : 'border-zinc-800 text-zinc-500 hover:text-zinc-300')}>
              {f.label}
            </button>
          ))}
          <span className="text-[10px] text-zinc-500 uppercase ml-3 mr-1">Alert state</span>
          {ALERT_FILTERS.map(f => (
            <button key={f} onClick={() => onAlertStateChange(f)}
              className={clsx('px-2 py-0.5 rounded border text-[10px] capitalize cursor-pointer', alertState === f ? 'bg-zinc-800 border-zinc-600 text-zinc-100' : 'border-zinc-800 text-zinc-500 hover:text-zinc-300')}>
              {f}
            </button>
          ))}
        </div>
      </div>

      {/* Device rows */}
      <div className="divide-y divide-zinc-800/80 border border-zinc-800 rounded-sm" role="list">
        {filtered.length === 0 && <div className="p-6 text-center text-zinc-500">No devices match these filters.</div>}
        {filtered.map(view => {
          const mac = view.device.macAddress;
          const offline = !view.status.isOnline;
          const critical = view.activeAlerts.find(a => a.severity === 'Critical');
          const alert = critical ?? view.activeAlerts[0];
          const syncing = view.optimistic?.pendingState !== undefined || view.pendingCommands > 0;

          return (
            <div
              key={mac}
              role="listitem"
              onClick={() => onSelect(mac)}
              className={clsx(
                'flex flex-wrap md:flex-nowrap items-center gap-3 px-3 py-2 cursor-pointer transition-colors',
                selectedMac === mac ? 'bg-zinc-800/70' : 'hover:bg-zinc-800/40',
                critical ? 'border-l-2 border-l-rose-500' : offline ? 'border-l-2 border-l-amber-500' : 'border-l-2 border-l-transparent',
                highlightedMacs.has(mac) && 'ring-1 ring-inset ring-cyan-700'
              )}
            >
              <span className={clsx('h-2 w-2 rounded-full shrink-0', offline ? 'bg-amber-500' : alert ? 'bg-rose-500 animate-pulse' : 'bg-emerald-500/60')} />
              <div className="min-w-0 w-48 shrink-0">
                <div className="text-zinc-100 truncate">{view.deviceName}</div>
                <div className="text-[10px] text-zinc-500">{mac} • {categoryLabel(view.device.category)}</div>
              </div>
              <div className="w-24 shrink-0 text-right tabular-nums">
                <span className="text-zinc-100 text-sm">{formatValue(view.latestTelemetry)}</span>
                <span className="text-zinc-500 ml-1">{view.latestTelemetry?.metricUnit ?? ''}</span>
              </div>
              <div className="w-28 shrink-0 hidden md:block">
                {(history[mac]?.values.length ?? 0) > 1 && <Sparkline data={history[mac].values} isAnomaly={!!alert} isOffline={offline} />}
              </div>
              <div className="flex-1 min-w-0 text-[10px]">
                {offline ? <span className="text-amber-400">Offline • lease expired</span>
                  : alert ? <span className={critical ? 'text-rose-400' : 'text-amber-300'}>{alert.message}</span>
                  : <span className="text-zinc-600">Nominal</span>}
              </div>

              {view.isControllable ? (
                <div className="flex items-center gap-1.5 shrink-0" onClick={e => e.stopPropagation()}>
                  <span
                    className={clsx('px-1.5 py-0.5 rounded text-[10px] font-bold border w-10 text-center',
                      view.reportedState === true ? 'border-emerald-800 text-emerald-400 bg-emerald-950/40' : view.reportedState === false ? 'border-zinc-700 text-zinc-400' : 'border-zinc-800 text-zinc-600',
                      syncing && 'border-dashed')}
                    title={syncing ? 'Applied locally - awaiting device acknowledgement' : 'State reported by the device'}
                  >
                    {view.reportedState === null ? '--' : view.reportedState ? 'ON' : 'OFF'}
                  </span>
                  <span className="w-4 flex justify-center" aria-label={syncing ? 'Syncing with device' : undefined}>
                    {syncing && <Loader2 size={12} className="animate-spin text-cyan-400" />}
                  </span>
                  <>
                      <button
                        disabled={view.reportedState === true}
                        onClick={() => onCommand([{ macAddress: mac, targetState: true }])}
                        className="flex items-center gap-1 px-2 py-1 rounded-sm border border-emerald-800 text-emerald-300 hover:bg-emerald-950 disabled:opacity-30 disabled:cursor-not-allowed cursor-pointer"
                        aria-label={`Turn ${view.deviceName} on`}
                      >
                        <Power size={11} /> ON
                      </button>
                      <button
                        disabled={view.reportedState === false}
                        onClick={() => onCommand([{ macAddress: mac, targetState: false }])}
                        className="flex items-center gap-1 px-2 py-1 rounded-sm border border-zinc-700 text-zinc-300 hover:bg-zinc-800 disabled:opacity-30 disabled:cursor-not-allowed cursor-pointer"
                        aria-label={`Turn ${view.deviceName} off`}
                      >
                        <PowerOff size={11} /> OFF
                      </button>
                  </>
                </div>
              ) : (
                <span className="text-[10px] text-zinc-600 shrink-0 w-[150px] text-right">Read-only sensor</span>
              )}
            </div>
          );
        })}
      </div>
      <div className="text-[10px] text-zinc-600">{filtered.length} of {devices.length} device(s) shown</div>
    </section>
  );
};
