export const SensorCategory = {
  Environmental: 0,
  PowerConsumption: 1,
  Actuator: 2
} as const;

export type SensorCategory = (typeof SensorCategory)[keyof typeof SensorCategory];

export interface DeviceAttachment {
  fileName: string;
  filePath: string;
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
  resolvedAt?: string;
  resolutionNotes?: string;
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

export interface JaggedBufferStatus {
  totalChannels: number;
  totalBufferedReadings: number;
  totalEstimatedBytes: number;
  channels: ChannelStatus[];
  customCollectionMetrics: {
    currentCount: number;
    capacity: number;
    totalIngested: number;
    droppedPackets: number;
    lastIngestedAt: string | null;
  };
}

export interface GatewayHealth {
  status: string;
  gatewayVersion: string;
  uptimeSeconds: number;
  activeDeviceCount: number;
  totalIngestedPackets: number;
  operatorEngagement: OperatorEngagementStats;
  timestamp: string;
}
