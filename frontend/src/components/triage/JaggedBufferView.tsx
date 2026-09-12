import { Layers } from 'lucide-react';

export const JaggedBufferView = ({ telemetry }: { telemetry: any }) => {
  const val = telemetry?.value || 0;
  
  // Mock jagged array (T[][]) visualization
  const ch0 = (val * 0.98).toFixed(1) + ", " + (val * 0.99).toFixed(1) + ", " + (val * 1.01).toFixed(1);
  const ch1 = (val * 1.02).toFixed(1) + ", " + (val * 0.95).toFixed(1) + ", ";

  return (
    <section className="space-y-2">
      <h3 className="text-xs font-bold text-zinc-500 uppercase flex items-center gap-2">
        <Layers size={14} /> Jagged Buffer History (T[][])
      </h3>
      <div className="bg-zinc-900 border border-zinc-800 rounded-sm p-4 text-xs text-zinc-300 font-mono">
        <div className="flex gap-4 mb-2">
          <span className="text-zinc-600">Queue Channel 0:</span>
          <span className="text-zinc-400">[ {ch0} ]</span>
        </div>
        <div className="flex gap-4">
          <span className="text-zinc-600">Queue Channel 1:</span>
          <span className="text-zinc-400">[ {ch1} <span className="text-rose-400 bg-rose-950/50 px-1">{val.toFixed(1)}</span> ]</span>
          <span className="text-zinc-500 italic ml-auto">(Spike Target)</span>
        </div>
      </div>
    </section>
  );
};
