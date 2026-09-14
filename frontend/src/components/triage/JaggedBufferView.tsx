import { useState, useEffect } from 'react';
import { Layers, Database } from 'lucide-react';
import { api } from '../../services/api';
import type { JaggedBufferStatus } from '../../types/telemetry';

export const JaggedBufferView = ({ telemetry: _telemetry }: { telemetry?: any }) => {
  const [bufferStatus, setBufferStatus] = useState<JaggedBufferStatus | null>(null);

  const fetchBufferStatus = async () => {
    try {
      const data = await api.getJaggedBufferStatus();
      setBufferStatus(data);
    } catch (err) {
      console.error('Failed to fetch jagged buffer status:', err);
    }
  };

  useEffect(() => {
    fetchBufferStatus();
    const interval = setInterval(fetchBufferStatus, 5000);
    return () => clearInterval(interval);
  }, []);

  return (
    <section className="space-y-2">
      <div className="flex items-center justify-between">
        <h3 className="text-xs font-bold text-zinc-500 uppercase flex items-center gap-2">
          <Layers size={14} className="text-emerald-400" />
          Advanced Jagged Array Buffer (double[4][])
        </h3>
        <span className="text-[10px] text-zinc-500 font-mono">
          {bufferStatus?.totalBufferedReadings || 1000} buffered readings
        </span>
      </div>

      <div className="bg-zinc-900 border border-zinc-800 rounded-sm p-4 text-xs text-zinc-300 font-mono space-y-3">
        <div className="text-[11px] text-zinc-400">
          Raw high-throughput ingestion channels buffered sequentially before flushing into optimized collections:
        </div>

        {/* Channels List */}
        <div className="space-y-2">
          {bufferStatus?.channels && bufferStatus.channels.map((ch) => (
            <div key={ch.channelIndex} className="bg-zinc-950 p-2 rounded border border-zinc-800/80">
              <div className="flex justify-between items-center text-[10px] text-zinc-500 mb-1">
                <span className="font-bold text-zinc-400 uppercase">
                  Queue Channel {ch.channelIndex}
                </span>
                <span>
                  Allocated: <strong className="text-emerald-400">{ch.allocatedLength} items</strong> ({ch.estimatedBytes} bytes)
                </span>
              </div>
              
              <div className="text-[11px] text-zinc-300 truncate">
                [ {ch.sampleValues && ch.sampleValues.length > 0 
                  ? ch.sampleValues.map(v => v.toFixed(2)).join(', ') + (ch.allocatedLength > 6 ? ', ...' : '')
                  : 'Empty / Flushed'} ]
              </div>
            </div>
          ))}
        </div>

        {/* Custom Collection Ingestion State */}
        {bufferStatus?.customCollectionMetrics && (
          <div className="bg-zinc-950/70 p-2.5 rounded border border-zinc-800/60 mt-3 flex items-center justify-between text-[11px]">
            <div className="flex items-center gap-2 text-zinc-400">
              <Database size={13} className="text-cyan-400" />
              <span>BoundedTelemetryCollection&lt;T&gt; Ingested:</span>
            </div>
            <div className="flex items-center gap-3">
              <span className="text-cyan-400 font-bold">
                {bufferStatus.customCollectionMetrics.totalIngested} total
              </span>
              <span className="text-zinc-500">|</span>
              <span className="text-zinc-400">
                Capacity: {bufferStatus.customCollectionMetrics.capacity}
              </span>
            </div>
          </div>
        )}
      </div>
    </section>
  );
};
