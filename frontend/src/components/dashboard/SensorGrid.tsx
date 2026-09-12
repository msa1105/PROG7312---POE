import { useState, useEffect, useRef } from 'react';
import { SensorCard } from './SensorCard';

type SensorGridProps = {
  devices: any[];
  onOpenDrawer: (data: any) => void;
};

export const SensorGrid = ({ devices, onOpenDrawer }: SensorGridProps) => {
  const [currentTime, setCurrentTime] = useState(new Date());
  
  // Track historical values for sparkline using a ref to avoid resetting on render
  // Keyed by macAddress
  const historyRef = useRef<Record<string, number[]>>({});

  useEffect(() => {
    const timeInterval = setInterval(() => setCurrentTime(new Date()), 1000);
    return () => clearInterval(timeInterval);
  }, []);

  // Update history buffer
  useEffect(() => {
    devices.forEach(d => {
      const mac = d.device.macAddress;
      if (!historyRef.current[mac]) {
        historyRef.current[mac] = [];
      }
      
      const val = d.latestTelemetry?.value;
      if (val !== undefined && val !== null) {
        // Only push if it's a new timestamp, or just push every poll if we assume constant streaming
        // For simplicity, we just push the latest value every render if it changed (or just keep a window of 30)
        const currentHist = historyRef.current[mac];
        // simple dedup by checking if the value changed. In a real app we'd check packetId or timestamp
        if (currentHist.length === 0 || currentHist[currentHist.length - 1] !== val) {
          currentHist.push(val);
          // Keep last 30 values for sparkline
          if (currentHist.length > 30) {
            currentHist.shift();
          }
        }
      }
    });
  }, [devices]);

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
      {devices.length === 0 && (
        <div className="col-span-full text-center p-12 border border-zinc-800 border-dashed rounded-sm text-zinc-500 font-mono text-sm">
          No active sensor nodes detected. Register a node to begin ingestion.
        </div>
      )}
      
      {devices.map((d, i) => (
        <SensorCard 
          key={i} 
          deviceData={d}
          history={historyRef.current[d.device.macAddress] || []}
          currentTime={currentTime}
          onOpenDrawer={onOpenDrawer}
        />
      ))}
    </div>
  );
};
