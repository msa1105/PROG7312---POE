import React, { useRef, useState } from 'react';
import { Upload } from 'lucide-react';
import { api } from '../services/api';

export const AttachmentUploader: React.FC<{ macAddress: string }> = ({ macAddress }) => {
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    setUploading(true);
    setError(null);
    try {
      await api.uploadAttachment(macAddress, file);
      // Optional: Trigger a refresh or handle success state
    } catch (err: any) {
      setError(err.response?.data || 'Failed to upload attachment.');
    }
    setUploading(false);
    if (fileInputRef.current) {
      fileInputRef.current.value = '';
    }
  };

  return (
    <div>
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
        className="w-full flex items-center justify-center gap-2 bg-slate-800 hover:bg-slate-700 border border-slate-600 text-slate-200 py-2 px-4 rounded text-sm transition-colors disabled:opacity-50"
      >
        <Upload size={16} />
        {uploading ? 'Uploading...' : 'Upload Log / Photo'}
      </button>
      {error && <p className="text-red-400 text-xs mt-2">{error}</p>}
    </div>
  );
};
