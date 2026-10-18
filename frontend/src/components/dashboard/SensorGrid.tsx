import { useState, useEffect } from 'react';
import { SensorCard } from './SensorCard';
import type { DeviceView } from '../../types/telemetry';
import type { SensorHistory } from '../../state/gatewayContext';

type SensorGridProps = {
  devices: DeviceView[];
  history: SensorHistory;
  receivedAt: Record<string, number>;
  onOpenDrawer: (macAddress: string) => void;
  onRefresh: () => Promise<void> | void;
  onReportIncident: (macAddress: string) => void;
};

export const SensorGrid = ({ devices, history, receivedAt, onOpenDrawer, onRefresh, onReportIncident }: SensorGridProps) => {
  // Ticks every second so offline duration timers keep counting between polls
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const timeInterval = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timeInterval);
  }, []);

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 2xl:grid-cols-3 gap-4">
      {devices.length === 0 && (
        <div className="col-span-full text-center p-12 border border-zinc-800 border-dashed rounded-sm text-zinc-500 font-mono text-sm">
          No active sensor nodes detected. Register a node to begin ingestion.
        </div>
      )}

      {devices.map((d) => (
        <SensorCard
          key={d.device.macAddress}
          deviceData={d}
          history={history[d.device.macAddress]?.values ?? []}
          fetchedAt={receivedAt[d.device.macAddress] ?? now}
          now={now}
          onOpenDrawer={onOpenDrawer}
          onRefresh={onRefresh}
          onReportIncident={onReportIncident}
        />
      ))}
    </div>
  );
};
