export const SensorCategory = {
  Environmental: 0,
  PowerConsumption: 1,
  Actuator: 2
} as const;

export type SensorCategory = (typeof SensorCategory)[keyof typeof SensorCategory];

export interface DeviceAttachment {
  fileName: string;
  sizeBytes: number;
  mimeType: string;
  uploadedAt: string;
}

export interface SensorDevice {
  macAddress: string;
  deploymentLocation: string;
  category: SensorCategory;
  attachments: DeviceAttachment[];
}

export interface TelemetryPacket<T> {
  packetId?: string;
  macAddress: string;
  timestamp?: string;
  value: T;
  metricUnit: string;
}

/** Type-erased TelemetryPacket<T> as stored by the gateway, with the rolling baseline used to score it. */
export interface TelemetrySnapshot {
  packetId: string;
  macAddress: string;
  timestamp: string;
  value: number | boolean | null;
  metricUnit: string;
  payloadType: string;
  isAnomaly: boolean;
  zScore: number | null;
  rollingMean: number | null;
  rollingStdDev: number | null;
  signalStrengthDbm: number | null;
  batteryPercent: number | null;
}

export interface DeviceConnectionStatus {
  isOnline: boolean;
  secondsSinceLastPacket: number | null;
  leaseSeconds: number;
  packetsReceived: number;
  packetDeliveryPercent: number | null;
}

export type AlertKind = 'Anomaly' | 'HeartbeatLost';

export interface DeviceAlert {
  id: string;
  macAddress: string;
  kind: AlertKind;
  raisedAt: string;
  message: string;
  triggerValue: number | null;
  baselineMean: number | null;
  zScore: number | null;
  deviation: number | null;
  acknowledgedAt: string | null;
  acknowledgedBy: string | null;
  notes: string | null;
  isActive: boolean;
}

export interface DeviceView {
  device: SensorDevice;
  latestTelemetry: TelemetrySnapshot | null;
  status: DeviceConnectionStatus;
  activeAlerts: DeviceAlert[];
}

export interface PingResult {
  macAddress: string;
  reachable: boolean;
  secondsSinceLastPacket: number | null;
  leaseSeconds: number;
  lastSignalStrengthDbm: number | null;
  lastBatteryPercent: number | null;
  checkedAt: string;
}

export interface LiveTopologyNode {
  nodeId: string;
  name: string;
  kind: 'gateway' | 'facility' | 'zone' | 'device';
  macAddress: string | null;
  isActive: boolean;
  isDegraded: boolean;
  totalDevices: number;
  onlineDevices: number;
  activeAlerts: number;
  availabilityPercent: number | null;
  packetDeliveryPercent: number | null;
  subNodes: LiveTopologyNode[];
}

export interface TopologyValidationResult {
  isValid: boolean;
  reason: string;
  auditTrail: string[];
}

export interface LiveTopology {
  root: LiveTopologyNode;
  validation: TopologyValidationResult;
}

export type IncidentSeverity = 'Low' | 'Medium' | 'High' | 'Critical' | number;
export type IncidentStatus = 'Reported' | 'InTriage' | 'Resolved' | number;

export interface IncidentIssue {
  id: string;
  macAddress: string;
  title: string;
  description: string;
  severity: IncidentSeverity;
  status: IncidentStatus;
  reportedBy: string;
  reportedAt: string;
  triagedAt?: string | null;
  resolvedAt?: string | null;
  resolutionNotes?: string | null;
  xpAwarded: number;
}

export interface OperatorEngagementStats {
  totalReported: number;
  totalInTriage: number;
  totalResolved: number;
  operatorXp: number;
  operatorLevel: number;
  operatorRank: string;
  resolutionRatePercent: number;
  badges: string[];
  meanTimeToTriageSeconds: number | null;
  meanTimeToDetectSeconds: number | null;
}

export interface PowerAggregationResult {
  meter1Watts: number;
  meter2Watts: number;
  aggregatedWatts: number;
  deltaWatts: number;
  isMeter1Greater: boolean;
  isMeter1Lesser: boolean;
  areEqual: boolean;
  formulaApplied: string;
  executionEngine: string;
}

export interface ChannelStatus {
  channelIndex: number;
  allocatedLength: number;
  estimatedBytes: number;
  sampleValues: number[];
}

export interface IngestionStateMetrics {
  currentCount: number;
  capacity: number;
  totalIngested: number;
  droppedPackets: number;
  lastIngestedAt: string | null;
}

export interface JaggedBufferStatus {
  totalChannels: number;
  totalBufferedReadings: number;
  totalEstimatedBytes: number;
  channels: ChannelStatus[];
  customCollectionMetrics: IngestionStateMetrics;
}

export interface GatewayHealth {
  status: string;
  gatewayVersion: string;
  uptimeSeconds: number;
  activeDeviceCount: number;
  registeredDeviceCount: number;
  availabilityPercent: number | null;
  packetDeliveryPercent: number | null;
  totalTelemetryPackets: number;
  totalIngestedPackets: number;
  jaggedBufferedReadings: number;
  bufferMetrics: IngestionStateMetrics;
  anomaliesLastMinute: number;
  activeAlertCount: number;
  meanTimeToDetectSeconds: number | null;
  leaseSeconds: number;
  operatorEngagement: OperatorEngagementStats;
  timestamp: string;
}
