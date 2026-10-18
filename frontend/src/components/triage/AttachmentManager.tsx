import { useRef, useState } from 'react';
import { UploadCloud, File, Download, ShieldCheck, Lock } from 'lucide-react';
import { api, getApiErrorMessage } from '../../services/api';
import type { DeviceAttachment, SensorDevice } from '../../types/telemetry';

type AttachmentProps = {
  device: SensorDevice;
  onRefresh?: () => void | Promise<void>;
};

export const AttachmentManager = ({ device, onRefresh }: AttachmentProps) => {
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);
  // Files shown immediately while they stream to the gateway
  const [pendingFiles, setPendingFiles] = useState<{ name: string; size: number }[]>([]);

  const handleFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    setUploading(true);
    setError(null);
    setSuccessMsg(null);
    setPendingFiles(prev => [...prev.filter(f => f.name !== file.name), { name: file.name, size: file.size }]);
    try {
      await api.uploadAttachment(device.macAddress, file);
      setSuccessMsg(`File uploaded and encrypted with AES-256-CBC at rest.`);
      setTimeout(() => setSuccessMsg(null), 4000);
      if (onRefresh) await onRefresh();
    } catch (err: unknown) {
      setError(`${file.name} was rejected: ${getApiErrorMessage(err, 'Failed to upload attachment.')}`);
    }
    setPendingFiles(prev => prev.filter(f => f.name !== file.name));
    setUploading(false);
    if (fileInputRef.current) {
      fileInputRef.current.value = '';
    }
  };

  return (
    <section className="space-y-2 pb-8">
      <div className="flex items-center justify-between">
        <h3 className="text-xs font-bold text-zinc-500 uppercase flex items-center gap-2">
          <UploadCloud size={14} className="text-cyan-400" /> Hardware Logs &amp; Attachments
        </h3>
        <span className="text-[10px] text-emerald-400 bg-emerald-950/60 border border-emerald-800 px-1.5 py-0.5 rounded flex items-center gap-1">
          <Lock size={10} /> AES-256 Encrypted at Rest
        </span>
      </div>

      <div className="bg-zinc-900 border border-zinc-800 rounded-sm p-4 text-xs text-zinc-300">
        {pendingFiles.length > 0 && (
          <ul className="space-y-2 mb-2">
            {pendingFiles.map(f => (
              <li key={f.name} className="flex justify-between items-center bg-zinc-950 p-2.5 border border-dashed border-cyan-800 rounded-sm animate-pulse">
                <span className="flex items-center gap-2 text-zinc-300">
                  <File size={13} className="text-zinc-500 shrink-0" />
                  <span className="truncate max-w-[180px]">{f.name}</span>
                </span>
                <span className="text-cyan-400 text-[10px]">Encrypting… {(f.size / 1024).toFixed(1)} KB</span>
              </li>
            ))}
          </ul>
        )}
        {device.attachments && device.attachments.length > 0 ? (
          <ul className="space-y-2 mb-4">
            {device.attachments.map((att: DeviceAttachment) => {
              const downloadUrl = api.getAttachmentDownloadUrl(device.macAddress, att.fileName);
              return (
                <li key={att.fileName} className="flex justify-between items-center bg-zinc-950 p-2.5 border border-zinc-800 rounded-sm">
                  <span className="flex items-center gap-2 text-zinc-200">
                    <File size={13} className="text-zinc-400 shrink-0" />
                    <span className="truncate max-w-[180px]">{att.fileName}</span>
                    <span className="text-[9px] text-emerald-400 bg-emerald-950/80 px-1 py-0.2 rounded border border-emerald-900 flex items-center gap-0.5">
                      <ShieldCheck size={9} /> AES
                    </span>
                  </span>
                  <div className="flex items-center gap-3">
                    <span className="text-zinc-500 text-[10px]">
                      {(att.sizeBytes / 1024).toFixed(1)} KB
                    </span>
                    <a 
                      href={downloadUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="text-cyan-400 hover:text-cyan-300 bg-cyan-950/40 hover:bg-cyan-950 border border-cyan-800 px-2 py-1 rounded text-[10px] uppercase font-bold flex items-center gap-1 transition-colors"
                      download={att.fileName}
                    >
                      <Download size={11} /> Decrypt &amp; Download
                    </a>
                  </div>
                </li>
              );
            })}
          </ul>
        ) : (
          <p className="text-zinc-600 mb-4 italic">No physical attachments or hardware logs uploaded yet.</p>
        )}

        <input 
          type="file" 
          ref={fileInputRef} 
          onChange={handleFileChange} 
          className="hidden"
          accept=".bin,.json,.log,.txt,.jpg,.png"
        />
        
        <button 
          onClick={() => fileInputRef.current?.click()}
          disabled={uploading}
          className="w-full border border-dashed border-zinc-700 hover:border-zinc-500 text-zinc-400 hover:text-zinc-200 bg-zinc-950/50 py-3 rounded-sm transition-colors flex items-center justify-center gap-2 disabled:opacity-50 font-mono text-xs cursor-pointer"
        >
          <UploadCloud size={14} />
          {uploading ? 'Streaming & Encrypting File...' : 'Select File to Encrypt & Attach (.bin, .log, .json, .png)'}
        </button>

        {successMsg && <p className="text-emerald-400 mt-2 text-[11px] font-mono">{successMsg}</p>}
        {error && <p className="text-rose-400 mt-2 text-[11px] font-mono">{error}</p>}
      </div>
    </section>
  );
};
