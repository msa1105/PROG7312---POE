import type { DeviceView, TelemetrySnapshot } from '../types/telemetry';

/** Splits a "Facility > Zone > Node" deployment path into trimmed segments. */
export const pathSegments = (location: string): string[] =>
  location.split('>').map(s => s.trim()).filter(Boolean);

/** Canonical " > "-joined form, matching the node IDs produced by the backend topology engine. */
export const normalizePath = (location: string): string => pathSegments(location).join(' > ');

/** The aggregator path a device hangs off (all segments except the device's own name). */
export const parentPath = (location: string): string => {
  const segments = pathSegments(location);
  return segments.length > 1 ? segments.slice(0, -1).join(' > ') : 'Unassigned';
};

/** True when a device sits anywhere beneath the given zone / facility node. */
export const isWithinZone = (location: string, zoneId: string): boolean => {
  const parent = parentPath(location);
  return parent === zoneId || parent.startsWith(`${zoneId} > `);
};

/** Seconds since the last packet, advanced locally between polls. */
export const liveSecondsSinceLastPacket = (view: DeviceView, fetchedAt: number, now: number): number | null => {
  const base = view.status.secondsSinceLastPacket;
  if (base === null || base === undefined) return null;
  return base + Math.max(0, (now - fetchedAt) / 1000);
};

export const formatDuration = (totalSeconds: number): string => {
  const s = Math.max(0, Math.floor(totalSeconds));
  const hours = Math.floor(s / 3600);
  const minutes = Math.floor((s % 3600) / 60);
  const seconds = s % 60;
  const mmss = `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
  return hours > 0 ? `${hours}:${mmss}` : mmss;
};

/** Compact human duration for SRE timing benchmarks (MTTD / MTTT). */
export const formatBenchmark = (seconds: number | null | undefined): string => {
  if (seconds === null || seconds === undefined) return '--';
  if (seconds < 60) return `${seconds.toFixed(1)}s`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ${Math.round(seconds % 60)}s`;
  return `${Math.floor(minutes / 60)}h ${minutes % 60}m`;
};

export const formatValue = (snapshot: TelemetrySnapshot | null | undefined): string => {
  if (!snapshot || snapshot.value === null || snapshot.value === undefined) return '--';
  return typeof snapshot.value === 'number' ? snapshot.value.toFixed(2) : String(snapshot.value).toUpperCase();
};

export const categoryLabel = (category: number): string =>
  category === 0 ? 'Environmental' : category === 1 ? 'Power' : 'Actuator';
