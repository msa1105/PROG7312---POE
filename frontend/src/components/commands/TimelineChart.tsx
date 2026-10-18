import { useEffect, useState } from 'react';
import { LineChart } from 'lucide-react';
import { api } from '../../services/api';
import type { DeviceView, TimelinePoint } from '../../types/telemetry';

const WIDTH = 640;
const HEIGHT = 180;
const PAD = { left: 44, right: 12, top: 12, bottom: 22 };
const WINDOWS = [5, 15, 30];

type TimelineChartProps = {
  device: DeviceView | null;
  windowMinutes: number;
  onWindowChange: (minutes: number) => void;
};

/**
 * Historical timeline for one device, served pre-sorted (and downsampled) from the device's SortedDictionary.
 * Drawn as an inline SVG polyline; anomalous buckets are marked and moisture sensors show the 20% threshold.
 */
export const TimelineChart = ({ device, windowMinutes, onWindowChange }: TimelineChartProps) => {
  const [points, setPoints] = useState<TimelinePoint[]>([]);
  const [fetchedAt, setFetchedAt] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const mac = device?.device.macAddress;

  useEffect(() => {
    if (!mac) return;
    let cancelled = false;
    const load = async () => {
      try {
        const data = await api.getTimeline(mac, windowMinutes, 120);
        if (!cancelled) {
          setPoints(data);
          setFetchedAt(Date.now());
          setError(null);
        }
      } catch {
        if (!cancelled) setError('Timeline unavailable.');
      }
    };
    load();
    const interval = setInterval(load, 3000);
    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, [mac, windowMinutes]);

  const numeric = points.filter(p => p.value !== null) as (TimelinePoint & { value: number })[];
  const isMoisture = device?.device.category === 0;
  const isDigital = device?.device.category === 2;

  const now = fetchedAt;
  const from = now - windowMinutes * 60_000;
  let min = Math.min(...numeric.map(p => p.min ?? p.value), isMoisture ? 20 : Infinity);
  let max = Math.max(...numeric.map(p => p.max ?? p.value), isMoisture ? 20 : -Infinity);
  if (isDigital) { min = 0; max = 1; }
  if (!Number.isFinite(min) || !Number.isFinite(max)) { min = 0; max = 1; }
  const span = max - min || 1;
  min -= span * 0.08;
  max += span * 0.08;

  const x = (iso: string) => PAD.left + ((new Date(iso).getTime() - from) / (now - from)) * (WIDTH - PAD.left - PAD.right);
  const y = (v: number) => PAD.top + (1 - (v - min) / (max - min)) * (HEIGHT - PAD.top - PAD.bottom);
  const path = numeric.map((p, i) => `${i === 0 ? 'M' : 'L'}${x(p.timestamp).toFixed(1)},${y(p.value).toFixed(1)}`).join(' ');

  return (
    <section className="bg-zinc-900 border border-zinc-800 rounded-sm p-4 font-mono text-xs" aria-label="Device timeline">
      <div className="flex flex-wrap items-center justify-between gap-2 mb-2">
        <h3 className="text-zinc-300 font-bold uppercase tracking-wider flex items-center gap-2">
          <LineChart size={14} className="text-emerald-400" />
          Timeline {device ? `• ${device.deviceName}` : ''}
        </h3>
        <div className="flex items-center gap-1">
          {WINDOWS.map(w => (
            <button
              key={w}
              onClick={() => onWindowChange(w)}
              className={`px-2 py-0.5 rounded border text-[10px] cursor-pointer ${w === windowMinutes ? 'bg-zinc-800 border-zinc-600 text-zinc-100' : 'border-zinc-800 text-zinc-500 hover:text-zinc-300'}`}
            >
              {w}m
            </button>
          ))}
        </div>
      </div>

      {!device ? (
        <p className="text-zinc-600 italic py-10 text-center">Select a device to view its timeline.</p>
      ) : error ? (
        <p className="text-rose-400 py-10 text-center">{error}</p>
      ) : numeric.length === 0 ? (
        <p className="text-zinc-600 italic py-10 text-center">No readings in this window yet.</p>
      ) : (
        <svg viewBox={`0 0 ${WIDTH} ${HEIGHT}`} className="w-full h-auto" role="img" aria-label={`${device.deviceName} readings over the last ${windowMinutes} minutes`}>
          {[0, 0.5, 1].map(f => {
            const v = min + (max - min) * f;
            return (
              <g key={f}>
                <line x1={PAD.left} x2={WIDTH - PAD.right} y1={y(v)} y2={y(v)} stroke="#27272a" strokeWidth={1} />
                <text x={PAD.left - 6} y={y(v) + 3} textAnchor="end" fontSize={9} fill="#71717a">
                  {isDigital ? (f === 1 ? 'ON' : f === 0 ? 'OFF' : '') : v.toFixed(0)}
                </text>
              </g>
            );
          })}
          {isMoisture && (
            <g>
              <line x1={PAD.left} x2={WIDTH - PAD.right} y1={y(20)} y2={y(20)} stroke="#f59e0b" strokeDasharray="4 4" strokeWidth={1} />
              <text x={WIDTH - PAD.right} y={y(20) - 4} textAnchor="end" fontSize={9} fill="#f59e0b">20% threshold</text>
            </g>
          )}
          <path d={path} fill="none" stroke={isDigital ? '#a78bfa' : '#a1a1aa'} strokeWidth={1.5} />
          {numeric.filter(p => p.isAnomaly).map(p => (
            <circle key={p.timestamp} cx={x(p.timestamp)} cy={y(p.max ?? p.value)} r={3} fill="#f43f5e" />
          ))}
          <text x={PAD.left} y={HEIGHT - 6} fontSize={9} fill="#71717a">-{windowMinutes} min</text>
          <text x={WIDTH - PAD.right} y={HEIGHT - 6} textAnchor="end" fontSize={9} fill="#71717a">now</text>
        </svg>
      )}
      {device && numeric.length > 0 && (
        <div className="mt-1 text-[10px] text-zinc-600">
          {points.reduce((n, p) => n + p.samples, 0).toLocaleString()} readings in {points.length} sorted bucket(s) • unit {device.latestTelemetry?.metricUnit ?? '-'}
        </div>
      )}
    </section>
  );
};
