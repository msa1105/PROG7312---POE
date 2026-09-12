import { useRef, useState } from 'react';
import { UploadCloud, File, Download } from 'lucide-react';
import { api } from '../../services/api';

export const AttachmentManager = ({ device }: { device: any }) => {
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    setUploading(true);
    setError(null);
    try {
      await api.uploadAttachment(device.macAddress, file);
      // Optional: trigger refresh here
    } catch (err: any) {
      setError(err.response?.data || 'Failed to upload attachment.');
    }
    setUploading(false);
    if (fileInputRef.current) {
      fileInputRef.current.value = '';
    }
  };

  return (
    <section className="space-y-2 pb-8">
      <h3 className="text-xs font-bold text-zinc-500 uppercase flex items-center gap-2">
        <UploadCloud size={14} /> Hardware Logs & Attachments
      </h3>
      <div className="bg-zinc-900 border border-zinc-800 rounded-sm p-4 text-xs text-zinc-300">
        
        {device.attachments && device.attachments.length > 0 ? (
          <ul className="space-y-2 mb-4">
            {device.attachments.map((att: any, i: number) => (
              <li key={i} className="flex justify-between items-center bg-zinc-950 p-2 border border-zinc-800 rounded-sm">
                <span className="flex items-center gap-2">
                  <File size={12} className="text-zinc-500" />
                  {att.fileName}
                </span>
                <div className="flex items-center gap-4">
                  <span className="text-zinc-600">{(att.sizeBytes / 1024).toFixed(1)} KB</span>
                  <button className="text-blue-400 hover:text-blue-300 uppercase tracking-widest text-[10px] flex items-center gap-1">
                    <Download size={12} /> View
                  </button>
                </div>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-zinc-600 mb-4 italic">No attachments found.</p>
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
          className="w-full border border-dashed border-zinc-700 hover:border-zinc-500 text-zinc-400 hover:text-zinc-200 bg-zinc-950/50 py-3 rounded-sm transition-colors flex items-center justify-center gap-2 disabled:opacity-50"
        >
          <UploadCloud size={14} />
          {uploading ? 'Uploading...' : 'Drop file here or Click to Upload (.bin, .log, .png)'}
        </button>
        {error && <p className="text-rose-400 mt-2 text-[10px]">{error}</p>}
      </div>
    </section>
  );
};
