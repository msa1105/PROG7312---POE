import { useState } from 'react';
import { Layers, Zap, ShieldOff, Droplets, PlugZap, Timer } from 'lucide-react';
import { clsx } from 'clsx';
import { api, getApiErrorMessage } from '../../services/api';
import type { DeviceView, PipelineState } from '../../types/telemetry';

const RACK_B = '24:6F:28:9A:12:02';
const SUBSTATION_MAIN = '24:6F:28:9A:12:03';
const GRID_2 = '24:6F:28:9A:12:04';
const BACKLOG_SIZE = 1500;
const DEMO_COST_MS = 3;

type PipelineMonitorProps = {
  pipeline: PipelineState | null;
  deviceMap: Record<string, DeviceView>;
};

const formatMs = (ms: number) => (ms >= 1000 ? `${(ms / 1000).toFixed(2)} s` : `${ms.toFixed(1)} ms`);

/**
 * Visualises the two ingestion lanes (Queue&lt;T&gt; FIFO vs PriorityQueue) and the HashSet alert statistics,
 * and provides the controls to simulate a high-priority alert hitting the API.
 */
export const PipelineMonitor = ({ pipeline, deviceMap }: PipelineMonitorProps) => {
  const [feedback, setFeedback] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const run = async (action: () => Promise<string>) => {
    setBusy(true);
    try {
      setFeedback(await action());
    } catch (err) {
      setFeedback(getApiErrorMessage(err, 'Request failed'));
    }
    setBusy(false);
  };

  const name = (mac: string) => deviceMap[mac]?.deviceName ?? mac;
  const queue = pipeline?.queue;
  const alerts = pipeline?.alerts;
  const standardFill = queue ? Math.min(100, (queue.standard.depth / Math.max(1, BACKLOG_SIZE)) * 100) : 0;

  const simulateMoisture = () => run(async () => {
    await api.simulationCritical(RACK_B, 'MoistureCrash');
    return `Moisture crash scheduled on ${name(RACK_B)} — the device will report it on its next transmission.`;
  });

  const simulateSurge = () => run(async () => {
    await api.simulationCritical(SUBSTATION_MAIN, 'PowerSurge');
    return `Power spike scheduled on ${name(SUBSTATION_MAIN)}.`;
  });

  // Slow the consumer, upload a backlog, then send a critical reading behind it to prove the bypass
  const runBacklogDemo = () => run(async () => {
    await api.setProcessingCost(DEMO_COST_MS);
    await api.simulationFlood(GRID_2, BACKLOG_SIZE);
    await api.simulationCritical(RACK_B, 'MoistureCrash');
    setTimeout(() => { api.setProcessingCost(0).catch(() => undefined); }, 12000);
    return `Backlog of ${BACKLOG_SIZE.toLocaleString()} readings queued behind a ${DEMO_COST_MS} ms/packet consumer; a critical reading follows it.`;
  });

  return (
    <section className="bg-zinc-900 border border-zinc-800 rounded-sm p-4 font-mono text-xs space-y-4" aria-label="Ingestion pipeline">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-zinc-300 font-bold uppercase tracking-wider flex items-center gap-2">
          <Layers size={14} className="text-cyan-400" /> Ingestion Pipeline
        </h3>
        {queue && queue.simulatedProcessingCostMs > 0 && (
          <span className="text-[10px] px-2 py-0.5 rounded border border-amber-800 text-amber-400 flex items-center gap-1">
            <Timer size={10} /> Demo slow consumer: {queue.simulatedProcessingCostMs} ms / standard packet
          </span>
        )}
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
        {/* Standard FIFO lane */}
        <div className="bg-zinc-950 border border-zinc-800 rounded-sm p-3">
          <div className="flex justify-between text-[10px] text-zinc-500 uppercase mb-1">
            <span>Standard lane • Queue&lt;T&gt; (FIFO)</span>
            <span>{queue ? `${queue.standard.processed.toLocaleString()} processed` : '--'}</span>
          </div>
          <div className="text-2xl text-zinc-100 tabular-nums">{queue ? queue.standard.depth.toLocaleString() : '--'} <span className="text-xs text-zinc-500">queued</span></div>
          <div className="w-full bg-zinc-900 h-1.5 mt-2 rounded-full overflow-hidden">
            <div className="bg-blue-500 h-full transition-all" style={{ width: `${standardFill}%` }} />
          </div>
          <div className="mt-2 text-[10px] text-zinc-500">
            Avg wait {queue ? formatMs(queue.standard.averageWaitMs) : '--'} • peak {queue?.standard.peakDepth.toLocaleString() ?? '--'} • rejected {queue?.rejected ?? 0}
          </div>
        </div>

        {/* Priority lane */}
        <div className="bg-zinc-950 border border-rose-900/60 rounded-sm p-3">
          <div className="flex justify-between text-[10px] text-zinc-500 uppercase mb-1">
            <span className="text-rose-400">Priority lane • PriorityQueue</span>
            <span>{queue ? `${queue.priority.processed.toLocaleString()} processed` : '--'}</span>
          </div>
          <div className="text-2xl text-zinc-100 tabular-nums">{queue ? queue.priority.depth : '--'} <span className="text-xs text-zinc-500">queued</span></div>
          <div className="mt-2 text-[10px] text-zinc-500">
            Last critical wait <strong className="text-rose-300">{queue ? formatMs(queue.priority.lastWaitMs) : '--'}</strong> • avg {queue ? formatMs(queue.priority.averageWaitMs) : '--'}
          </div>
          <div className="mt-1 text-[10px] text-zinc-600">Ordered by severity (Critical → High), then arrival.</div>
        </div>
      </div>

      {/* Bypass evidence */}
      <div className={clsx('rounded-sm p-3 border text-[11px]', queue?.largestBypass ? 'bg-rose-950/20 border-rose-900/60 text-rose-200' : 'bg-zinc-950 border-zinc-800 text-zinc-500')}>
        {queue?.largestBypass ? (
          <>
            <div className="flex items-center gap-1.5 font-bold text-rose-300"><Zap size={12} /> Largest priority bypass</div>
            <div className="mt-1">
              {name(queue.largestBypass.macAddress)}: “{queue.largestBypass.reason}” overtook <strong>{queue.largestBypass.overtookStandardPackets.toLocaleString()}</strong> queued
              standard packet(s) and was processed after {formatMs(queue.largestBypass.waitMs)} at {new Date(queue.largestBypass.processedAt).toLocaleTimeString()}.
            </div>
            {queue.lastBypass && queue.lastBypass.sequence !== queue.largestBypass.sequence && (
              <div className="mt-1 text-[10px] text-rose-300/70">
                Latest: {name(queue.lastBypass.macAddress)} overtook {queue.lastBypass.overtookStandardPackets.toLocaleString()} packet(s) after {formatMs(queue.lastBypass.waitMs)}.
              </div>
            )}
          </>
        ) : (
          'No priority bypass yet — run the backlog demo to see a critical alert overtake the standard queue.'
        )}
      </div>

      {/* HashSet statistics */}
      <div className="grid grid-cols-3 gap-2 text-center">
        <div className="bg-zinc-950 border border-zinc-800 rounded-sm p-2">
          <div className="text-[10px] text-zinc-500 uppercase">Active error states</div>
          <div className="text-lg text-zinc-100">{alerts?.activeAlerts ?? '--'}</div>
        </div>
        <div className="bg-zinc-950 border border-zinc-800 rounded-sm p-2">
          <div className="text-[10px] text-zinc-500 uppercase">Disconnected (HashSet)</div>
          <div className="text-lg text-amber-400">{alerts?.disconnectedNodes ?? '--'}</div>
        </div>
        <div className="bg-zinc-950 border border-zinc-800 rounded-sm p-2" title="Duplicate alerts / duplicate disconnect detections rejected by HashSet.Add">
          <div className="text-[10px] text-zinc-500 uppercase flex items-center justify-center gap-1"><ShieldOff size={10} /> Duplicates blocked</div>
          <div className="text-lg text-emerald-400">
            {alerts ? (alerts.duplicateAlertsSuppressed + alerts.duplicateDisconnectsSuppressed).toLocaleString() : '--'}
          </div>
        </div>
      </div>
      {pipeline && pipeline.disconnectedNodes.length > 0 && (
        <div className="flex flex-wrap gap-1.5 text-[10px]">
          {pipeline.disconnectedNodes.map(mac => (
            <span key={mac} className="px-1.5 py-0.5 rounded border border-amber-900 bg-amber-950/40 text-amber-300">{name(mac)} offline</span>
          ))}
        </div>
      )}

      {/* High-priority alert simulation */}
      <div className="border-t border-zinc-800 pt-3">
        <div className="text-[10px] text-zinc-500 uppercase mb-2">Simulate high-priority alert (sent by the device simulator over HTTP)</div>
        <div className="flex flex-wrap gap-2">
          <button disabled={busy} onClick={simulateMoisture} className="flex items-center gap-1 px-2.5 py-1.5 bg-zinc-800 hover:bg-zinc-700 border border-rose-900 text-rose-300 rounded-sm disabled:opacity-50 cursor-pointer">
            <Droplets size={12} /> Critical moisture drop
          </button>
          <button disabled={busy} onClick={simulateSurge} className="flex items-center gap-1 px-2.5 py-1.5 bg-zinc-800 hover:bg-zinc-700 border border-rose-900 text-rose-300 rounded-sm disabled:opacity-50 cursor-pointer">
            <PlugZap size={12} /> Severe power spike
          </button>
          <button disabled={busy} onClick={runBacklogDemo} className="flex items-center gap-1 px-2.5 py-1.5 bg-zinc-800 hover:bg-zinc-700 border border-cyan-900 text-cyan-300 rounded-sm disabled:opacity-50 cursor-pointer">
            <Layers size={12} /> Backlog + critical bypass demo
          </button>
        </div>
        {feedback && <p className="mt-2 text-[11px] text-zinc-400">{feedback}</p>}
      </div>
    </section>
  );
};
