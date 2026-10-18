import { useState } from 'react';
import { X, Cpu, MapPin, CheckCircle2, AlertCircle, Sparkles } from 'lucide-react';
import { useGatewayStore } from '../../state/gatewayContext';
import type { SensorCategory } from '../../types/telemetry';

type RegisterModalProps = {
  existingMacs?: string[];
  onClose: () => void;
  onSuccess: () => void;
};

const MAC_REGEX = /^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$/;

export const RegisterDeviceModal = ({ existingMacs = [], onClose, onSuccess }: RegisterModalProps) => {
  const [macAddress, setMacAddress] = useState('');
  const [category, setCategory] = useState<number>(0);
  const [facility, setFacility] = useState('Facility Alpha');
  const [zone, setZone] = useState('Greenhouse 2');
  const [nodeId, setNodeId] = useState('Node-101');
  const [error, setError] = useState<string | null>(null);
  const { registerDevice } = useGatewayStore();

  const combinedLocation = `${facility.trim()} > ${zone.trim()} > ${nodeId.trim()}`;
  const isMacFormatValid = MAC_REGEX.test(macAddress.trim());
  const isDuplicate = existingMacs.some(m => m.toUpperCase() === macAddress.trim().toUpperCase());

  const handleMacChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    let val = e.target.value.toUpperCase().replace(/[^0-9A-F:]/g, '');
    // Auto-insert colons if user types raw hex (e.g. 246F289A...)
    if (!val.includes(':') && val.length > 2) {
      val = val.match(/.{1,2}/g)?.join(':') || val;
    }
    if (val.length <= 17) {
      setMacAddress(val);
    }
  };

  const handleGenerateSampleMac = () => {
    const hex = () => Math.floor(Math.random() * 256).toString(16).padStart(2, '0').toUpperCase();
    setMacAddress(`24:6F:28:${hex()}:${hex()}:${hex()}`);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!isMacFormatValid) {
      setError('Please provide a valid 6-octet MAC Address (XX:XX:XX:XX:XX:XX).');
      return;
    }

    // Optimistic: the node appears immediately; the store removes it and reports the reason if the API rejects it
    void registerDevice({
      macAddress: macAddress.trim().toUpperCase(),
      category: category as SensorCategory,
      deploymentLocation: combinedLocation
    }).then(ok => ok && onSuccess());
    onClose();
  };

  return (
    <div className="fixed inset-0 z-[110] flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-zinc-950/80 backdrop-blur-sm" onClick={onClose} />
      
      <div className="relative w-full max-w-lg bg-zinc-950 border border-zinc-800 shadow-2xl rounded-sm font-mono text-xs animate-in zoom-in-95 duration-200">
        
        {/* Modal Header */}
        <div className="p-4 border-b border-zinc-800 flex justify-between items-center bg-zinc-900/50">
          <div className="text-zinc-100 font-bold uppercase tracking-widest flex items-center gap-2">
            <Cpu size={16} className="text-cyan-400" /> Provision Smart-X Node
          </div>
          <button onClick={onClose} className="text-zinc-500 hover:text-zinc-200">
            <X size={18} />
          </button>
        </div>

        {/* Modal Body */}
        <form onSubmit={handleSubmit} className="p-6 space-y-4 text-zinc-300">
          
          {/* MAC Address with Real-time Validation */}
          <div>
            <div className="flex justify-between items-center mb-1">
              <label className="text-[11px] text-zinc-400 uppercase tracking-widest font-bold">
                Device MAC Address / Hardware Identifier
              </label>
              <button
                type="button"
                onClick={handleGenerateSampleMac}
                className="text-[10px] text-cyan-400 hover:text-cyan-300 flex items-center gap-1 underline"
              >
                <Sparkles size={11} /> Auto-Generate
              </button>
            </div>
            
            <div className="relative">
              <input 
                required
                placeholder="24:6F:28:XX:XX:XX"
                className={`w-full bg-zinc-900 border rounded-sm p-2.5 text-zinc-100 font-mono text-sm uppercase outline-none transition-colors ${
                  macAddress.length === 0 
                    ? 'border-zinc-800 focus:border-zinc-500' 
                    : isMacFormatValid 
                      ? isDuplicate 
                        ? 'border-amber-500 text-amber-300' 
                        : 'border-emerald-500 text-emerald-300' 
                      : 'border-rose-500 text-rose-300'
                }`}
                value={macAddress}
                onChange={handleMacChange}
              />
              <div className="absolute right-3 top-3">
                {isMacFormatValid && !isDuplicate && <CheckCircle2 size={16} className="text-emerald-400" />}
                {isDuplicate && <AlertCircle size={16} className="text-amber-400" />}
                {!isMacFormatValid && macAddress.length > 0 && <AlertCircle size={16} className="text-rose-400" />}
              </div>
            </div>

            <div className="mt-1 text-[10px] flex justify-between">
              {macAddress.length > 0 && !isMacFormatValid && (
                <span className="text-rose-400">Invalid format: must match XX:XX:XX:XX:XX:XX hex notation.</span>
              )}
              {isDuplicate && (
                <span className="text-amber-400">Node already registered (will update deployment settings).</span>
              )}
              {isMacFormatValid && !isDuplicate && (
                <span className="text-emerald-400">Format verified (EUI-48 compliant).</span>
              )}
            </div>
          </div>

          {/* Category Selection */}
          <div>
            <label className="block text-[11px] text-zinc-400 uppercase tracking-widest font-bold mb-1">
              Sensor Category &amp; Generic Type Spec
            </label>
            <select 
              className="w-full bg-zinc-900 border border-zinc-800 rounded-sm p-2.5 text-zinc-200 focus:border-zinc-500 outline-none"
              value={category}
              onChange={e => setCategory(parseInt(e.target.value))}
            >
              <option value={0}>0 - Environmental (TelemetryPacket&lt;float&gt; • Temperature/Moisture)</option>
              <option value={1}>1 - PowerConsumption (TelemetryPacket&lt;PowerMetric&gt; • Operator Overloads)</option>
              <option value={2}>2 - Actuator (TelemetryPacket&lt;bool&gt; • Digital State)</option>
            </select>
          </div>

          {/* Structured Deployment Location Hierarchy */}
          <div>
            <label className="block text-[11px] text-zinc-400 uppercase tracking-widest font-bold mb-1">
              Deployment Location Hierarchy (Facility &gt; Zone &gt; Node)
            </label>
            <div className="grid grid-cols-3 gap-2 mb-2">
              <input 
                required
                placeholder="Facility"
                className="bg-zinc-900 border border-zinc-800 rounded p-2 text-zinc-200 outline-none"
                value={facility}
                onChange={e => setFacility(e.target.value)}
              />
              <input 
                required
                placeholder="Zone / Sub-Zone"
                className="bg-zinc-900 border border-zinc-800 rounded p-2 text-zinc-200 outline-none"
                value={zone}
                onChange={e => setZone(e.target.value)}
              />
              <input 
                required
                placeholder="Node ID"
                className="bg-zinc-900 border border-zinc-800 rounded p-2 text-zinc-200 outline-none"
                value={nodeId}
                onChange={e => setNodeId(e.target.value)}
              />
            </div>
            <div className="flex items-center gap-1.5 text-[10px] text-zinc-500 bg-zinc-950 p-2 rounded border border-zinc-800/80">
              <MapPin size={12} className="text-zinc-400" />
              <span>Resolved Topology Path: <strong className="text-zinc-300">{combinedLocation}</strong></span>
            </div>
          </div>

          {error && (
            <div className="p-2.5 bg-rose-950/80 border border-rose-800 text-rose-300 rounded text-[11px]">
              {error}
            </div>
          )}

          <div className="pt-2 flex gap-3">
            <button
              type="button"
              onClick={onClose}
              className="flex-1 bg-zinc-900 hover:bg-zinc-800 text-zinc-400 py-3 rounded-sm transition-colors uppercase tracking-wider font-bold"
            >
              Cancel
            </button>
            <button 
              type="submit"
              disabled={!isMacFormatValid}
              className="flex-1 bg-emerald-500 hover:bg-emerald-400 text-zinc-950 font-bold py-3 rounded-sm transition-all uppercase tracking-wider disabled:opacity-50 cursor-pointer"
            >
              Provision Node
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
