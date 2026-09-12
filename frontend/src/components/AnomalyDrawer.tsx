import React from 'react';
import { X, HardDrive, FileText, UploadCloud } from 'lucide-react';
import { AttachmentUploader } from './AttachmentUploader';

export const AnomalyDrawer: React.FC<{ deviceData: any, onClose: () => void }> = ({ deviceData, onClose }) => {
  const { device, latestTelemetry } = deviceData;

  return (
    <div className="fixed inset-0 z-[100] flex justify-end">
      {/* Backdrop */}
      <div 
        className="absolute inset-0 bg-slate-900/60 backdrop-blur-sm" 
        onClick={onClose}
      />
      
      {/* Drawer */}
      <div className="relative w-full max-w-md bg-slate-800 border-l border-slate-700 h-full shadow-2xl flex flex-col animate-in slide-in-from-right duration-300">
        
        <div className="p-6 border-b border-slate-700 flex justify-between items-start">
          <div>
            <h2 className="text-xl font-bold text-slate-100 flex items-center gap-2">
              <HardDrive className="text-blue-400" />
              Node Diagnostics
            </h2>
            <div className="text-sm text-slate-400 mt-1 breadcrumbs">
              {device.deploymentLocation} &gt; {device.macAddress}
            </div>
          </div>
          <button onClick={onClose} className="p-2 text-slate-400 hover:text-white rounded-full hover:bg-slate-700 transition-colors">
            <X size={20} />
          </button>
        </div>

        <div className="flex-1 overflow-y-auto p-6 space-y-6">
          
          <div className="bg-slate-900 rounded-lg p-4 border border-slate-700">
            <h3 className="text-sm font-semibold text-slate-300 mb-3 flex items-center gap-2">
              <FileText size={16} className="text-slate-500"/> Raw TelemetryPacket&lt;T&gt;
            </h3>
            <pre className="text-xs text-green-400 overflow-x-auto">
              {JSON.stringify(latestTelemetry || { status: 'No payload received' }, null, 2)}
            </pre>
          </div>

          <div className="bg-slate-900 rounded-lg p-4 border border-slate-700">
            <h3 className="text-sm font-semibold text-slate-300 mb-3 flex items-center gap-2">
              <UploadCloud size={16} className="text-slate-500"/> Attachments
            </h3>
            {device.attachments && device.attachments.length > 0 ? (
              <ul className="space-y-2 mb-4">
                {device.attachments.map((att: any, i: number) => (
                  <li key={i} className="flex justify-between items-center text-sm p-2 bg-slate-800 rounded border border-slate-700">
                    <span className="text-slate-200 truncate pr-4">{att.fileName}</span>
                    <span className="text-xs text-slate-500">{(att.sizeBytes / 1024).toFixed(1)} KB</span>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="text-xs text-slate-500 mb-4 italic">No attachments found.</p>
            )}

            <AttachmentUploader macAddress={device.macAddress} />
          </div>

        </div>
      </div>
    </div>
  );
};
