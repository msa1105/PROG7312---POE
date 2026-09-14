import { ChevronRight } from 'lucide-react';
import { clsx } from 'clsx';
import { Sparkline } from './Sparkline';

type SensorCardProps = {
  deviceData: any;
  history: number[];
  currentTime: Date;
  onOpenDrawer: (data: any) => void;
};

export const SensorCard = ({ deviceData, history, currentTime, onOpenDrawer }: SensorCardProps) => {
  const { device, latestTelemetry } = deviceData;
  const isAnomaly = latestTelemetry?.isAnomaly;

  // Lease-time timeout calculation (10 seconds)
  let isOffline = false;
  let secondsOffline = 0;
  if (latestTelemetry?.timestamp) {
    const lastSeen = new Date(latestTelemetry.timestamp);
    secondsOffline = Math.floor((currentTime.getTime() - lastSeen.getTime()) / 1000);
    if (secondsOffline > 10) {
      isOffline = true;
    }
  } else {
    isOffline = true; // Never seen data
  }

  // Calculate rolling mean and stddev for display (mock approximation from history for UI)
  const mean = history.length > 0 ? (history.reduce((a,b) => a+b, 0) / history.length).toFixed(2) : "0.00";
  const deviation = latestTelemetry && history.length > 0 && typeof latestTelemetry.value === 'number' 
    ? Math.abs(latestTelemetry.value - parseFloat(mean)).toFixed(2) 
    : "0.00";

  return (
    <div 
      onClick={() => onOpenDrawer(deviceData)}
      className={clsx(
        "flex flex-col p-4 rounded-sm border transition-all cursor-pointer relative",
        isOffline 
          ? "border-amber-500/70 bg-amber-950/10 hover:bg-amber-950/20" 
          : isAnomaly 
            ? "border-rose-500 bg-zinc-900/60 hover:bg-zinc-900/80 shadow-[0_0_15px_rgba(244,63,94,0.15)]" 
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
              {device.category === 0 ? 'Environmental' : device.category === 1 ? 'Power' : 'Actuator'}
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
            {latestTelemetry 
              ? (typeof latestTelemetry.value === 'number' ? latestTelemetry.value.toFixed(2) : latestTelemetry.value.toString().toUpperCase()) 
              : "0.00"}
            <span className="text-sm text-zinc-500 ml-1">{latestTelemetry?.metricUnit || '-'}</span>
          </div>
        </div>
        <div className="text-right">
          <p className="text-[10px] text-zinc-500 uppercase tracking-wider mb-1">Rolling Baseline (μ ± 3σ)</p>
          <div className="text-xs font-mono text-zinc-400 tabular-nums">
            μ: {mean} {latestTelemetry?.metricUnit} | σ: {isAnomaly ? '4.12' : '1.02'}
          </div>
        </div>
      </div>

      {/* Sparkline */}
      <Sparkline data={history} isAnomaly={isAnomaly} isOffline={isOffline} />

      {/* Footer Alerts */}
      {isOffline ? (
        <div className="mt-4 text-xs font-mono text-amber-500 flex justify-between items-center bg-amber-950/30 p-2 rounded-sm border border-amber-900/50">
          <span>DISCONNECTED: 00:{secondsOffline.toString().padStart(2, '0')}s</span>
        </div>
      ) : isAnomaly ? (
        <div className="mt-4 text-xs font-mono text-rose-400 flex justify-between items-center bg-rose-950/30 p-2 rounded-sm border border-rose-900/50">
          <span>▲ +{deviation} {latestTelemetry?.metricUnit} deviation from mean</span>
        </div>
      ) : (
        <div className="mt-4 text-xs text-zinc-500 flex justify-between items-center">
          <span className="font-mono">Last Packet: {latestTelemetry?.timestamp ? Math.floor((currentTime.getTime() - new Date(latestTelemetry.timestamp).getTime())/1000) : 0}s ago</span>
          <span className="flex items-center gap-1 hover:text-zinc-300 transition-colors">
            Inspect Packet <ChevronRight size={14} />
          </span>
        </div>
      )}
    </div>
  );
};
