import { useState, useEffect } from 'react';
import { Calculator, Check, Cpu } from 'lucide-react';
import { api } from '../../services/api';
import type { PowerAggregationResult } from '../../types/telemetry';

export const OperatorOverloadBox = ({ device, telemetry }: { device: any; telemetry: any }) => {
  if (device.category !== 1) return null; // Only show for PowerConsumption

  const val = typeof telemetry?.value === 'number' ? telemetry.value : 400.0;
  const [referenceMeterWatts, setReferenceMeterWatts] = useState(380.20);
  const [result, setResult] = useState<PowerAggregationResult | null>(null);
  const [loading, setLoading] = useState(false);

  const calculateWithCSharpBackend = async (m1: number, m2: number) => {
    setLoading(true);
    try {
      const data = await api.aggregatePowerMetrics(m1, m2);
      setResult(data);
    } catch (err) {
      console.error('Operator overload API failed:', err);
    }
    setLoading(false);
  };

  useEffect(() => {
    calculateWithCSharpBackend(val, referenceMeterWatts);
  }, [val, referenceMeterWatts]);

  return (
    <section className="space-y-2">
      <div className="flex items-center justify-between">
        <h3 className="text-xs font-bold text-zinc-500 uppercase flex items-center gap-2">
          <Calculator size={14} className="text-yellow-400" /> 
          Operator Overloading Engine (C# Struct Overloads)
        </h3>
        <span className="text-[10px] text-emerald-400 font-mono bg-emerald-950/60 border border-emerald-800 px-1.5 py-0.5 rounded flex items-center gap-1">
          <Cpu size={10} /> .NET 10 Value-Type Struct
        </span>
      </div>

      <div className="bg-zinc-900 border border-zinc-800 rounded-sm p-4 text-xs text-zinc-300 font-mono space-y-3">
        <div className="text-[11px] text-zinc-400">
          Evaluates <code className="text-yellow-300">Meter3 = Meter1 + Meter2</code> and <code className="text-rose-300">Delta = Meter1 - Meter2</code> directly in C# using overloaded struct operators:
        </div>

        {/* Dynamic Reference Meter Slider / Input */}
        <div className="bg-zinc-950 p-2.5 rounded border border-zinc-800/80 flex items-center justify-between gap-4">
          <span className="text-zinc-500 text-[11px]">Compare Against Meter 2:</span>
          <div className="flex items-center gap-2">
            <input
              type="number"
              step="5"
              value={referenceMeterWatts}
              onChange={e => setReferenceMeterWatts(parseFloat(e.target.value) || 0)}
              className="w-24 bg-zinc-900 border border-zinc-700 px-2 py-1 text-right text-zinc-200 rounded text-xs"
            />
            <span className="text-zinc-400">W</span>
          </div>
        </div>

        {/* Arithmetic Result (+ and -) */}
        <div className="grid grid-cols-1 md:grid-cols-2 gap-2">
          <div className="bg-zinc-950 p-2.5 rounded border border-zinc-800">
            <div className="text-[10px] text-zinc-500 uppercase tracking-wider mb-1">
              Direct Aggregation (Meter1 + Meter2)
            </div>
            <div className="text-base font-bold text-emerald-400">
              {loading ? 'Evaluating in C#...' : result ? `${result.aggregatedWatts.toFixed(2)} W` : 'Calculating...'}
            </div>
            <div className="text-[10px] text-zinc-500 mt-1">
              [{val.toFixed(2)} W] + [{referenceMeterWatts.toFixed(2)} W]
            </div>
          </div>

          <div className="bg-zinc-950 p-2.5 rounded border border-zinc-800">
            <div className="text-[10px] text-zinc-500 uppercase tracking-wider mb-1">
              Delta Variance (Meter1 - Meter2)
            </div>
            <div className="text-base font-bold text-rose-400">
              {loading ? 'Evaluating in C#...' : result ? `${result.deltaWatts.toFixed(2)} W` : 'Calculating...'}
            </div>
            <div className="text-[10px] text-zinc-500 mt-1">
              Absolute struct difference: |Δ|
            </div>
          </div>
        </div>

        {/* Logical Comparison Operators (> and ==) */}
        <div className="bg-zinc-950/60 p-2.5 rounded border border-zinc-800/80 flex flex-wrap items-center justify-between gap-2 text-[11px]">
          <div className="flex items-center gap-1.5">
            <span className="text-zinc-500">Threshold Operator (&gt;):</span>
            <span className={result?.isMeter1Greater ? "text-amber-400 font-bold" : "text-zinc-400 font-bold"}>
              {result ? (result.isMeter1Greater ? "Meter 1 > Meter 2 (TRUE)" : "Meter 1 <= Meter 2 (FALSE)") : '--'}
            </span>
          </div>
          <div className="flex items-center gap-1.5">
            <span className="text-zinc-500">Equality Operator (==):</span>
            <span className={result?.areEqual ? "text-cyan-400 font-bold" : "text-zinc-500 font-bold"}>
              {result ? (result.areEqual ? "EQUAL (TRUE)" : "NOT EQUAL (FALSE)") : '--'}
            </span>
          </div>
        </div>

        <div className="text-[10px] text-zinc-500 text-right flex items-center justify-end gap-1">
          <Check size={12} className="text-emerald-400" />
          Verified: {result?.executionEngine || '.NET 10 Minimal API Engine'}
        </div>
      </div>
    </section>
  );
};
