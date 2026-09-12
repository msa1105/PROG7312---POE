import { useState } from 'react';
import { Network, ChevronRight, ChevronDown, Circle } from 'lucide-react';
import { clsx } from 'clsx';

type Node = {
  nodeId: string;
  name: string;
  isActive: boolean;
  subNodes?: Node[];
};

const TreeNode = ({ node, depth = 0 }: { node: Node; depth?: number }) => {
  const [expanded, setExpanded] = useState(true);
  const hasChildren = node.subNodes && node.subNodes.length > 0;
  
  // Recursively check if any child is inactive
  const hasInactiveChild = (n: Node): boolean => {
    if (!n.isActive) return true;
    if (n.subNodes) {
      return n.subNodes.some(hasInactiveChild);
    }
    return false;
  };
  
  const statusBubbled = hasInactiveChild(node);

  return (
    <div className="font-mono text-xs">
      <div 
        className={clsx(
          "flex items-center py-1.5 px-2 hover:bg-zinc-800/50 rounded-sm cursor-pointer transition-colors",
          depth === 0 ? "text-zinc-200 font-bold" : "text-zinc-400"
        )}
        style={{ paddingLeft: `${depth * 1.5 + 0.5}rem` }}
        onClick={() => hasChildren && setExpanded(!expanded)}
      >
        <div className="w-4 h-4 flex items-center justify-center mr-1">
          {hasChildren ? (
            expanded ? <ChevronDown size={14} className="text-zinc-500" /> : <ChevronRight size={14} className="text-zinc-500" />
          ) : (
            <div className="w-1 h-1 bg-zinc-700 rounded-full" />
          )}
        </div>
        
        <span className="flex-1 flex items-center gap-2">
          {node.nodeId} <span className="text-zinc-600 font-normal">({node.name})</span>
        </span>
        
        {/* Bubbling Status Indicator */}
        <div className="flex items-center gap-2">
          {statusBubbled && hasChildren && (
            <span className="flex h-2 w-2 relative">
              <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-amber-400 opacity-75"></span>
              <span className="relative inline-flex rounded-full h-2 w-2 bg-amber-500"></span>
            </span>
          )}
          {!node.isActive && (
             <span className="h-2 w-2 rounded-full bg-rose-500"></span>
          )}
          {node.isActive && !statusBubbled && (
             <span className="h-2 w-2 rounded-full bg-emerald-500/50"></span>
          )}
        </div>
      </div>
      
      {expanded && hasChildren && (
        <div className="border-l border-zinc-800 ml-[0.9rem] mt-1 mb-2">
          {node.subNodes!.map((child) => (
            <TreeNode key={child.nodeId} node={child} depth={depth + 1} />
          ))}
        </div>
      )}
    </div>
  );
};

export const TopologyTree = () => {
  // Mock Data for visualization
  const mockTree: Node = {
    nodeId: "Facility Alpha",
    name: "Root Hub",
    isActive: true,
    subNodes: [
      {
        nodeId: "Zone 1 (Greenhouse)",
        name: "Aggregator",
        isActive: true,
        subNodes: [
          { nodeId: "ESP32-PWR-01", name: "Meter", isActive: true },
          { nodeId: "ESP32-ENV-02", name: "Sensor", isActive: false } // Inactive triggers bubble
        ]
      },
      {
        nodeId: "Zone 2 (Hydroponics)",
        name: "Aggregator",
        isActive: true,
        subNodes: [
          { nodeId: "ESP32-ACT-05", name: "Pump", isActive: true }
        ]
      }
    ]
  };

  return (
    <div className="bg-zinc-900 border border-zinc-800 rounded-sm overflow-hidden h-full flex flex-col">
      <div className="p-3 border-b border-zinc-800 bg-zinc-950 flex items-center justify-between text-zinc-500 text-xs tracking-widest uppercase">
        <div className="flex items-center gap-2">
          <Network size={14} /> Mesh Topology Tree
        </div>
      </div>
      <div className="p-4 flex-1 overflow-y-auto">
        <TreeNode node={mockTree} />
      </div>
    </div>
  );
};
