import { useEffect, useState } from 'react';
import { History, GitBranch, BatteryMedium, Wifi } from 'lucide-react';
import { clsx } from 'clsx';
import { api } from '../../services/api';
import type { DeviceView, TelemetrySnapshot } from '../../types/telemetry';
import { Sparkline } from '../dashboard/Sparkline';
import { formatValue, normalizePath, parentPath, pathSegments } from '../../utils/telemetry';

type DeviceContextPanelProps = {
  deviceData: DeviceView;
  allDevices: DeviceView[];
  onSelectDevice: (macAddress: string) => void;
};

/**
 * Progressive-disclosure context for one node: preceding buffered readings, last known
 * transmission metadata, its place in the deployment hierarchy and adjacent sensor trends.
 */
export const DeviceContextPanel = ({ deviceData, allDevices, onSelectDevice }: DeviceContextPanelProps) => {
  const { device, latestTelemetry, status } = deviceData;
  const [history, setHistory] = useState<TelemetrySnapshot[]>([]);
  const [adjacentHistory, setAdjacentHistory] = useState<Record<string, number[]>>({});

  const zone = parentPath(device.deploymentLocation);
  const ownPath = normalizePath(device.deploymentLocation);
  const adjacent = allDevices.filter(d => d.device.macAddress !== device.macAddress && parentPath(d.device.deploymentLocation) === zone);
  const downstream = allDevices.filter(d => normalizePath(d.device.deploymentLocation).startsWith(`${ownPath} > `));
  const adjacentKey = adjacent.map(d => d.device.macAddress).join(',');

  useEffect(() => {
    const load = async () => {
      try {
        const macs = adjacentKey ? adjacentKey.split(',') : [];
        const [own, ...neighbours] = await Promise.all([
          api.getDeviceHistory(device.macAddress),
          ...macs.map(mac => api.getDeviceHistory(mac)),
        ]);
        setHistory(own);
        setAdjacentHistory(Object.fromEntries(macs.map((mac, i) => [
          mac,
          neighbours[i].map(s => s.value).filter((v): v is number => typeof v === 'number'),
        ])));
      } catch (err) {
        console.error('Failed to load node history:', err);
      }
    };
    load();
    const interval = setInterval(load, 3000);
    return () => clearInterval(interval);
  }, [device.macAddress, adjacentKey]);

  const numericHistory = history.map(s => s.value).filter((v): v is number => typeof v === 'number');
  const preceding = history.slice(-8).reverse();

  return (
    <>
      {/* Last known transmission metadata */}
      <section className="space-y-2">
        <h3 className="text-xs font-bold text-zinc-500 uppercase flex items-center gap-2">
          <Wifi size={14} className="text-cyan-400" /> Link Health (Last Known)
        </h3>
        <div className="bg-zinc-900 border border-zinc-800 rounded-sm p-4 text-xs grid grid-cols-2 gap-3">
          <div>
            <div className="text-[10px] text-zinc-500 uppercase">Signal Strength</div>
            <div className="text-zinc-200">{latestTelemetry?.signalStrengthDbm != null ? `${latestTelemetry.signalStrengthDbm} dBm` : '--'}</div>
          </div>
          <div>
            <div className="text-[10px] text-zinc-500 uppercase flex items-center gap-1"><BatteryMedium size={11} /> Battery</div>
            <div className={clsx(latestTelemetry?.batteryPercent != null && latestTelemetry.batteryPercent < 20 ? 'text-amber-400' : 'text-zinc-200')}>
              {latestTelemetry?.batteryPercent != null ? `${latestTelemetry.batteryPercent.toFixed(1)}%` : '--'}
            </div>
          </div>
          <div>
            <div className="text-[10px] text-zinc-500 uppercase">Heartbeat Lease</div>
            <div className={status.isOnline ? 'text-emerald-400' : 'text-amber-400'}>
              {status.isOnline ? 'Valid' : 'Expired'} ({status.leaseSeconds}s = 2 windows)
            </div>
          </div>
          <div>
            <div className="text-[10px] text-zinc-500 uppercase">Packet Delivery (60s)</div>
            <div className="text-zinc-200">{status.packetDeliveryPercent != null ? `${status.packetDeliveryPercent.toFixed(1)}%` : '--'} • {status.packetsReceived} rx</div>
          </div>
        </div>
      </section>

      {/* Preceding historical buffer readings */}
      <section className="space-y-2">
        <h3 className="text-xs font-bold text-zinc-500 uppercase flex items-center gap-2">
          <History size={14} className="text-emerald-400" /> Preceding Readings (BoundedTelemetryCollection, last {history.length})
        </h3>
        <div className="bg-zinc-900 border border-zinc-800 rounded-sm p-4 text-xs space-y-3">
          {numericHistory.length > 1 && <Sparkline data={numericHistory} isAnomaly={history.some(s => s.isAnomaly)} isOffline={!status.isOnline} />}
          {preceding.length === 0 ? (
            <p className="text-zinc-600 italic">No readings buffered for this node yet.</p>
          ) : (
            <table className="w-full text-[11px] tabular-nums">
              <thead>
                <tr className="text-zinc-500 text-left">
                  <th className="font-normal pb-1">Time (UTC)</th>
                  <th className="font-normal pb-1 text-right">Value</th>
                  <th className="font-normal pb-1 text-right">Z-Score</th>
                </tr>
              </thead>
              <tbody>
                {preceding.map(s => (
                  <tr key={s.packetId} className={s.isAnomaly ? 'text-rose-400 font-bold' : 'text-zinc-300'}>
                    <td>{new Date(s.timestamp).toISOString().substring(11, 19)}</td>
                    <td className="text-right">{formatValue(s)} {s.metricUnit}</td>
                    <td className="text-right">{s.zScore != null ? `${s.zScore > 0 ? '+' : ''}${s.zScore.toFixed(2)}σ` : '--'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </section>

      {/* Deployment hierarchy, adjacent sensors and downstream dependencies */}
      <section className="space-y-2">
        <h3 className="text-xs font-bold text-zinc-500 uppercase flex items-center gap-2">
          <GitBranch size={14} className="text-purple-400" /> Topology Context
        </h3>
        <div className="bg-zinc-900 border border-zinc-800 rounded-sm p-4 text-xs space-y-3">
          <div className="text-[11px] text-zinc-400">
            {pathSegments(device.deploymentLocation).map((segment, i, all) => (
              <span key={i}>
                <span className={i === all.length - 1 ? 'text-zinc-100 font-bold' : ''}>{segment}</span>
                {i < all.length - 1 && <span className="text-zinc-600"> › </span>}
              </span>
            ))}
          </div>

          <div>
            <div className="text-[10px] text-zinc-500 uppercase mb-1.5">Adjacent Sensors ({zone})</div>
            {adjacent.length === 0 ? (
              <p className="text-zinc-600 italic">No other nodes in this sub-zone.</p>
            ) : (
              <ul className="space-y-1.5">
                {adjacent.map(d => {
                  const degraded = !d.status.isOnline || d.activeAlerts.length > 0;
                  return (
                    <li key={d.device.macAddress}>
                      <button
                        onClick={() => onSelectDevice(d.device.macAddress)}
                        className="w-full flex items-center gap-3 bg-zinc-950 border border-zinc-800 hover:border-zinc-600 rounded-sm px-2 py-1.5 text-left cursor-pointer"
                      >
                        <span className={clsx('h-2 w-2 rounded-full shrink-0', !d.status.isOnline ? 'bg-amber-500' : d.activeAlerts.length > 0 ? 'bg-rose-500' : 'bg-emerald-500/50')} />
                        <span className="min-w-0 flex-1">
                          <span className={clsx('block truncate', degraded ? 'text-zinc-100' : 'text-zinc-400')}>{pathSegments(d.device.deploymentLocation).at(-1)}</span>
                          <span className="block text-[10px] text-zinc-600">{d.device.macAddress}</span>
                        </span>
                        <span className="w-20 shrink-0">
                          {(adjacentHistory[d.device.macAddress]?.length ?? 0) > 1 && (
                            <Sparkline data={adjacentHistory[d.device.macAddress]} isAnomaly={d.activeAlerts.length > 0} isOffline={!d.status.isOnline} />
                          )}
                        </span>
                        <span className="text-zinc-300 tabular-nums shrink-0">{formatValue(d.latestTelemetry)} {d.latestTelemetry?.metricUnit}</span>
                      </button>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>

          <div>
            <div className="text-[10px] text-zinc-500 uppercase mb-1">Downstream Dependencies</div>
            {downstream.length === 0 ? (
              <p className="text-zinc-600 italic">None — leaf node.</p>
            ) : (
              <p className="text-zinc-300">{downstream.map(d => d.device.macAddress).join(', ')}</p>
            )}
          </div>
        </div>
      </section>
    </>
  );
};
