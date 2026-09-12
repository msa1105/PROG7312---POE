import { useState } from 'react';
import { X, Cpu, MapPin } from 'lucide-react';
import { api } from '../../services/api';

type RegisterModalProps = {
  onClose: () => void;
  onSuccess: () => void;
};

export const RegisterDeviceModal = ({ onClose, onSuccess }: RegisterModalProps) => {
  const [formData, setFormData] = useState({
    macAddress: '',
    category: 0 as any, // Cast to any to avoid SensorCategory type mismatch
    deploymentLocation: ''
  });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoading(true);
    setError(null);
    try {
      await api.registerDevice({
        ...formData
      });
      onSuccess();
      onClose();
    } catch (err: any) {
      setError(err.response?.data?.title || 'Failed to register device.');
    }
    setLoading(false);
  };

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center">
      <div className="absolute inset-0 bg-zinc-950/80 backdrop-blur-sm" onClick={onClose} />
      
      <div className="relative w-full max-w-md bg-zinc-950 border border-zinc-800 shadow-2xl rounded-sm font-mono text-sm animate-in zoom-in-95 duration-200">
        
        <div className="p-4 border-b border-zinc-800 flex justify-between items-center bg-zinc-900/50">
          <div className="text-zinc-100 font-bold uppercase tracking-widest flex items-center gap-2">
            <Cpu size={16} className="text-zinc-500" /> Register Sensor Node
          </div>
          <button onClick={onClose} className="text-zinc-500 hover:text-zinc-200">
            <X size={18} />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="p-6 space-y-4">
          
          <div>
            <label className="block text-xs text-zinc-500 uppercase tracking-widest mb-2">MAC Address</label>
            <input 
              required
              placeholder="XX:XX:XX:XX:XX:XX"
              pattern="^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$"
              className="w-full bg-zinc-900 border border-zinc-800 rounded-sm p-3 text-zinc-200 focus:border-zinc-500 outline-none uppercase"
              value={formData.macAddress}
              onChange={e => setFormData({...formData, macAddress: e.target.value.toUpperCase()})}
            />
          </div>

          <div>
            <label className="block text-xs text-zinc-500 uppercase tracking-widest mb-2">Telemetry Category</label>
            <select 
              className="w-full bg-zinc-900 border border-zinc-800 rounded-sm p-3 text-zinc-200 focus:border-zinc-500 outline-none appearance-none"
              value={formData.category}
              onChange={e => setFormData({...formData, category: parseInt(e.target.value)})}
            >
              <option value={0}>0 - Environmental (float)</option>
              <option value={1}>1 - PowerConsumption (PowerMetric)</option>
              <option value={2}>2 - Actuator (bool)</option>
            </select>
          </div>

          <div>
            <label className="block text-xs text-zinc-500 uppercase tracking-widest mb-2">Deployment Path</label>
            <div className="relative">
              <MapPin size={14} className="absolute left-3 top-3.5 text-zinc-600" />
              <input 
                required
                placeholder="Facility / Zone / Node"
                className="w-full bg-zinc-900 border border-zinc-800 rounded-sm p-3 pl-9 text-zinc-200 focus:border-zinc-500 outline-none"
                value={formData.deploymentLocation}
                onChange={e => setFormData({...formData, deploymentLocation: e.target.value})}
              />
            </div>
          </div>

          {error && <p className="text-rose-400 text-xs">{error}</p>}

          <button 
            type="submit"
            disabled={loading}
            className="w-full mt-4 bg-zinc-200 hover:bg-white text-zinc-900 font-bold py-3 rounded-sm transition-colors disabled:opacity-50"
          >
            {loading ? 'Registering...' : 'Provision Node'}
          </button>
        </form>
      </div>
    </div>
  );
};
