import { X, SearchCode, Database, AlertOctagon } from 'lucide-react';
import { OperatorOverloadBox } from './OperatorOverloadBox';
import { JaggedBufferView } from './JaggedBufferView';
import { AttachmentManager } from './AttachmentManager';
import { DeviceContextPanel } from './DeviceContextPanel';
import type { DeviceView } from '../../types/telemetry';
import { formatDuration, formatValue, liveSecondsSinceLastPacket } from '../../utils/telemetry';

type DrawerProps = {
  deviceData: DeviceView;
  allDevices: DeviceView[];
  fetchedAt: number;
  onClose: () => void;
  onRefresh?: () => void;
  onReportIncident?: (mac: string) => void;
  onSelectDevice: (mac: string) => void;
};

export const DiagnosticDrawer = ({ deviceData, allDevices, fetchedAt, onClose, onRefresh, onReportIncident, onSelectDevice }: DrawerProps) => {
  const { device, latestTelemetry, status, activeAlerts } = deviceData;
  const secondsSince = liveSecondsSinceLastPacket(deviceData, fetchedAt, fetchedAt);

  return (
    <div className="fixed inset-0 z-[100] flex justify-end">
      {/* Backdrop */}
      <div
        className="absolute inset-0 bg-zinc-950/80 backdrop-blur-sm"
        onClick={onClose}
      />

      {/* Drawer Panel */}
      <div className="relative w-full max-w-xl bg-zinc-950 border-l border-zinc-800 shadow-2xl h-full flex flex-col font-mono text-sm animate-in slide-in-from-right duration-300">

        {/* Header */}
        <div className="p-6 border-b border-zinc-800 flex justify-between items-start bg-zinc-900/50">
          <div>
            <div className="text-zinc-500 text-xs mb-1 uppercase tracking-widest flex items-center gap-2">
              <SearchCode size={14} /> DIAGNOSTIC INSPECTOR
            </div>
            <h2 className="text-xl font-bold text-zinc-100 uppercase">
              {device.macAddress}
            </h2>
            <div className="text-xs text-zinc-400 mt-1">
              Level 3 Deep Packet Triage // {device.deploymentLocation}
            </div>
          </div>
          <div className="flex items-center gap-2">
            {onReportIncident && (
              <button
                onClick={() => onReportIncident(device.macAddress)}
                className="px-2.5 py-1.5 bg-rose-950 hover:bg-rose-900 border border-rose-800 text-rose-300 text-xs font-bold rounded flex items-center gap-1 transition-colors"
                title="Flag Anomaly & Report Issue"
              >
                <AlertOctagon size={13} /> Flag Issue
              </button>
            )}
            <button
              onClick={onClose}
              className="p-2 text-zinc-500 hover:text-zinc-200 hover:bg-zinc-800 rounded transition-colors"
            >
              <X size={20} />
            </button>
          </div>
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto p-6 space-y-6">

          {/* Active (unacknowledged) alerts */}
          {(activeAlerts.length > 0 || !status.isOnline) && (
            <section className="space-y-1.5">
              {!status.isOnline && (
                <div className="text-xs bg-amber-950/30 border border-amber-900/60 text-amber-400 rounded-sm p-2.5">
                  OFFLINE — no telemetry for {secondsSince !== null ? formatDuration(secondsSince) : 'ever'} (lease {status.leaseSeconds}s)
                </div>
              )}
              {activeAlerts.map(alert => (
                <div key={alert.id} className={`text-xs rounded-sm p-2.5 border ${alert.kind === 'Anomaly' ? 'bg-rose-950/30 border-rose-900/60 text-rose-300' : 'bg-amber-950/20 border-amber-900/50 text-amber-300'}`}>
                  <strong>{alert.kind === 'Anomaly' ? 'OUTLIER' : 'HEARTBEAT LOST'}</strong> @ {new Date(alert.raisedAt).toLocaleTimeString()} — {alert.message}
                  {alert.kind === 'Anomaly' && alert.triggerValue != null && (
                    <div className="text-[11px] mt-1 text-rose-400">
                      Trigger {alert.triggerValue.toFixed(2)} {latestTelemetry?.metricUnit} vs μ {alert.baselineMean?.toFixed(2)} → Δ {alert.deviation != null && alert.deviation >= 0 ? '+' : ''}{alert.deviation?.toFixed(2)}
                    </div>
                  )}
                </div>
              ))}
            </section>
          )}

          {/* Section: Raw Payload */}
          <section className="space-y-2">
            <h3 className="text-xs font-bold text-zinc-500 uppercase flex items-center gap-2">
              <Database size={14} /> Telemetry Packet (Generic Unboxed Payload)
            </h3>
            <div className="bg-zinc-900 border border-zinc-800 rounded-sm p-4 text-xs text-zinc-300 overflow-x-auto">
              <div className="grid grid-cols-3 gap-2">
                <span className="text-zinc-500">PacketId:</span>
                <span className="col-span-2 text-blue-400 break-all">{latestTelemetry?.packetId || 'N/A'}</span>

                <span className="text-zinc-500">Type:</span>
                <span className="col-span-2 text-purple-400">
                  TelemetryPacket&lt;{latestTelemetry?.payloadType ?? (device.category === 1 ? 'PowerMetric' : device.category === 0 ? 'float' : 'bool')}&gt;
                </span>

                <span className="text-zinc-500">Timestamp:</span>
                <span className="col-span-2 text-emerald-400">{latestTelemetry?.timestamp || 'N/A'} (UTC)</span>

                <span className="text-zinc-500">Value:</span>
                <span className="col-span-2 text-yellow-400">
                  {formatValue(latestTelemetry)} {latestTelemetry?.metricUnit}
                </span>

                <span className="text-zinc-500">Baseline:</span>
                <span className="col-span-2 text-zinc-300">
                  {latestTelemetry?.rollingMean != null
                    ? `μ ${latestTelemetry.rollingMean.toFixed(2)} • σ ${latestTelemetry.rollingStdDev?.toFixed(2)} • Z ${latestTelemetry.zScore?.toFixed(2) ?? '--'}`
                    : 'n/a'}
                </span>

                <span className="text-zinc-500">Transmission:</span>
                <span className="col-span-2 text-zinc-300">
                  {latestTelemetry?.signalStrengthDbm ?? '--'} dBm • {latestTelemetry?.batteryPercent ?? '--'}% battery
                </span>
              </div>
            </div>
          </section>

          {/* Preceding readings, link health, adjacent sensors */}
          <DeviceContextPanel deviceData={deviceData} allDevices={allDevices} onSelectDevice={onSelectDevice} />

          {/* Operator Overload Calculator (Live .NET API Struct Engine) */}
          {device.category === 1 && <OperatorOverloadBox telemetry={latestTelemetry} />}

          {/* Jagged Buffer Dump (Live .NET API double[4][] Buffer) */}
          <JaggedBufferView />

          {/* Attachments with AES-256 Encryption */}
          <AttachmentManager device={device} onRefresh={onRefresh} />

        </div>
      </div>
    </div>
  );
};
