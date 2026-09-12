import { Calculator } from 'lucide-react';

export const OperatorOverloadBox = ({ device, telemetry }: { device: any, telemetry: any }) => {
  if (device.category !== 1) return null; // Only show for PowerConsumption

  const val = telemetry?.value || 0;
  const mockMeter2 = 380.20;
  const total = (val + mockMeter2).toFixed(2);
  const delta = Math.abs(val - mockMeter2).toFixed(2);

  return (
    <section className="space-y-2">
      <h3 className="text-xs font-bold text-zinc-500 uppercase flex items-center gap-2">
        <Calculator size={14} /> Operator Overloading Calculation (C# Struct Engine)
      </h3>
      <div className="bg-zinc-900 border border-zinc-800 rounded-sm p-4 text-xs text-zinc-300">
        <div className="text-zinc-500 mb-2">Sub-Zone Total = Meter1 + Meter2</div>
        <div className="font-mono bg-zinc-950 p-2 rounded-sm border border-zinc-800 mb-2 flex justify-between">
          <span>[ {val.toFixed(2)} W ] + [ {mockMeter2.toFixed(2)} W ]</span>
          <span className="text-emerald-400">= {total} W</span>
        </div>
        <div className="text-zinc-400">
          Delta to Reference Meter: <span className="text-rose-400">{delta} W</span> (Meter1 - Meter2)
        </div>
      </div>
    </section>
  );
};
