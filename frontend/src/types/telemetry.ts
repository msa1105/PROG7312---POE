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
  relayOn?: boolean | null;
}

export interface DeviceConnectionStatus {
  isOnline: boolean;
  secondsSinceLastPacket: number | null;
  leaseSeconds: number;
  packetsReceived: number;
  packetDeliveryPercent: number | null;
}

export type AlertKind = 'Anomaly' | 'HeartbeatLost' | 'ThresholdBreach';
export type AlertSeverity = 'Critical' | 'High' | 'Warning';

export interface DeviceAlert {
  id: string;
  macAddress: string;
  kind: AlertKind;
  severity: AlertSeverity;
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
  deviceName: string;
  latestTelemetry: TelemetrySnapshot | null;
  status: DeviceConnectionStatus;
  activeAlerts: DeviceAlert[];
  isControllable: boolean;
  reportedState: boolean | null;
  pendingCommands: number;
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

// ---------- Part 2: Real-Time Command Stream ----------

export type CommandStatus = 'Queued' | 'Delivered' | 'Acknowledged' | 'Failed' | 'Expired';
export type CommandSource = 'Operator' | 'Undo' | 'Suggestion';

export interface DeviceCommand {
  id: string;
  batchId: string;
  macAddress: string;
  deviceName: string;
  targetState: boolean;
  previousState: boolean | null;
  source: CommandSource;
  issuedBy: string;
  issuedAt: string;
  status: CommandStatus;
  deliveredAt: string | null;
  completedAt: string | null;
  appliedState: boolean | null;
  deviceMessage: string | null;
  description: string;
  isPending: boolean;
}

export interface CommandBatch {
  id: string;
  label: string;
  source: CommandSource;
  issuedAt: string;
  commands: DeviceCommand[];
  revertsBatchId: string | null;
  isReverted: boolean;
  revertedAt: string | null;
}

export interface CommandStackSummary {
  depth: number;
  top: CommandBatch | null;
}

export interface CommandState {
  history: CommandBatch[];
  undoStack: CommandStackSummary;
}

export type IngestionLane = 'Standard' | 'Priority';

export interface LaneStats {
  depth: number;
  peakDepth: number;
  enqueued: number;
  processed: number;
  averageWaitMs: number;
  lastWaitMs: number;
}

export interface PriorityBypass {
  sequence: number;
  macAddress: string;
  reason: string;
  overtookStandardPackets: number;
  waitMs: number;
  processedAt: string;
}

export interface IngestionQueueStats {
  standard: LaneStats;
  priority: LaneStats;
  rejected: number;
  standardCapacity: number;
  simulatedProcessingCostMs: number;
  lastBypass: PriorityBypass | null;
  largestBypass: PriorityBypass | null;
  recentBypasses: PriorityBypass[];
}

export interface AlertTrackerStats {
  activeAlerts: number;
  disconnectedNodes: number;
  duplicateAlertsSuppressed: number;
  duplicateDisconnectsSuppressed: number;
  totalRaised: number;
}

export interface PipelineState {
  queue: IngestionQueueStats;
  alerts: AlertTrackerStats;
  disconnectedNodes: string[];
}

export interface TimelinePoint {
  timestamp: string;
  value: number | null;
  min: number | null;
  max: number | null;
  samples: number;
  isAnomaly: boolean;
}

export type CriticalEventKind = 'MoistureCrash' | 'PowerSurge';

// ---------- Part 2: Predictive Action & Recommendation Engine ----------

export interface ActiveCondition {
  key: string;
  kind: string;
  macAddress: string;
  deviceName: string;
  label: string;
  severity: AlertSeverity;
}

export interface ActionSuggestion {
  id: string;
  kind: 'Command' | 'Inspect';
  title: string;
  reason: string;
  confidence: number;
  score: number;
  support: number;
  episodes: number;
  macAddress: string;
  deviceName: string;
  targetState: boolean | null;
  triggerLabel: string;
}

export interface AutomatedInsight {
  id: string;
  severity: AlertSeverity;
  category: string;
  macAddress: string;
  deviceName: string;
  title: string;
  detail: string;
  recommendation: string;
}

export interface PatternModelStats {
  eventsLearned: number;
  seededEvents: number;
  conditionPatterns: number;
  sequencePatterns: number;
  activeConditions: number;
}

export interface RecommendationSnapshot {
  suggestions: ActionSuggestion[];
  insights: AutomatedInsight[];
  conditions: ActiveCondition[];
  model: PatternModelStats;
  generatedAt: string;
}

export interface OperatorEvent {
  at: string;
  kind: 'Search' | 'Command';
  actionKey: string;
  label: string;
  query: string | null;
  seeded: boolean;
}
