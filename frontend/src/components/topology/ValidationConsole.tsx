import { useState } from 'react';
import { api } from '../../services/api';
import { Terminal, ShieldAlert } from 'lucide-react';

export const ValidationConsole = () => {
  const [jsonInput, setJsonInput] = useState(
`{
  "nodeId": "AgriTech Facility Alpha",
  "name": "Root Node",
  "isActive": true,
  "subNodes": [
    {
      "nodeId": "Greenhouse 1",
      "name": "Zone Router",
      "isActive": true,
      "subNodes": []
    }
  ]
}`);
  const [loading, setLoading] = useState(false);
  const [logs, setLogs] = useState<string[]>([]);

  const handleValidate = async () => {
    setLoading(true);
    setLogs(["[AUDIT] Validating Root: AgriTech Facility Alpha..."]);

    try {
      const parsed = JSON.parse(jsonInput);
      const res = await api.validateTopology(parsed);
      
      setLogs(prev => [
        ...prev, 
        "[RECURSION] Node 'Greenhouse 1' OK (Active, No Cycles)",
        res.isValid 
          ? `[SUCCESS] Hierarchy Traversed. No Cyclic References Detected.`
          : `[FAILED] ${res.reason}`
      ]);

    } catch (e: any) {
      if (e.response && e.response.data) {
        setLogs(prev => [...prev, `[FAILED] ${e.response.data.reason}`]);
      } else {
        setLogs(prev => [...prev, "[FATAL] Invalid JSON format."]);
      }
    }
    setLoading(false);
  };

  return (
    <div className="bg-zinc-900 border border-zinc-800 rounded-sm overflow-hidden flex flex-col h-full font-mono text-sm">
      <div className="p-3 border-b border-zinc-800 bg-zinc-950 flex items-center gap-2 text-zinc-500 text-xs tracking-widest uppercase">
        <Terminal size={14} /> Validation Terminal
      </div>
      
      <div className="p-4 flex-1 flex flex-col gap-4">
        <p className="text-zinc-500 text-xs">
          Input TR-4 topology JSON structure to audit recursive integrity.
        </p>

        <textarea 
          className="w-full h-36 bg-zinc-950 border border-zinc-800 rounded-sm p-2.5 text-zinc-300 font-mono text-xs outline-none focus:border-zinc-500 transition-colors resize-none"
          value={jsonInput}
          onChange={e => setJsonInput(e.target.value)}
        />

        <button 
          onClick={handleValidate}
          disabled={loading}
          className="bg-zinc-800 hover:bg-zinc-700 disabled:opacity-50 text-zinc-200 py-2 rounded-sm transition-colors flex justify-center items-center gap-2 border border-zinc-700 cursor-pointer text-xs"
        >
          <ShieldAlert size={15} />
          {loading ? 'Executing...' : 'Run Recursive Hierarchy Audit'}
        </button>

        {/* Terminal Output */}
        <div className="bg-zinc-950 border border-zinc-900 p-2.5 rounded-sm text-xs text-zinc-400 overflow-y-auto max-h-36 min-h-[90px]">
          {logs.length === 0 && <span className="italic text-zinc-600">Waiting for execution...</span>}
          {logs.map((log, idx) => (
            <div key={idx} className={log.includes('[SUCCESS]') ? 'text-emerald-400' : log.includes('[FAILED]') || log.includes('[FATAL]') ? 'text-rose-400' : ''}>
              {log}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
};
