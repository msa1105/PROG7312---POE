import { useState } from 'react';
import { ChevronRight, Radio, RotateCcw, CheckCheck, NotebookPen } from 'lucide-react';
import { clsx } from 'clsx';
import { Sparkline } from './Sparkline';
import type { DeviceView } from '../../types/telemetry';
import { api, getApiErrorMessage } from '../../services/api';
import { categoryLabel, formatDuration, formatValue, liveSecondsSinceLastPacket } from '../../utils/telemetry';

type SensorCardProps = {
  deviceData: DeviceView;
  history: number[];
  fetchedAt: number;
  now: number;
  onOpenDrawer: (macAddress: string) => void;
  onRefresh: () => Promise<void> | void;
  onReportIncident: (macAddress: string) => void;
};

export const SensorCard = ({ deviceData, history, fetchedAt, now, onOpenDrawer, onRefresh, onReportIncident }: SensorCardProps) => {
  const { device, latestTelemetry, status, activeAlerts } = deviceData;
  const [actionFeedback, setActionFeedback] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Lease-time heartbeat: the gateway marks a node offline after two missed transmission windows
  const isOffline = !status.isOnline;
  const secondsOffline = liveSecondsSinceLastPacket(deviceData, fetchedAt, now);

  // Latched alerts persist until acknowledged (HITL triage), so short spikes are never missed between polls
  const anomalyAlert = activeAlerts.find(a => a.kind === 'Anomaly');
  const heartbeatAlert = activeAlerts.find(a => a.kind === 'HeartbeatLost');
  const isAnomaly = !!anomalyAlert;
  const needsTriage = activeAlerts.length > 0 || isOffline;

  const unit = latestTelemetry?.metricUnit ?? '';
  const mean = latestTelemetry?.rollingMean;
  const sigma = latestTelemetry?.rollingStdDev;

  const runAction = async (label: string, action: () => Promise<string>) => {
    setBusy(true);
    try {
      setActionFeedback(await action());
      await onRefresh();
    } catch (err) {
      setActionFeedback(`${label} failed: ${getApiErrorMessage(err, 'request failed')}`);
    }
    setBusy(false);
    setTimeout(() => setActionFeedback(null), 5000);
  };

  const handlePing = () => runAction('Ping', async () => {
    const result = await api.pingDevice(device.macAddress);
    return result.reachable
      ? `Ping OK • last packet ${result.secondsSinceLastPacket?.toFixed(1)}s ago • ${result.lastSignalStrengthDbm ?? '--'} dBm`
      : `No response • lease (${result.leaseSeconds}s) expired • last ${result.lastSignalStrengthDbm ?? '--'} dBm, ${result.lastBatteryPercent ?? '--'}% batt`;
  });

  const handleRestart = () => runAction('Restart', async () => {
    await api.restartIngestionBuffer(device.macAddress);
    return 'Ingestion buffer & rolling baseline restarted';
  });

  const handleAcknowledge = () => {
    const notes = prompt('Diagnostic notes for the audit trail:', isAnomaly ? 'Spike reviewed; confirmed transient.' : 'Heartbeat loss reviewed.');
    if (notes === null) return;
    runAction('Acknowledge', async () => {
      const result = await api.acknowledgeAlerts(device.macAddress, notes);
      return `${result.acknowledgedCount} alert(s) acknowledged`;
    });
  };

  const stop = (handler: () => void) => (e: React.MouseEvent) => {
    e.stopPropagation();
    handler();
  };

  return (
    <div
      onClick={() => onOpenDrawer(device.macAddress)}
      className={clsx(
        "flex flex-col p-4 rounded-sm border transition-all cursor-pointer relative",
        isOffline
          ? "border-amber-500/70 bg-amber-950/10 hover:bg-amber-950/20"
          : isAnomaly
            ? "border-rose-500 bg-zinc-900/60 hover:bg-zinc-900/80 shadow-[0_0_15px_rgba(244,63,94,0.15)]"
            : heartbeatAlert
              ? "border-amber-700/60 bg-zinc-900/60 hover:bg-zinc-900/80"
              : "border-zinc-800 bg-zinc-900/60 hover:bg-zinc-800/80"
      )}
    >
      {/* Header */}
      <div className="flex flex-wrap justify-between items-start gap-2 mb-3 border-b border-zinc-800/50 pb-2.5">
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-1.5 font-mono text-zinc-100 text-xs">
            <span className="font-bold tracking-wider">{device.macAddress}</span>
            <span className={clsx("px-1.5 py-0.5 text-[9px] font-bold rounded-sm uppercase tracking-wide",
              device.category === 0 ? "bg-cyan-950 text-cyan-400 border border-cyan-800/60" :
              device.category === 1 ? "bg-yellow-950 text-yellow-400 border border-yellow-800/60" :
              "bg-purple-950 text-purple-400 border border-purple-800/60"
            )}>
              {categoryLabel(device.category)}
            </span>
          </div>
          <p className="text-[11px] text-zinc-500 mt-1 truncate" title={device.deploymentLocation}>
            Location: {device.deploymentLocation}
          </p>
        </div>

        <div className="shrink-0">
          {isOffline ? (
            <span className="text-[9px] font-mono font-bold bg-amber-950 text-amber-400 px-1.5 py-0.5 rounded-sm border border-amber-900">
              OFFLINE
            </span>
          ) : isAnomaly ? (
            <span className="text-[9px] font-mono font-bold bg-rose-950 text-rose-400 px-1.5 py-0.5 rounded-sm border border-rose-900 animate-pulse">
              OUTLIER
            </span>
          ) : heartbeatAlert ? (
            <span className="text-[9px] font-mono font-bold bg-amber-950/60 text-amber-400 px-1.5 py-0.5 rounded-sm border border-amber-900">
              RECOVERED
            </span>
          ) : (
            <span className="text-[9px] font-mono bg-zinc-800 text-zinc-400 px-1.5 py-0.5 rounded-sm border border-zinc-700/60">
              NOMINAL
            </span>
          )}
        </div>
      </div>

      {/* Body: Values */}
      <div className="flex justify-between items-end mb-2">
        <div>
          <p className="text-[10px] text-zinc-500 uppercase tracking-wider mb-1">Current Value</p>
          <div className="text-3xl font-mono text-zinc-100 tabular-nums">
            {formatValue(latestTelemetry)}
            <span className="text-sm text-zinc-500 ml-1">{unit || '-'}</span>
          </div>
        </div>
        <div className="text-right">
          <p className="text-[10px] text-zinc-500 uppercase tracking-wider mb-1">Rolling Baseline (μ ± 3σ)</p>
          <div className="text-xs font-mono text-zinc-400 tabular-nums">
            {mean != null && sigma != null
              ? <>μ: {mean.toFixed(2)} {unit} | σ: {sigma.toFixed(2)}</>
              : typeof latestTelemetry?.value === 'boolean' ? 'Digital state (no baseline)' : 'Warming up baseline…'}
          </div>
        </div>
      </div>

      {/* Sparkline */}
      <Sparkline data={history} isAnomaly={isAnomaly} isOffline={isOffline} />

      {/* Footer Alerts */}
      {isOffline ? (
        <div className="mt-4 text-xs font-mono text-amber-500 flex justify-between items-center bg-amber-950/30 p-2 rounded-sm border border-amber-900/50">
          <span>DISCONNECTED: {secondsOffline !== null ? formatDuration(secondsOffline) : 'never seen'}</span>
          <span className="text-[10px] text-amber-600">lease {status.leaseSeconds}s</span>
        </div>
      ) : anomalyAlert ? (
        <div className="mt-4 text-xs font-mono text-rose-400 flex justify-between items-center gap-2 bg-rose-950/30 p-2 rounded-sm border border-rose-900/50">
          <span>
            {(anomalyAlert.deviation ?? 0) >= 0 ? '▲ +' : '▼ '}{(anomalyAlert.deviation ?? 0).toFixed(2)} {unit} deviation from mean
          </span>
          <span className="text-[10px] font-bold">Z = {anomalyAlert.zScore?.toFixed(2)}σ</span>
        </div>
      ) : heartbeatAlert ? (
        <div className="mt-4 text-xs font-mono text-amber-400 bg-amber-950/20 p-2 rounded-sm border border-amber-900/40">
          Heartbeat lost at {new Date(heartbeatAlert.raisedAt).toLocaleTimeString()} — acknowledge to clear
        </div>
      ) : (
        <div className="mt-4 text-xs text-zinc-500 flex justify-between items-center">
          <span className="font-mono">Last Packet: {secondsOffline !== null ? Math.floor(secondsOffline) : 0}s ago</span>
          <span className="flex items-center gap-1 hover:text-zinc-300 transition-colors">
            Inspect Packet <ChevronRight size={14} />
          </span>
        </div>
      )}

      {/* Inline Human-in-the-Loop triage actions (only when the node needs attention) */}
      {needsTriage && (
        <div className="mt-2 flex flex-wrap gap-1.5 text-[10px] font-mono">
          <button disabled={busy} onClick={stop(handlePing)} className="flex items-center gap-1 px-2 py-1 rounded-sm border border-zinc-700 bg-zinc-950 text-zinc-300 hover:bg-zinc-800 disabled:opacity-50 cursor-pointer">
            <Radio size={11} /> Ping
          </button>
          <button disabled={busy} onClick={stop(handleRestart)} className="flex items-center gap-1 px-2 py-1 rounded-sm border border-zinc-700 bg-zinc-950 text-zinc-300 hover:bg-zinc-800 disabled:opacity-50 cursor-pointer">
            <RotateCcw size={11} /> Restart Buffer
          </button>
          {activeAlerts.length > 0 && (
            <button disabled={busy} onClick={stop(handleAcknowledge)} className="flex items-center gap-1 px-2 py-1 rounded-sm border border-emerald-800 bg-emerald-950/40 text-emerald-300 hover:bg-emerald-950 disabled:opacity-50 cursor-pointer">
              <CheckCheck size={11} /> Acknowledge
            </button>
          )}
          <button onClick={stop(() => onReportIncident(device.macAddress))} className="flex items-center gap-1 px-2 py-1 rounded-sm border border-purple-800 bg-purple-950/40 text-purple-300 hover:bg-purple-950 cursor-pointer">
            <NotebookPen size={11} /> Log Incident
          </button>
        </div>
      )}
      {actionFeedback && (
        <p className="mt-1.5 text-[10px] font-mono text-zinc-400" onClick={e => e.stopPropagation()}>{actionFeedback}</p>
      )}
    </div>
  );
};
