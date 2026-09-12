import { useState, useEffect } from 'react';

import { Activity, Zap, Droplets, AlertTriangle, Clock } from 'lucide-react';
import { clsx } from 'clsx';
import { AnomalyDrawer } from './AnomalyDrawer';

export const TelemetryDashboard: React.FC<{ devices: any[] }> = ({ devices }) => {
  const [selectedDevice, setSelectedDevice] = useState<any>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [currentTime, setCurrentTime] = useState(new Date());

  useEffect(() => {
    const timeInterval = setInterval(() => setCurrentTime(new Date()), 1000);
    return () => clearInterval(timeInterval);
  }, []);

  const openDrawer = (deviceData: any) => {
    setSelectedDevice(deviceData);
    setDrawerOpen(true);
  };

  return (
    <div className="space-y-4">
      <div className="flex justify-between items-center">
        <h2 className="text-xl font-bold text-slate-100 flex items-center gap-2">
          <Activity size={24} className="text-blue-400" />
          Live Telemetry Grid
        </h2>
        <div className="text-xs text-slate-400">
          Auto-refreshing every 2s
        </div>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
        {devices.length === 0 && (
          <div className="col-span-full text-center p-8 border border-slate-800 border-dashed rounded-xl text-slate-500">
            No sensor nodes registered yet.
          </div>
        )}
        
        {devices.map((d, i) => {
          const telemetry = d.latestTelemetry;
          const isAnomaly = telemetry?.isAnomaly;
          
          // Lease-time timeout calculation (10 seconds)
          let isOffline = false;
          let secondsOffline = 0;
          if (telemetry?.timestamp) {
            const lastSeen = new Date(telemetry.timestamp);
            secondsOffline = Math.floor((currentTime.getTime() - lastSeen.getTime()) / 1000);
            if (secondsOffline > 10) {
              isOffline = true;
            }
          } else {
            isOffline = true; // Never seen data
          }

          return (
            <div 
              key={i} 
              onClick={() => openDrawer(d)}
              className={clsx(
                "p-5 rounded-xl border transition-all cursor-pointer relative overflow-hidden",
                isOffline 
                  ? "border-amber-500/50 bg-amber-950/20 hover:bg-amber-900/30" 
                  : isAnomaly 
                    ? "border-red-500 bg-red-950/40 hover:bg-red-900/50 shadow-[0_0_15px_rgba(239,68,68,0.2)]" 
                    : "border-slate-700 bg-slate-800/40 hover:bg-slate-700/60"
              )}
            >
              <div className="flex justify-between items-start mb-4">
                <div>
                  <h3 className="font-semibold text-slate-200">{d.device.deploymentLocation}</h3>
                  <p className="text-xs text-slate-400 font-mono">{d.device.macAddress}</p>
                </div>
                {d.device.category === 0 && <Droplets size={20} className="text-cyan-400" />}
                {d.device.category === 1 && <Zap size={20} className="text-yellow-400" />}
                {d.device.category === 2 && <Activity size={20} className="text-purple-400" />}
              </div>

              {isOffline ? (
                <div className="flex items-center gap-2 text-amber-500 font-medium bg-amber-500/10 p-2 rounded animate-pulse">
                  <Clock size={16} />
                  <span>OFFLINE: {secondsOffline}s ago</span>
                </div>
              ) : telemetry ? (
                <div>
                  <div className="text-3xl font-light text-white flex items-baseline gap-1">
                    {telemetry.value.toFixed(2)} <span className="text-sm text-slate-400">{telemetry.metricUnit}</span>
                  </div>
                  
                  {isAnomaly && (
                    <div className="mt-3 flex items-center gap-1 text-xs text-red-400 font-semibold bg-red-500/10 px-2 py-1 rounded w-fit">
                      <AlertTriangle size={14} />
                      SPIKE DETECTED |Z| &gt; 3.0
                    </div>
                  )}
                </div>
              ) : (
                <div className="text-sm text-slate-500 italic">Waiting for data...</div>
              )}
            </div>
          );
        })}
      </div>

      {drawerOpen && selectedDevice && (
        <AnomalyDrawer 
          deviceData={selectedDevice} 
          onClose={() => setDrawerOpen(false)} 
        />
      )}
    </div>
  );
};
