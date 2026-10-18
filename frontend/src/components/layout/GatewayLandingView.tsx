import { useState, useEffect } from 'react';
import { 
  Activity, 
  History, 
  RadioTower, 
  ShieldCheck, 
  ArrowRight, 
  Lock, 
  Cpu, 
  Layers, 
  Calculator, 
  FileCode, 
  CheckCircle2
} from 'lucide-react';
import type { GatewayHealth } from '../../types/telemetry';
import { api, API_BASE_URL } from '../../services/api';
import { formatDuration } from '../../utils/telemetry';

type GatewayLandingProps = {
  onEnterIngestion: () => void;
  onEnterCommandStream: () => void;
  registeredDeviceCount: number;
};

export const GatewayLandingView = ({ onEnterIngestion, onEnterCommandStream, registeredDeviceCount }: GatewayLandingProps) => {
  const [health, setHealth] = useState<GatewayHealth | null>(null);
  const [latency, setLatency] = useState<number | null>(null);
  const [lockedModal, setLockedModal] = useState<{ title: string; desc: string; milestone: string } | null>(null);
  const [healthReceivedAt, setHealthReceivedAt] = useState(() => Date.now());
  const [now, setNow] = useState(() => Date.now());

  // Uptime counter ticks locally every second between health polls
  useEffect(() => {
    const tick = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(tick);
  }, []);

  useEffect(() => {
    const checkGateway = async () => {
      const t0 = performance.now();
      try {
        const data = await api.getGatewayHealth();
        setLatency(Math.round(performance.now() - t0));
        setHealth(data);
        setHealthReceivedAt(Date.now());
      } catch (err) {
        console.warn('Backend offline or initializing:', err);
        setHealth(null);
        setLatency(null);
      }
    };

    checkGateway();
    const interval = setInterval(checkGateway, 4000);
    return () => clearInterval(interval);
  }, []);

  return (
    <div className="min-h-[calc(100vh-60px)] flex flex-col justify-between p-6 max-w-7xl mx-auto font-sans">
      {/* Top Banner: Gateway Connection Status */}
      <div className="bg-zinc-900/70 border border-zinc-800 rounded-sm p-4 mb-8 flex flex-wrap items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <div className="relative flex h-3 w-3">
            <span className={`animate-ping absolute inline-flex h-full w-full rounded-full ${health ? 'bg-emerald-400 opacity-75' : 'bg-amber-400 opacity-75'}`}></span>
            <span className={`relative inline-flex rounded-full h-3 w-3 ${health ? 'bg-emerald-500' : 'bg-amber-500'}`}></span>
          </div>
          <div>
            <div className="text-xs font-mono uppercase tracking-widest text-zinc-400">
              Gateway Connection Status: <span className={health ? "text-emerald-400 font-bold" : "text-amber-400 font-bold"}>{health ? "ONLINE // 100% OPERATIONAL" : "CONNECTING TO .NET BACKEND..."}</span>
            </div>
            <div className="text-[11px] text-zinc-500 font-mono">
              Target API: {API_BASE_URL.startsWith('http') ? API_BASE_URL : `${window.location.origin}${API_BASE_URL}`} • REST over HTTP/1.1 • {health?.gatewayVersion ?? '.NET 10 Minimal API'}
            </div>
          </div>
        </div>

        <div className="flex items-center gap-6 font-mono text-xs">
          <div className="text-right">
            <span className="text-zinc-500">API LATENCY: </span>
            <span className="text-zinc-200 font-bold">{latency !== null ? `${latency}ms` : '--'}</span>
          </div>
          <div className="h-6 w-px bg-zinc-800"></div>
          <div className="text-right">
            <span className="text-zinc-500">ACTIVE NODES: </span>
            <span className="text-cyan-400 font-bold">
              {health ? `${health.activeDeviceCount}/${health.registeredDeviceCount}` : registeredDeviceCount} Connected
            </span>
          </div>
          <div className="h-6 w-px bg-zinc-800"></div>
          <div className="text-right">
            <span className="text-zinc-500">UPTIME: </span>
            <span className="text-zinc-200 font-bold tabular-nums">
              {health ? formatDuration(health.uptimeSeconds + (now - healthReceivedAt) / 1000) : '--'}
            </span>
          </div>
          <div className="h-6 w-px bg-zinc-800"></div>
          <div className="text-right">
            <span className="text-zinc-500">OPERATOR XP: </span>
            <span className="text-purple-400 font-bold">{health?.operatorEngagement?.operatorXp ?? '--'} XP</span>
          </div>
        </div>
      </div>

      {/* Hero Section */}
      <div className="text-center max-w-3xl mx-auto mb-10">
        <div className="inline-flex items-center gap-2 px-3 py-1 bg-zinc-900 border border-zinc-700/80 rounded-full text-xs font-mono text-zinc-300 mb-4">
          <ShieldCheck size={14} className="text-emerald-400" />
          PROG7312 PoE Part 2 • Real-Time Processing Gateway
        </div>
        <h1 className="text-4xl md:text-5xl font-extrabold tracking-tight text-zinc-100 uppercase mb-4">
          Smart-X <span className="bg-gradient-to-r from-emerald-400 via-cyan-400 to-blue-500 bg-clip-text text-transparent">Telemetry Gateway</span>
        </h1>
        <p className="text-sm md:text-base text-zinc-400 leading-relaxed">
          High-performance distributed telemetry ingestion and node lifecycle controller. 
          Engineered on .NET 10 Minimal APIs with unboxed generic packet wrappers, jagged array batching, and recursive topology validation.
        </p>
      </div>

      {/* 3 Architectural Pillars */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6 mb-12">
        {/* Pillar 1: Ingestion & Telemetry (Active) */}
        <div className="relative group rounded-md border-2 border-emerald-500/80 bg-zinc-900/90 p-6 flex flex-col justify-between shadow-[0_0_30px_rgba(16,185,129,0.12)] hover:border-emerald-400 transition-all">
          <div className="absolute -top-3 right-4 bg-emerald-950 border border-emerald-500 text-emerald-400 text-[10px] font-mono font-bold px-2 py-0.5 rounded uppercase tracking-wider">
            Part 1 • Active Ingestion
          </div>
          
          <div>
            <div className="w-12 h-12 rounded-sm bg-emerald-950/60 border border-emerald-500/40 flex items-center justify-center text-emerald-400 mb-4">
              <Activity size={24} />
            </div>

            <h2 className="text-lg font-bold text-zinc-100 uppercase tracking-wide mb-2 flex items-center gap-2">
              1. Sensor Telemetry & Ingestion
            </h2>

            <p className="text-xs text-zinc-400 leading-relaxed mb-6">
              Primary telemetry processing pipeline. Ingests raw environmental, power consumption, and actuator telemetry without runtime type-coercion overhead.
            </p>

            <div className="space-y-2 border-t border-zinc-800/80 pt-4 mb-6 font-mono text-xs text-zinc-300">
              <div className="flex items-center gap-2 text-zinc-300">
                <CheckCircle2 size={13} className="text-emerald-400 shrink-0" />
                <span><strong className="text-zinc-100">TelemetryPacket&lt;T&gt;</strong> Generic Wrapper</span>
              </div>
              <div className="flex items-center gap-2 text-zinc-300">
                <CheckCircle2 size={13} className="text-emerald-400 shrink-0" />
                <span><strong className="text-zinc-100">PowerMetric</strong> Operator Overloading (+, -, ==)</span>
              </div>
              <div className="flex items-center gap-2 text-zinc-300">
                <CheckCircle2 size={13} className="text-emerald-400 shrink-0" />
                <span><strong className="text-zinc-100">T[][] Jagged Array</strong> Batch Pipeline</span>
              </div>
              <div className="flex items-center gap-2 text-zinc-300">
                <CheckCircle2 size={13} className="text-emerald-400 shrink-0" />
                <span><strong className="text-zinc-100">Recursive</strong> Tree Topology Validation</span>
              </div>
              <div className="flex items-center gap-2 text-zinc-300">
                <CheckCircle2 size={13} className="text-emerald-400 shrink-0" />
                <span><strong className="text-zinc-100">AES-256</strong> Encrypted Log Attachments</span>
              </div>
              <div className="flex items-center gap-2 text-zinc-300">
                <CheckCircle2 size={13} className="text-emerald-400 shrink-0" />
                <span><strong className="text-zinc-100">Incident Triage List</strong> &amp; Gamified XP</span>
              </div>
            </div>
          </div>

          <button
            onClick={onEnterIngestion}
            className="w-full bg-emerald-500 hover:bg-emerald-400 text-zinc-950 font-bold py-3 px-4 rounded-sm flex items-center justify-center gap-2 uppercase tracking-wider text-xs transition-all shadow-md group-hover:scale-[1.01]"
          >
            Launch Telemetry Gateway <ArrowRight size={16} />
          </button>
        </div>

        {/* Pillar 2: Command Stream & History (Active - Part 2) */}
        <div className="relative group rounded-md border-2 border-cyan-500/80 bg-zinc-900/90 p-6 flex flex-col justify-between shadow-[0_0_30px_rgba(6,182,212,0.12)] hover:border-cyan-400 transition-all">
          <div className="absolute -top-3 right-4 bg-cyan-950 border border-cyan-500 text-cyan-400 text-[10px] font-mono font-bold px-2 py-0.5 rounded uppercase tracking-wider">
            Part 2 • Active Command Stream
          </div>

          <div>
            <div className="w-12 h-12 rounded-sm bg-cyan-950/60 border border-cyan-500/40 flex items-center justify-center text-cyan-400 mb-4">
              <History size={24} />
            </div>

            <h2 className="text-lg font-bold text-zinc-100 uppercase tracking-wide mb-2 flex items-center gap-2">
              2. Command Stream &amp; History
            </h2>

            <p className="text-xs text-zinc-400 leading-relaxed mb-6">
              Real-time processing engine. Issues manual overrides to edge actuators, prioritises critical alerts, and predicts the next operational action.
            </p>

            <div className="space-y-2 border-t border-zinc-800/80 pt-4 mb-6 font-mono text-xs text-zinc-300">
              <div className="flex items-center gap-2">
                <CheckCircle2 size={13} className="text-cyan-400 shrink-0" />
                <span><strong className="text-zinc-100">Queue&lt;T&gt; + PriorityQueue</strong> Ingestion Lanes</span>
              </div>
              <div className="flex items-center gap-2">
                <CheckCircle2 size={13} className="text-cyan-400 shrink-0" />
                <span><strong className="text-zinc-100">Stack&lt;T&gt;</strong> Command Undo History</span>
              </div>
              <div className="flex items-center gap-2">
                <CheckCircle2 size={13} className="text-cyan-400 shrink-0" />
                <span><strong className="text-zinc-100">Dictionary / SortedDictionary</strong> Lookups &amp; Timelines</span>
              </div>
              <div className="flex items-center gap-2">
                <CheckCircle2 size={13} className="text-cyan-400 shrink-0" />
                <span><strong className="text-zinc-100">HashSet&lt;T&gt;</strong> Unique Alert States</span>
              </div>
              <div className="flex items-center gap-2">
                <CheckCircle2 size={13} className="text-cyan-400 shrink-0" />
                <span><strong className="text-zinc-100">Predictive</strong> Suggested Actions</span>
              </div>
            </div>
          </div>

          <button
            onClick={onEnterCommandStream}
            className="w-full bg-cyan-500 hover:bg-cyan-400 text-zinc-950 font-bold py-3 px-4 rounded-sm flex items-center justify-center gap-2 uppercase tracking-wider text-xs transition-all shadow-md group-hover:scale-[1.01]"
          >
            Launch Command Stream <ArrowRight size={16} />
          </button>
        </div>

        {/* Pillar 3: Network Topology & Mesh (Disabled - PoE) */}
        <div 
          onClick={() => setLockedModal({
            title: 'Pillar 3: Network Topology & Mesh Routing',
            milestone: 'Final PoE Milestone (Locked)',
            desc: 'Dynamic RF mesh multi-hop routing, AODV graph traversal, signal propagation heatmaps, and routing failover algorithms are scheduled for delivery in the Final PoE.'
          })}
          className="relative rounded-md border border-zinc-800 bg-zinc-950/70 p-6 flex flex-col justify-between opacity-75 hover:opacity-100 hover:border-zinc-700 transition-all cursor-pointer group"
        >
          <div className="absolute -top-3 right-4 bg-zinc-900 border border-zinc-700 text-zinc-400 text-[10px] font-mono px-2 py-0.5 rounded uppercase tracking-wider flex items-center gap-1">
            <Lock size={10} /> Final PoE Milestone
          </div>

          <div>
            <div className="w-12 h-12 rounded-sm bg-zinc-900 border border-zinc-800 flex items-center justify-center text-zinc-500 group-hover:text-zinc-400 mb-4">
              <RadioTower size={24} />
            </div>

            <h2 className="text-lg font-bold text-zinc-300 uppercase tracking-wide mb-2 flex items-center gap-2">
              3. Topology &amp; Mesh Routing
            </h2>

            <p className="text-xs text-zinc-500 leading-relaxed mb-6">
              Physical node spatial mesh graphing. Models dynamic wireless hop routing, packet delivery probabilities, and mesh self-healing.
            </p>

            <div className="space-y-2 border-t border-zinc-800/80 pt-4 mb-6 font-mono text-xs text-zinc-600">
              <div className="flex items-center gap-2">
                <Lock size={12} className="text-zinc-600" />
                <span>Dynamic Mesh Multi-Hop Routing</span>
              </div>
              <div className="flex items-center gap-2">
                <Lock size={12} className="text-zinc-600" />
                <span>AODV Graph Traversal Algorithms</span>
              </div>
              <div className="flex items-center gap-2">
                <Lock size={12} className="text-zinc-600" />
                <span>RF Signal Attenuation Modeling</span>
              </div>
              <div className="flex items-center gap-2">
                <Lock size={12} className="text-zinc-600" />
                <span>Autonomous Mesh Self-Healing</span>
              </div>
            </div>
          </div>

          <button
            disabled
            className="w-full bg-zinc-900 text-zinc-500 border border-zinc-800 py-3 px-4 rounded-sm flex items-center justify-center gap-2 uppercase tracking-wider text-xs cursor-not-allowed"
          >
            <Lock size={14} /> Provisioning in Final PoE
          </button>
        </div>
      </div>

      {/* Technical Architecture Summary Chips */}
      <div className="bg-zinc-900/40 border border-zinc-800/70 rounded-sm p-4 grid grid-cols-2 md:grid-cols-4 gap-4 text-xs font-mono text-zinc-400">
        <div className="flex items-center gap-2">
          <Cpu size={16} className="text-cyan-400" />
          <span>Generics: <strong className="text-zinc-200">TelemetryPacket&lt;T&gt;</strong></span>
        </div>
        <div className="flex items-center gap-2">
          <Calculator size={16} className="text-yellow-400" />
          <span>Overloading: <strong className="text-zinc-200">PowerMetric (+, -, ==)</strong></span>
        </div>
        <div className="flex items-center gap-2">
          <Layers size={16} className="text-emerald-400" />
          <span>Buffer: <strong className="text-zinc-200">double[4][] Jagged</strong></span>
        </div>
        <div className="flex items-center gap-2">
          <FileCode size={16} className="text-purple-400" />
          <span>Storage: <strong className="text-zinc-200">AES-256 Encrypted</strong></span>
        </div>
      </div>

      {/* Locked Pillar Info Modal */}
      {lockedModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-zinc-950/80 backdrop-blur-sm">
          <div className="bg-zinc-900 border border-zinc-700 rounded-md p-6 max-w-md w-full shadow-2xl font-mono text-sm animate-in zoom-in-95 duration-150">
            <div className="flex items-center gap-2 text-amber-400 text-xs uppercase tracking-widest mb-2">
              <Lock size={14} /> {lockedModal.milestone}
            </div>
            <h3 className="text-lg font-bold text-zinc-100 uppercase mb-3">
              {lockedModal.title}
            </h3>
            <p className="text-xs text-zinc-300 leading-relaxed mb-6">
              {lockedModal.desc}
            </p>
            <div className="bg-zinc-950 border border-zinc-800 p-3 rounded text-[11px] text-zinc-400 mb-6">
              Notice: Per assignment specification, this architectural pillar is formally disabled during Part 1 and will be provisioned in subsequent milestone submissions.
            </div>
            <button
              onClick={() => setLockedModal(null)}
              className="w-full bg-zinc-800 hover:bg-zinc-700 text-zinc-200 py-2.5 rounded font-bold uppercase tracking-wider text-xs transition-colors"
            >
              Acknowledge &amp; Close
            </button>
          </div>
        </div>
      )}
    </div>
  );
};
