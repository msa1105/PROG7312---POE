import React, { useState } from 'react';
import { api } from '../services/api';
import { Play, AlertTriangle } from 'lucide-react';

export const TelemetrySimulator: React.FC<{ devices: any[] }> = ({ devices }) => {
  const [simulating, setSimulating] = useState(false);

  const injectSpike = async () => {
    if (devices.length === 0) return;
    const randomDevice = devices[Math.floor(Math.random() * devices.length)].device;
    
    // Inject a value way above the mean (assuming mean ~50, we send 500)
    await api.ingestFloatTelemetry({
      macAddress: randomDevice.macAddress,
      value: 500 + Math.random() * 50,
      metricUnit: 'kW'
    });
  };

  const startNormalTelemetry = () => {
    setSimulating(true);
    const interval = setInterval(async () => {
      for (const d of devices) {
        // Normal payload around 50
        await api.ingestFloatTelemetry({
          macAddress: d.device.macAddress,
          value: 50 + (Math.random() * 10 - 5),
          metricUnit: 'kW'
        });
      }
    }, 2000); // match grid refresh rate

    // Stop after 30 seconds for safety
    setTimeout(() => {
      clearInterval(interval);
      setSimulating(false);
    }, 30000);
  };

  return (
    <div className="bg-slate-800/50 border border-slate-700 p-6 rounded-xl mt-6">
      <h3 className="text-lg font-semibold text-slate-100 mb-4 flex items-center gap-2">
        <Play size={20} className="text-emerald-400" />
        Telemetry Simulator
      </h3>
      <p className="text-sm text-slate-400 mb-4">
        Inject test data into the API to visualize the rolling Z-score outlier detection and lease-time dropouts.
      </p>
      
      <div className="flex gap-4">
        <button 
          onClick={startNormalTelemetry}
          disabled={simulating || devices.length === 0}
          className="bg-emerald-600 hover:bg-emerald-700 disabled:opacity-50 text-white font-medium py-2 px-4 rounded transition-colors"
        >
          {simulating ? 'Simulating (30s)...' : 'Start Normal Telemetry'}
        </button>

        <button 
          onClick={injectSpike}
          disabled={devices.length === 0}
          className="flex items-center gap-2 bg-red-600 hover:bg-red-700 disabled:opacity-50 text-white font-medium py-2 px-4 rounded transition-colors"
        >
          <AlertTriangle size={18} />
          Inject Data Spike (|Z| &gt; 3)
        </button>
      </div>
      
      {devices.length === 0 && (
        <p className="text-xs text-amber-400 mt-2">Register a device first to use the simulator.</p>
      )}
    </div>
  );
};
