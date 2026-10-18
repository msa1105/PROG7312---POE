import { useEffect, useState } from 'react';
import { Network, ChevronRight, ChevronDown, ShieldCheck, ShieldAlert } from 'lucide-react';
import { clsx } from 'clsx';
import { api } from '../../services/api';
import type { LiveTopology, LiveTopologyNode } from '../../types/telemetry';

type TreeNodeProps = {
  node: LiveTopologyNode;
  depth?: number;
  selectedZone: string | null;
  onSelectZone: (zoneId: string | null) => void;
  onSelectDevice: (macAddress: string) => void;
};

const TreeNode = ({ node, depth = 0, selectedZone, onSelectZone, onSelectDevice }: TreeNodeProps) => {
  const [expanded, setExpanded] = useState(true);
  const isDevice = node.kind === 'device';
  const hasChildren = node.subNodes.length > 0;
  const isSelected = !isDevice && selectedZone === node.nodeId;

  // Degraded state is bubbled up recursively by the backend topology engine
  const statusBubbled = node.isDegraded;

  const handleClick = () => {
    if (isDevice && node.macAddress) {
      onSelectDevice(node.macAddress);
    } else if (node.kind !== 'gateway') {
      // Selecting a zone filters the ingestion matrix down to the affected sub-tree
      onSelectZone(isSelected ? null : node.nodeId);
    }
  };

  return (
    <div className="font-mono text-xs">
      <div
        className={clsx(
          "flex items-center py-1.5 px-2 hover:bg-zinc-800/50 rounded-sm cursor-pointer transition-colors",
          depth === 0 ? "text-zinc-200 font-bold" : "text-zinc-400",
          isSelected && "bg-zinc-800/80 ring-1 ring-cyan-700"
        )}
        style={{ paddingLeft: `${depth * 1.5 + 0.5}rem` }}
        onClick={handleClick}
        title={isDevice ? 'Open diagnostic drawer' : 'Filter ingestion matrix to this zone'}
      >
        <button
          className="w-4 h-4 flex items-center justify-center mr-1 cursor-pointer"
          onClick={e => { e.stopPropagation(); if (hasChildren) setExpanded(!expanded); }}
        >
          {hasChildren ? (
            expanded ? <ChevronDown size={14} className="text-zinc-500" /> : <ChevronRight size={14} className="text-zinc-500" />
          ) : (
            <div className="w-1 h-1 bg-zinc-700 rounded-full" />
          )}
        </button>

        <span className="flex-1 flex items-center gap-2 min-w-0">
          <span className="truncate">{isDevice ? node.macAddress : node.name}</span>
          <span className="text-zinc-600 font-normal truncate">
            ({isDevice ? node.name : `${node.onlineDevices}/${node.totalDevices} online`})
          </span>
        </span>

        {/* Per-zone SRE reliability indices */}
        {!isDevice && (
          <span className="hidden sm:flex items-center gap-2 text-[10px] font-normal text-zinc-500 mr-2 tabular-nums">
            <span title="Availability">{node.availabilityPercent != null ? `${node.availabilityPercent.toFixed(0)}%` : '--'}</span>
            <span title="Packet delivery success" className={clsx(node.packetDeliveryPercent != null && node.packetDeliveryPercent < 95 && 'text-amber-400')}>
              PDR {node.packetDeliveryPercent != null ? `${node.packetDeliveryPercent.toFixed(0)}%` : '--'}
            </span>
          </span>
        )}

        {/* Bubbling Status Indicator */}
        <div className="flex items-center gap-2">
          {statusBubbled && !isDevice && (
            <span className="flex h-2 w-2 relative">
              <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-amber-400 opacity-75"></span>
              <span className="relative inline-flex rounded-full h-2 w-2 bg-amber-500"></span>
            </span>
          )}
          {isDevice && !node.isActive && (
             <span className="h-2 w-2 rounded-full bg-rose-500" title="Offline"></span>
          )}
          {isDevice && node.isActive && node.activeAlerts > 0 && (
             <span className="h-2 w-2 rounded-full bg-rose-400 animate-pulse" title="Unacknowledged alert"></span>
          )}
          {!statusBubbled && (
             <span className="h-2 w-2 rounded-full bg-emerald-500/50"></span>
          )}
        </div>
      </div>

      {expanded && hasChildren && (
        <div className="border-l border-zinc-800 ml-[0.9rem] mt-1 mb-2">
          {node.subNodes.map((child) => (
            <TreeNode key={child.nodeId} node={child} depth={depth + 1} selectedZone={selectedZone} onSelectZone={onSelectZone} onSelectDevice={onSelectDevice} />
          ))}
        </div>
      )}
    </div>
  );
};

type TopologyTreeProps = {
  selectedZone: string | null;
  onSelectZone: (zoneId: string | null) => void;
  onSelectDevice: (macAddress: string) => void;
};

export const TopologyTree = ({ selectedZone, onSelectZone, onSelectDevice }: TopologyTreeProps) => {
  const [topology, setTopology] = useState<LiveTopology | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const load = async () => {
      try {
        setTopology(await api.getLiveTopology());
        setError(null);
      } catch {
        setError('Unable to load live topology from the gateway.');
      }
    };
    load();
    const interval = setInterval(load, 2000);
    return () => clearInterval(interval);
  }, []);

  const validation = topology?.validation;

  return (
    <div className="bg-zinc-900 border border-zinc-800 rounded-sm overflow-hidden h-full flex flex-col">
      <div className="p-3 border-b border-zinc-800 bg-zinc-950 flex items-center justify-between text-zinc-500 text-xs tracking-widest uppercase">
        <div className="flex items-center gap-2">
          <Network size={14} /> Mesh Topology Tree
        </div>
        <span className="text-[10px] normal-case tracking-normal text-zinc-600">Avail • PDR</span>
      </div>
      <div className="p-4 flex-1 overflow-y-auto">
        {topology ? (
          <TreeNode node={topology.root} selectedZone={selectedZone} onSelectZone={onSelectZone} onSelectDevice={onSelectDevice} />
        ) : (
          <p className="text-xs font-mono text-zinc-600 italic">{error ?? 'Loading deployment hierarchy…'}</p>
        )}
      </div>
      {validation && (
        <div className={clsx(
          "px-3 py-2 border-t text-[10px] font-mono flex items-start gap-1.5",
          validation.isValid ? "border-zinc-800 text-emerald-400" : "border-amber-900/60 bg-amber-950/20 text-amber-400"
        )}>
          {validation.isValid ? <ShieldCheck size={12} className="shrink-0 mt-px" /> : <ShieldAlert size={12} className="shrink-0 mt-px" />}
          <span>Recursive audit: {validation.reason}</span>
        </div>
      )}
    </div>
  );
};
