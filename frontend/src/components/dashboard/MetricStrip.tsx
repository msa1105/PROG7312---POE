import { Activity, LayoutGrid, Zap, SignalHigh } from 'lucide-react';

type MetricStripProps = {
  devices: any[];
};

export const MetricStrip = ({ devices }: MetricStripProps) => {
  // Mock calculated KPIs
  const activeCount = devices.length;
  const disconnectedCount = devices.filter(d => {
    if (!d.latestTelemetry?.timestamp) return true;
    return (new Date().getTime() - new Date(d.latestTelemetry.timestamp).getTime()) > 10000;
  }).length;
  
  const availability = activeCount === 0 ? 0 : ((activeCount - disconnectedCount) / activeCount) * 100;
  
  // Random simulation for throughput and buffers based on active devices
  const throughput = activeCount > 0 ? (activeCount * 0.5).toFixed(1) : '0.0';
  const bufferDepth = activeCount > 0 ? (activeCount * 2) : 0;
  const anomalyCount = devices.filter(d => d.latestTelemetry?.isAnomaly).length;

  return (
    <div className="grid grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
      <div className="bg-zinc-900 border border-zinc-800 p-4 rounded-sm flex items-center justify-between">
        <div>
          <p className="text-xs text-zinc-500 uppercase tracking-widest mb-1">Throughput Rate</p>
          <h3 className="text-2xl font-mono text-zinc-200">{throughput} <span className="text-sm text-zinc-600">pkts/s</span></h3>
        </div>
        <Activity size={24} className="text-zinc-700" />
      </div>

      <div className="bg-zinc-900 border border-zinc-800 p-4 rounded-sm flex items-center justify-between">
        <div>
          <p className="text-xs text-zinc-500 uppercase tracking-widest mb-1">Buffer Depth (T[][])</p>
          <h3 className="text-2xl font-mono text-zinc-200">{bufferDepth} <span className="text-sm text-zinc-600">items</span></h3>
          <div className="w-full bg-zinc-950 h-1 mt-2 rounded-full overflow-hidden">
            <div className="bg-blue-500 h-full" style={{ width: `${Math.min(bufferDepth, 100)}%` }}></div>
          </div>
        </div>
        <LayoutGrid size={24} className="text-zinc-700" />
      </div>

      <div className="bg-zinc-900 border border-zinc-800 p-4 rounded-sm flex items-center justify-between">
        <div>
          <p className="text-xs text-zinc-500 uppercase tracking-widest mb-1">Anomaly Rate (|Z| &gt; 3)</p>
          <h3 className="text-2xl font-mono text-rose-400">{anomalyCount} <span className="text-sm text-zinc-600">spikes/min</span></h3>
        </div>
        <Zap size={24} className={anomalyCount > 0 ? "text-rose-500/50" : "text-zinc-700"} />
      </div>

      <div className="bg-zinc-900 border border-zinc-800 p-4 rounded-sm flex items-center justify-between">
        <div>
          <p className="text-xs text-zinc-500 uppercase tracking-widest mb-1">Mesh Availability</p>
          <h3 className="text-2xl font-mono text-emerald-400">{availability.toFixed(1)} <span className="text-sm text-zinc-600">%</span></h3>
        </div>
        <SignalHigh size={24} className="text-emerald-500/50" />
      </div>
    </div>
  );
};
