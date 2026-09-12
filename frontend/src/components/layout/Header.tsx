import { useState, useEffect } from 'react';
import { Server, Activity, Plus } from 'lucide-react';

type HeaderProps = {
  onRegisterClick: () => void;
  onBurst: () => void;
  onInjectSpike: () => void;
  onDropHeartbeat: () => void;
  onReset: () => void;
};

export const Header = ({ onRegisterClick, onBurst, onInjectSpike, onDropHeartbeat, onReset }: HeaderProps) => {
  const [time, setTime] = useState(new Date().toISOString().substring(11, 19));

  useEffect(() => {
    const int = setInterval(() => {
      setTime(new Date().toISOString().substring(11, 19));
    }, 1000);
    return () => clearInterval(int);
  }, []);

  return (
    <header className="border-b border-zinc-800 bg-zinc-950 sticky top-0 z-50">
      <div className="px-4 py-2 flex items-center justify-between border-b border-zinc-800/50 text-xs font-mono">
        <div className="flex items-center gap-4">
          <div className="text-zinc-300 font-bold uppercase tracking-widest flex items-center gap-2">
            <Server size={14} className="text-zinc-500" />
            Smart-X Core // Node Ingestion Engine
          </div>
        </div>
        
        <div className="flex items-center gap-6">
          <div className="flex items-center gap-2 text-zinc-400">
            <span className="h-2 w-2 rounded-full bg-emerald-500 animate-pulse"></span>
            SYSTEM STATUS: ONLINE
          </div>
          <div className="text-zinc-500">
            UTC: {time}
          </div>
        </div>
      </div>
      
      {/* Simulation Ribbon */}
      <div className="px-4 py-2 bg-zinc-900/30 flex items-center justify-between text-xs font-mono">
        <div className="text-zinc-500 flex items-center gap-2">
          <Activity size={12} />
          Simulation Control Panel
        </div>
        <div className="flex gap-2">
          <button onClick={onBurst} className="px-3 py-1 bg-zinc-800 hover:bg-zinc-700 text-zinc-300 border border-zinc-700 rounded-sm transition-colors">
            Burst 5,000 Pkts
          </button>
          <button onClick={onInjectSpike} className="px-3 py-1 bg-zinc-800 hover:bg-zinc-700 text-zinc-300 border border-zinc-700 rounded-sm transition-colors">
            Inject +4σ Spike
          </button>
          <button onClick={onDropHeartbeat} className="px-3 py-1 bg-zinc-800 hover:bg-zinc-700 text-zinc-300 border border-zinc-700 rounded-sm transition-colors">
            Drop Node Heartbeat
          </button>
          <button onClick={onReset} className="px-3 py-1 bg-zinc-800 hover:bg-zinc-700 text-zinc-300 border border-zinc-700 rounded-sm transition-colors">
            Reset Baseline
          </button>
          
          <div className="w-px h-6 bg-zinc-700 mx-2"></div>
          
          <button 
            onClick={onRegisterClick}
            className="px-3 py-1 bg-zinc-200 hover:bg-white text-zinc-900 font-bold rounded-sm transition-colors flex items-center gap-1"
          >
            <Plus size={14} /> Register Node
          </button>
        </div>
      </div>
    </header>
  );
};
