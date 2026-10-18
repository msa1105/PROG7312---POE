import { useEffect, useRef, useState } from 'react';
import { Activity, LayoutGrid, Zap, SignalHigh } from 'lucide-react';
import { api } from '../../services/api';
import type { GatewayHealth } from '../../types/telemetry';
import { formatBenchmark } from '../../utils/telemetry';

export const MetricStrip = () => {
  // Facility-level KPIs straight from the gateway (overview first)
  const [health, setHealth] = useState<GatewayHealth | null>(null);
  const [throughput, setThroughput] = useState<number | null>(null);
  const lastSample = useRef<{ packets: number; at: number } | null>(null);

  useEffect(() => {
    const poll = async () => {
      try {
        const data = await api.getGatewayHealth();
        const at = Date.now();
        const previous = lastSample.current;
        if (previous && at > previous.at && data.totalTelemetryPackets >= previous.packets) {
          setThroughput((data.totalTelemetryPackets - previous.packets) / ((at - previous.at) / 1000));
        }
        lastSample.current = { packets: data.totalTelemetryPackets, at };
        setHealth(data);
      } catch (err) {
        console.error('Failed to fetch gateway KPIs:', err);
      }
    };

    poll();
    const interval = setInterval(poll, 2000);
    return () => clearInterval(interval);
  }, []);

  const buffer = health?.bufferMetrics;
  const bufferFill = buffer && buffer.capacity > 0 ? (buffer.currentCount / buffer.capacity) * 100 : 0;
  const jaggedDepth = health?.jaggedBufferedReadings;
  const anomalyRate = health?.anomaliesLastMinute ?? 0;

  return (
    <div className="grid grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
      <div className="bg-zinc-900 border border-zinc-800 p-4 rounded-sm flex items-center justify-between">
        <div>
          <p className="text-xs text-zinc-500 uppercase tracking-widest mb-1">Throughput Rate</p>
          <h3 className="text-2xl font-mono text-zinc-200">{throughput !== null ? throughput.toFixed(1) : '--'} <span className="text-sm text-zinc-600">pkts/s</span></h3>
          <p className="text-[10px] font-mono text-zinc-600 mt-1">{health ? health.totalTelemetryPackets.toLocaleString() : '--'} packets total</p>
        </div>
        <Activity size={24} className="text-zinc-700" />
      </div>

      <div className="bg-zinc-900 border border-zinc-800 p-4 rounded-sm flex items-center justify-between">
        <div className="flex-1 min-w-0 pr-2">
          <p className="text-xs text-zinc-500 uppercase tracking-widest mb-1">Buffer Depth (T[][])</p>
          <h3 className="text-2xl font-mono text-zinc-200">{jaggedDepth != null ? jaggedDepth.toLocaleString() : '--'} <span className="text-sm text-zinc-600">items</span></h3>
          <div className="w-full bg-zinc-950 h-1 mt-2 rounded-full overflow-hidden">
            <div className="bg-blue-500 h-full" style={{ width: `${Math.min(bufferFill, 100)}%` }}></div>
          </div>
          <p className="text-[10px] font-mono text-zinc-600 mt-1">
            Flushed ring: {buffer ? `${buffer.currentCount.toLocaleString()}/${buffer.capacity.toLocaleString()}` : '--'} • {buffer ? buffer.droppedPackets.toLocaleString() : '--'} overwritten
          </p>
        </div>
        <LayoutGrid size={24} className="text-zinc-700 shrink-0" />
      </div>

      <div className="bg-zinc-900 border border-zinc-800 p-4 rounded-sm flex items-center justify-between">
        <div>
          <p className="text-xs text-zinc-500 uppercase tracking-widest mb-1">Anomaly Rate (|Z| &gt; 3)</p>
          <h3 className={`text-2xl font-mono ${anomalyRate > 0 ? 'text-rose-400' : 'text-zinc-200'}`}>{anomalyRate} <span className="text-sm text-zinc-600">spikes/min</span></h3>
          <p className="text-[10px] font-mono text-zinc-600 mt-1">{health?.activeAlertCount ?? 0} alert(s) awaiting triage</p>
        </div>
        <Zap size={24} className={anomalyRate > 0 ? "text-rose-500/50" : "text-zinc-700"} />
      </div>

      <div className="bg-zinc-900 border border-zinc-800 p-4 rounded-sm flex items-center justify-between">
        <div>
          <p className="text-xs text-zinc-500 uppercase tracking-widest mb-1">Mesh Availability</p>
          <h3 className={`text-2xl font-mono ${health?.availabilityPercent != null && health.availabilityPercent < 100 ? 'text-amber-400' : 'text-emerald-400'}`}>
            {health?.availabilityPercent != null ? health.availabilityPercent.toFixed(1) : '--'} <span className="text-sm text-zinc-600">%</span>
          </h3>
          <p className="text-[10px] font-mono text-zinc-600 mt-1">
            Delivery {health?.packetDeliveryPercent != null ? `${health.packetDeliveryPercent.toFixed(1)}%` : '--'} • MTTD {formatBenchmark(health?.meanTimeToDetectSeconds)}
          </p>
        </div>
        <SignalHigh size={24} className="text-emerald-500/50" />
      </div>
    </div>
  );
};
