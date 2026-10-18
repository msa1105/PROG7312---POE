import { Link } from 'react-router-dom';
import { Lock, RadioTower } from 'lucide-react';

/** Pillar 3 is scheduled for the final PoE; a direct visit explains that instead of showing a blank page. */
const TopologyLockedPage = () => (
  <div className="mt-6 max-w-xl mx-auto bg-zinc-900 border border-zinc-800 rounded-sm p-6 font-mono text-sm text-zinc-300">
    <div className="flex items-center gap-2 text-amber-400 text-xs uppercase tracking-widest mb-3">
      <Lock size={14} /> Final PoE Milestone (Locked)
    </div>
    <h2 className="text-lg font-bold text-zinc-100 uppercase mb-2 flex items-center gap-2">
      <RadioTower size={18} className="text-zinc-500" /> Network Topology &amp; Mesh Routing
    </h2>
    <p className="text-xs text-zinc-400 leading-relaxed mb-5">
      Multi-hop mesh routing, AODV path discovery and signal attenuation heatmaps are delivered in the final PoE.
      The recursive deployment hierarchy is already available in the ingestion module's Mesh Tree.
    </p>
    <div className="flex gap-2 text-xs">
      <Link to="/ingestion" className="px-3 py-2 bg-zinc-800 hover:bg-zinc-700 border border-zinc-700 rounded-sm">Open Ingestion</Link>
      <Link to="/commands" className="px-3 py-2 bg-zinc-800 hover:bg-zinc-700 border border-zinc-700 rounded-sm">Open Command Stream</Link>
    </div>
  </div>
);

export default TopologyLockedPage;
