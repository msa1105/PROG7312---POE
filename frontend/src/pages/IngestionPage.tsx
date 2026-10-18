import { Network, Terminal, Activity, Filter, X } from 'lucide-react';
import { MetricStrip } from '../components/dashboard/MetricStrip';
import { SensorGrid } from '../components/dashboard/SensorGrid';
import { TopologyTree } from '../components/topology/TopologyTree';
import { ValidationConsole } from '../components/topology/ValidationConsole';
import { useGatewayStore } from '../state/gatewayContext';
import { isWithinZone } from '../utils/telemetry';

/** Pillar 1: Sensor Data Ingestion & Telemetry (Part 1), backed by the shared live store. */
const IngestionPage = () => {
  const store = useGatewayStore();
  const { zoneFilter, setZoneFilter, topologyTab, setTopologyTab } = store;
  const visibleDevices = zoneFilter ? store.devices.filter(d => isWithinZone(d.device.deploymentLocation, zoneFilter)) : store.devices;

  return (
    <div className="mt-4 space-y-6">
      {/* Full Width Metric Strip across top */}
      <MetricStrip />

      {/* Main Content Grid */}
      <div className="grid grid-cols-1 xl:grid-cols-12 gap-6 items-start">
        {/* Left: Sensor Ingestion Matrix (8 cols on XL) */}
        <div className="xl:col-span-8">
          <div className="flex items-center justify-between mb-3 border-b border-zinc-800/80 pb-2">
            <div className="text-xs font-mono font-bold uppercase tracking-wider text-zinc-300 flex items-center gap-2">
              <Activity size={14} className="text-emerald-400" />
              Live Ingestion Matrix ({visibleDevices.length} Edge Nodes)
            </div>
            <div className="text-[11px] font-mono text-zinc-500 hidden sm:block">
              Click any node card for Deep Packet Triage &amp; C# Struct Operations
            </div>
          </div>

          {zoneFilter && (
            <div className="mb-3 flex items-center justify-between gap-2 px-3 py-1.5 bg-zinc-900 border border-zinc-700 rounded-sm text-[11px] font-mono text-zinc-300">
              <span className="flex items-center gap-2 truncate">
                <Filter size={12} className="text-cyan-400 shrink-0" />
                Zone filter: <strong className="text-zinc-100 truncate">{zoneFilter}</strong>
              </span>
              <button onClick={() => setZoneFilter(null)} className="flex items-center gap-1 text-zinc-400 hover:text-zinc-100 cursor-pointer shrink-0">
                <X size={12} /> Clear
              </button>
            </div>
          )}

          <SensorGrid
            devices={visibleDevices}
            history={store.history}
            receivedAt={store.receivedAt}
            onOpenDrawer={store.setSelectedMac}
            onRefresh={store.refreshDevices}
            onReportIncident={store.requestIncidentReport}
          />
        </div>

        {/* Right: Topology & Audit Console with Tabs (4 cols on XL) */}
        <div className="xl:col-span-4 flex flex-col gap-3">
          <div className="flex items-center justify-between bg-zinc-950 p-1.5 rounded border border-zinc-800 text-xs font-mono">
            <div className="flex gap-1.5">
              <button
                onClick={() => setTopologyTab('tree')}
                className={`flex items-center gap-1.5 px-3 py-1.5 rounded transition-colors cursor-pointer ${
                  topologyTab === 'tree'
                    ? 'bg-zinc-800 text-zinc-100 font-bold border border-zinc-700'
                    : 'text-zinc-500 hover:text-zinc-300'
                }`}
              >
                <Network size={13} />
                <span>Mesh Tree</span>
              </button>
              <button
                onClick={() => setTopologyTab('console')}
                className={`flex items-center gap-1.5 px-3 py-1.5 rounded transition-colors cursor-pointer ${
                  topologyTab === 'console'
                    ? 'bg-zinc-800 text-zinc-100 font-bold border border-zinc-700'
                    : 'text-zinc-500 hover:text-zinc-300'
                }`}
              >
                <Terminal size={13} />
                <span>Audit Console</span>
              </button>
            </div>
            <span className="text-[10px] font-mono text-zinc-500 uppercase pr-1">
              {topologyTab === 'tree' ? 'Recursive Hierarchy' : 'JSON Validator'}
            </span>
          </div>

          <div className="w-full min-h-[440px]">
            {topologyTab === 'tree' ? (
              <TopologyTree selectedZone={zoneFilter} onSelectZone={setZoneFilter} onSelectDevice={store.setSelectedMac} />
            ) : (
              <ValidationConsole />
            )}
          </div>
        </div>
      </div>
    </div>
  );
};

export default IngestionPage;
