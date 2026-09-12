import React, { useState } from 'react';
import { Server, MapPin, Tag } from 'lucide-react';
import { api } from '../services/api';
import { SensorCategory } from '../types/telemetry';

export const DeviceRegistration: React.FC<{ onRegistered: () => void }> = ({ onRegistered }) => {
  const [macAddress, setMacAddress] = useState('');
  const [location, setLocation] = useState('');
  const [category, setCategory] = useState<SensorCategory>(SensorCategory.Environmental);
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoading(true);
    try {
      await api.registerDevice({
        macAddress,
        deploymentLocation: location,
        category: Number(category)
      });
      setMacAddress('');
      setLocation('');
      onRegistered();
    } catch (error) {
      console.error('Registration failed', error);
    }
    setLoading(false);
  };

  return (
    <div className="bg-slate-800/50 border border-slate-700 p-6 rounded-xl">
      <h3 className="text-lg font-semibold text-slate-100 mb-4 flex items-center gap-2">
        <Server size={20} className="text-blue-400" />
        Register New Sensor Node
      </h3>
      
      <form onSubmit={handleSubmit} className="flex flex-col gap-4">
        <div>
          <label className="text-xs text-slate-400 mb-1 flex items-center gap-1">
            <Tag size={12} /> MAC Address
          </label>
          <input 
            type="text" 
            required
            placeholder="00:1A:2B:3C:4D:5E"
            className="w-full bg-slate-900 border border-slate-700 rounded p-2 text-slate-200 outline-none focus:border-blue-500 transition-colors"
            value={macAddress}
            onChange={(e) => setMacAddress(e.target.value)}
          />
        </div>

        <div>
          <label className="text-xs text-slate-400 mb-1 flex items-center gap-1">
            <MapPin size={12} /> Deployment Location
          </label>
          <input 
            type="text" 
            required
            placeholder="Facility A / GreenHouse 1"
            className="w-full bg-slate-900 border border-slate-700 rounded p-2 text-slate-200 outline-none focus:border-blue-500 transition-colors"
            value={location}
            onChange={(e) => setLocation(e.target.value)}
          />
        </div>

        <div>
          <label className="text-xs text-slate-400 mb-1 flex items-center gap-1">
            <Server size={12} /> Sensor Category
          </label>
          <select 
            className="w-full bg-slate-900 border border-slate-700 rounded p-2 text-slate-200 outline-none focus:border-blue-500 transition-colors"
            value={category}
            onChange={(e) => setCategory(Number(e.target.value))}
          >
            <option value={SensorCategory.Environmental}>Environmental</option>
            <option value={SensorCategory.PowerConsumption}>Power Consumption</option>
            <option value={SensorCategory.Actuator}>Actuator</option>
          </select>
        </div>

        <button 
          type="submit" 
          disabled={loading}
          className="mt-2 bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-medium py-2 px-4 rounded transition-colors"
        >
          {loading ? 'Registering...' : 'Register Node'}
        </button>
      </form>
    </div>
  );
};
