import React, { useState } from 'react';
import { api } from '../services/api';
import { Network, CheckCircle, XCircle } from 'lucide-react';

export const TopologyValidatorView: React.FC = () => {
  const [jsonInput, setJsonInput] = useState(
`{
  "nodeId": "gateway-1",
  "name": "Primary Gateway",
  "isActive": true,
  "subNodes": [
    {
      "nodeId": "zone-a",
      "name": "Zone A Router",
      "isActive": true,
      "subNodes": []
    }
  ]
}`);
  const [result, setResult] = useState<{ isValid: boolean; reason: string } | null>(null);
  const [loading, setLoading] = useState(false);

  const handleValidate = async () => {
    setLoading(true);
    setResult(null);
    try {
      const parsed = JSON.parse(jsonInput);
      const res = await api.validateTopology(parsed);
      setResult(res);
    } catch (e: any) {
      if (e.response && e.response.data) {
        setResult(e.response.data);
      } else {
        setResult({ isValid: false, reason: "Invalid JSON format." });
      }
    }
    setLoading(false);
  };

  return (
    <div className="bg-slate-800/50 border border-slate-700 p-6 rounded-xl mt-6">
      <h3 className="text-lg font-semibold text-slate-100 mb-4 flex items-center gap-2">
        <Network size={20} className="text-blue-400" />
        Recursive Topology Validator
      </h3>
      
      <p className="text-sm text-slate-400 mb-4">
        Paste a JSON hierarchy of DeploymentNodes to validate structural integrity (cycles, empty IDs, active states).
      </p>

      <textarea 
        className="w-full h-48 bg-slate-900 border border-slate-700 rounded p-4 text-slate-200 font-mono text-sm outline-none focus:border-blue-500 transition-colors mb-4"
        value={jsonInput}
        onChange={e => setJsonInput(e.target.value)}
      />

      <div className="flex items-center gap-4">
        <button 
          onClick={handleValidate}
          disabled={loading}
          className="bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-medium py-2 px-6 rounded transition-colors"
        >
          {loading ? 'Validating...' : 'Validate Tree'}
        </button>

        {result && (
          <div className={`flex items-center gap-2 text-sm font-medium ${result.isValid ? 'text-emerald-400' : 'text-red-400'}`}>
            {result.isValid ? <CheckCircle size={18} /> : <XCircle size={18} />}
            {result.reason}
          </div>
        )}
      </div>
    </div>
  );
};
