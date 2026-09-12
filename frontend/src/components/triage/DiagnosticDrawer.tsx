import { X, SearchCode, Database, Download } from 'lucide-react';
import { OperatorOverloadBox } from './OperatorOverloadBox';
import { JaggedBufferView } from './JaggedBufferView';
import { AttachmentManager } from './AttachmentManager';

type DrawerProps = {
  deviceData: any;
  onClose: () => void;
};

export const DiagnosticDrawer = ({ deviceData, onClose }: DrawerProps) => {
  const { device, latestTelemetry } = deviceData;

  return (
    <div className="fixed inset-0 z-[100] flex justify-end">
      {/* Backdrop */}
      <div 
        className="absolute inset-0 bg-zinc-950/80 backdrop-blur-sm" 
        onClick={onClose}
      />
      
      {/* Drawer Panel */}
      <div className="relative w-full max-w-xl bg-zinc-950 border-l border-zinc-800 shadow-2xl h-full flex flex-col font-mono text-sm animate-in slide-in-from-right duration-300">
        
        {/* Header */}
        <div className="p-6 border-b border-zinc-800 flex justify-between items-start bg-zinc-900/50">
          <div>
            <div className="text-zinc-500 text-xs mb-1 uppercase tracking-widest flex items-center gap-2">
              <SearchCode size={14} /> DIAGNOSTIC INSPECTOR
            </div>
            <h2 className="text-xl font-bold text-zinc-100 uppercase">
              {device.macAddress}
            </h2>
            <div className="text-xs text-zinc-400 mt-1">
              Level 3 Deep Packet Triage // {device.deploymentLocation}
            </div>
          </div>
          <button 
            onClick={onClose} 
            className="p-2 text-zinc-500 hover:text-zinc-200 hover:bg-zinc-800 rounded transition-colors"
          >
            <X size={20} />
          </button>
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto p-6 space-y-6">
          
          {/* Section: Raw Payload */}
          <section className="space-y-2">
            <h3 className="text-xs font-bold text-zinc-500 uppercase flex items-center gap-2">
              <Database size={14} /> Telemetry Packet (Generic Unboxed Payload)
            </h3>
            <div className="bg-zinc-900 border border-zinc-800 rounded-sm p-4 text-xs text-zinc-300 overflow-x-auto">
              <div className="grid grid-cols-3 gap-2">
                <span className="text-zinc-500">PacketId:</span>
                <span className="col-span-2 text-blue-400">{latestTelemetry?.packetId || 'N/A'}</span>
                
                <span className="text-zinc-500">Type:</span>
                <span className="col-span-2 text-purple-400">
                  TelemetryPacket&lt;{device.category === 1 ? 'PowerMetric' : device.category === 0 ? 'float' : 'bool'}&gt;
                </span>
                
                <span className="text-zinc-500">Timestamp:</span>
                <span className="col-span-2 text-emerald-400">{latestTelemetry?.timestamp || 'N/A'} (UTC)</span>
                
                <span className="text-zinc-500">Value:</span>
                <span className="col-span-2 text-yellow-400">{latestTelemetry?.value?.toFixed(2) || 'N/A'} {latestTelemetry?.metricUnit}</span>
              </div>
            </div>
          </section>

          {/* Operator Overload Calculator (Mocked for Demo based on PRD) */}
          <OperatorOverloadBox device={device} telemetry={latestTelemetry} />

          {/* Jagged Buffer Dump (Mocked based on PRD) */}
          <JaggedBufferView telemetry={latestTelemetry} />

          {/* Attachments */}
          <AttachmentManager device={device} />

        </div>
      </div>
    </div>
  );
};
