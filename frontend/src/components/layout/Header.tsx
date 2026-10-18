import { useState, useEffect } from 'react';
import { Server, Activity, Plus, ArrowLeft, ShieldAlert } from 'lucide-react';
import { getApiErrorMessage } from '../../services/api';

type HeaderProps = {
  onReturnToLanding: () => void;
  onRegisterClick: () => void;
  onBurst: () => Promise<string>;
  onInjectSpike: () => Promise<string>;
  onDropHeartbeat: () => Promise<string>;
  onReset: () => Promise<string>;
  onToggleTriage?: () => void;
  activeTriageCount?: number;
  isBackendOnline?: boolean;
};

export const Header = ({ 
  onReturnToLanding, 
  onRegisterClick, 
  onBurst, 
  onInjectSpike, 
  onDropHeartbeat, 
  onReset,
  onToggleTriage,
  activeTriageCount = 0,
  isBackendOnline = true
}: HeaderProps) => {
  const [time, setTime] = useState(new Date().toISOString().substring(11, 19));
  const [toast, setToast] = useState<{msg: string, type: 'success' | 'error' | 'info'} | null>(null);

  const showToast = (msg: string, type: 'success' | 'error' | 'info') => {
    setToast({ msg, type });
    setTimeout(() => setToast(null), 3500);
  };

  const handleAction = async (actionName: string, actionFn: () => Promise<string>) => {
    try {
      showToast(await actionFn(), 'success');
    } catch (err: unknown) {
      showToast(`${actionName} Failed: ${getApiErrorMessage(err, 'Request failed')}`, 'error');
    }
  };

  useEffect(() => {
    const int = setInterval(() => {
      setTime(new Date().toISOString().substring(11, 19));
    }, 1000);
    return () => clearInterval(int);
  }, []);

  return (
    <header className="border-b border-zinc-800 bg-zinc-950 sticky top-0 z-50">
      <div className="px-4 py-2 flex items-center justify-between border-b border-zinc-800/50 text-xs font-mono">
        <div className="flex items-center gap-3">
          <button
            onClick={onReturnToLanding}
            className="flex items-center gap-1.5 px-2.5 py-1 bg-zinc-900 hover:bg-zinc-800 text-zinc-300 border border-zinc-700 rounded transition-colors"
            title="Return to Gateway Startup Menu"
          >
            <ArrowLeft size={13} />
            <span className="font-bold">Gateway Menu</span>
          </button>

          <div className="text-zinc-300 font-bold uppercase tracking-widest flex items-center gap-2">
            <Server size={14} className="text-zinc-500" />
            Smart-X Core // Node Ingestion Engine
          </div>
        </div>
        
        <div className="flex items-center gap-6">
          {onToggleTriage && (
            <button
              onClick={onToggleTriage}
              className="flex items-center gap-1.5 px-2 py-0.5 bg-purple-950/60 hover:bg-purple-900 border border-purple-800 text-purple-300 rounded transition-colors text-xs font-bold"
            >
              <ShieldAlert size={12} className="text-purple-400" />
              <span>Incident Triage</span>
              {activeTriageCount > 0 && (
                <span className="bg-purple-500 text-zinc-950 px-1 rounded-full text-[10px] font-extrabold">
                  {activeTriageCount}
                </span>
              )}
            </button>
          )}

          <div className="flex items-center gap-2 text-zinc-400">
            <span className={`h-2 w-2 rounded-full animate-pulse ${isBackendOnline ? 'bg-emerald-500' : 'bg-rose-500'}`}></span>
            SYSTEM STATUS: {isBackendOnline ? 'ONLINE' : <span className="text-rose-400 font-bold">GATEWAY UNREACHABLE</span>}
          </div>
          <div className="text-zinc-500 hidden sm:block">
            UTC: {time}
          </div>
        </div>
      </div>
      
      {/* Simulation Ribbon */}
      <div className="px-4 py-2 bg-zinc-900/30 flex flex-wrap items-center justify-between gap-2 text-xs font-mono">
        <div className="text-zinc-500 flex items-center gap-2">
          <Activity size={12} />
          Simulation Control Panel
        </div>
        <div className="flex flex-wrap items-center gap-2 relative">
          {toast && (
            <div className={`absolute -top-10 right-0 px-3 py-1 rounded-sm text-xs font-bold border whitespace-nowrap z-50 ${toast.type === 'success' ? 'bg-emerald-950 border-emerald-500 text-emerald-400' : 'bg-rose-950 border-rose-500 text-rose-400'}`}>
              {toast.msg}
            </div>
          )}
          <button onClick={() => handleAction('Burst', onBurst)} className="px-3 py-1 bg-zinc-800 hover:bg-zinc-700 text-emerald-400 border border-emerald-900 rounded-sm transition-colors cursor-pointer">
            Burst 5,000 Pkts
          </button>
          <button onClick={() => handleAction('Spike', onInjectSpike)} className="px-3 py-1 bg-zinc-800 hover:bg-zinc-700 text-rose-400 border border-rose-900 rounded-sm transition-colors cursor-pointer">
            Inject +4σ Spike
          </button>
          <button onClick={() => handleAction('Drop', onDropHeartbeat)} className="px-3 py-1 bg-zinc-800 hover:bg-zinc-700 text-amber-400 border border-amber-900 rounded-sm transition-colors cursor-pointer">
            Drop Node Heartbeat
          </button>
          <button onClick={() => handleAction('Reset', onReset)} className="px-3 py-1 bg-zinc-800 hover:bg-zinc-700 text-slate-300 border border-slate-700 rounded-sm transition-colors cursor-pointer">
            Reset Baseline
          </button>
          
          <div className="w-px h-6 bg-zinc-700 mx-2 hidden md:block"></div>
          
          <button 
            onClick={onRegisterClick}
            className="px-3 py-1 bg-zinc-200 hover:bg-white text-zinc-900 font-bold rounded-sm transition-colors flex items-center gap-1 cursor-pointer"
          >
            <Plus size={14} /> Register Node
          </button>
        </div>
      </div>
    </header>
  );
};
