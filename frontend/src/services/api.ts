import axios from 'axios';
import type {
  SensorDevice,
  TelemetryPacket,
  IncidentIssue,
  OperatorEngagementStats,
  PowerAggregationResult,
  JaggedBufferStatus,
  GatewayHealth,
  DeviceView,
  TelemetrySnapshot,
  PingResult,
  DeviceAlert,
  LiveTopology,
  TopologyValidationResult,
  CommandBatch,
  CommandState,
  CommandStackSummary,
  PipelineState,
  TimelinePoint,
  CriticalEventKind,
  RecommendationSnapshot,
  OperatorEvent
} from '../types/telemetry';

// Same-origin '/api' is proxied to the .NET backend by Vite (dev/preview) and nginx (Docker)
export const API_BASE_URL: string = import.meta.env.VITE_API_URL || '/api';

// SignalR hub lives beside the API (proxied the same way, including the WebSocket upgrade)
export const HUB_URL: string = import.meta.env.VITE_HUB_URL
  || (API_BASE_URL.startsWith('http') ? API_BASE_URL.replace(/\/api\/?$/, '/hubs/gateway') : '/hubs/gateway');

const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

/** Extracts the most useful human-readable message from an API error. */
export const getApiErrorMessage = (err: unknown, fallback: string): string => {
  if (axios.isAxiosError(err)) {
    const data = err.response?.data;
    if (typeof data === 'string' && data.trim()) return data;
    if (data?.error) return String(data.error);
    if (data?.detail) return String(data.detail);
    if (data?.title) return String(data.title);
    return err.message || fallback;
  }
  return err instanceof Error ? err.message : fallback;
};

const mac = (macAddress: string) => encodeURIComponent(macAddress);

export const api = {
  // Gateway Status
  getGatewayHealth: async (): Promise<GatewayHealth> => {
    const response = await apiClient.get<GatewayHealth>('/health');
    return response.data;
  },

  // Device Registry
  registerDevice: async (device: Partial<SensorDevice>): Promise<SensorDevice> => {
    const response = await apiClient.post<SensorDevice>('/sensors/register', device);
    return response.data;
  },

  getAllDevices: async (): Promise<DeviceView[]> => {
    const response = await apiClient.get<DeviceView[]>('/sensors');
    return response.data;
  },

  getDeviceHistory: async (macAddress: string): Promise<TelemetrySnapshot[]> => {
    const response = await apiClient.get<TelemetrySnapshot[]>(`/sensors/${mac(macAddress)}/history`);
    return response.data;
  },

  // Human-in-the-Loop inline triage actions
  pingDevice: async (macAddress: string): Promise<PingResult> => {
    const response = await apiClient.post<PingResult>(`/sensors/${mac(macAddress)}/ping`);
    return response.data;
  },

  restartIngestionBuffer: async (macAddress: string): Promise<{ message: string }> => {
    const response = await apiClient.post(`/sensors/${mac(macAddress)}/buffer/restart`);
    return response.data;
  },

  acknowledgeAlerts: async (macAddress: string, notes?: string): Promise<{ acknowledgedCount: number; alerts: DeviceAlert[]; meanTimeToDetectSeconds: number | null }> => {
    const response = await apiClient.post(`/sensors/${mac(macAddress)}/alerts/acknowledge`, { notes, acknowledgedBy: 'Operator-Gateway' });
    return response.data;
  },

  // Telemetry Ingestion (for simulator)
  ingestFloatTelemetry: async (packet: TelemetryPacket<number>) => {
    const response = await apiClient.post('/telemetry/ingest/float', packet);
    return response.data;
  },

  ingestIntTelemetry: async (packet: TelemetryPacket<number>) => {
    const response = await apiClient.post('/telemetry/ingest/int', packet);
    return response.data;
  },

  ingestPowerTelemetry: async (packet: TelemetryPacket<{ watts: number }>) => {
    const response = await apiClient.post('/telemetry/ingest/power', packet);
    return response.data;
  },

  ingestBoolTelemetry: async (packet: TelemetryPacket<boolean>) => {
    const response = await apiClient.post('/telemetry/ingest/bool', packet);
    return response.data;
  },

  // Jagged Buffer
  getJaggedBufferStatus: async (): Promise<JaggedBufferStatus> => {
    const response = await apiClient.get<JaggedBufferStatus>('/telemetry/buffer/status');
    return response.data;
  },

  // Operator Overloading API
  aggregatePowerMetrics: async (wattsMeter1: number, wattsMeter2: number): Promise<PowerAggregationResult> => {
    const response = await apiClient.post<PowerAggregationResult>('/telemetry/power-metrics/aggregate', {
      wattsMeter1,
      wattsMeter2
    });
    return response.data;
  },

  // Attachments (Multipart with AES-256 Encryption at Rest)
  uploadAttachment: async (macAddress: string, file: File) => {
    const formData = new FormData();
    formData.append('file', file);
    const response = await apiClient.post(`/sensors/${mac(macAddress)}/attachments`, formData, {
      headers: {
        'Content-Type': 'multipart/form-data',
      },
    });
    return response.data;
  },

  getAttachmentDownloadUrl: (macAddress: string, fileName: string): string => {
    return `${API_BASE_URL}/sensors/${mac(macAddress)}/attachments/${encodeURIComponent(fileName)}`;
  },

  // Topology Validation (400 responses carry the audit result, so they are not treated as errors)
  validateTopology: async (rootNode: unknown): Promise<TopologyValidationResult> => {
    const response = await apiClient.post<TopologyValidationResult>('/topology/validate', rootNode, {
      validateStatus: status => status === 200 || status === 400,
    });
    return response.data;
  },

  getLiveTopology: async (): Promise<LiveTopology> => {
    const response = await apiClient.get<LiveTopology>('/topology/live');
    return response.data;
  },

  // Dynamic Engagement: Issues & Triage
  getIssues: async (macAddress?: string, status?: string): Promise<IncidentIssue[]> => {
    const params: Record<string, string> = {};
    if (macAddress) params.macAddress = macAddress;
    if (status) params.status = status;
    const response = await apiClient.get<IncidentIssue[]>('/issues', { params });
    return response.data;
  },

  reportIssue: async (issue: Partial<IncidentIssue>): Promise<IncidentIssue> => {
    const response = await apiClient.post<IncidentIssue>('/issues', issue);
    return response.data;
  },

  updateIssueStatus: async (id: string, status: number | string, notes?: string): Promise<IncidentIssue> => {
    const response = await apiClient.patch<IncidentIssue>(`/issues/${encodeURIComponent(id)}/status`, { status, notes });
    return response.data;
  },

  getEngagementStats: async (): Promise<OperatorEngagementStats> => {
    const response = await apiClient.get<OperatorEngagementStats>('/issues/stats');
    return response.data;
  },

  // Simulation Controls
  simulationSpike: async (macAddress: string, multiplier: number = 4.5) => {
    const response = await apiClient.post('/simulation/spike', { macAddress, multiplier });
    return response.data;
  },

  simulationDropHeartbeat: async (macAddress: string) => {
    const response = await apiClient.post('/simulation/drop-heartbeat', { macAddress });
    return response.data;
  },

  simulationBurst: async (): Promise<{ processedCount: number; elapsedMs: number }> => {
    const response = await apiClient.post('/simulation/burst-batch');
    return response.data;
  },

  simulationReset: async () => {
    const response = await apiClient.post('/simulation/reset');
    return response.data;
  },

  simulationRestoreHeartbeat: async (macAddress: string) => {
    const response = await apiClient.post('/simulation/restore-heartbeat', { macAddress });
    return response.data;
  },

  simulationCritical: async (macAddress: string, kind: CriticalEventKind) => {
    const response = await apiClient.post('/simulation/critical', { macAddress, kind });
    return response.data;
  },

  simulationFlood: async (macAddress: string, count: number) => {
    const response = await apiClient.post('/simulation/flood', { macAddress, count });
    return response.data;
  },

  // ---------- Part 2: Real-Time Command Stream ----------

  issueCommand: async (targets: { macAddress: string; targetState: boolean }[], label?: string): Promise<CommandBatch> => {
    const response = await apiClient.post<CommandBatch>('/commands', { targets, label, issuedBy: 'Operator-Gateway' });
    return response.data;
  },

  undoLastCommand: async (): Promise<{ reverted: CommandBatch; undoBatch: CommandBatch }> => {
    const response = await apiClient.post('/commands/undo');
    return response.data;
  },

  getCommandState: async (): Promise<CommandState> => {
    const [history, undoStack] = await Promise.all([
      apiClient.get<CommandBatch[]>('/commands/history', { params: { limit: 40 } }),
      apiClient.get<CommandStackSummary>('/commands/stack'),
    ]);
    return { history: history.data, undoStack: undoStack.data };
  },

  getPipelineState: async (): Promise<PipelineState> => {
    const response = await apiClient.get<PipelineState>('/pipeline/stats');
    return response.data;
  },

  setProcessingCost: async (milliseconds: number) => {
    const response = await apiClient.post('/pipeline/processing-cost', { milliseconds });
    return response.data;
  },

  getTimeline: async (macAddress: string, minutes: number, maxPoints = 120): Promise<TimelinePoint[]> => {
    const response = await apiClient.get<TimelinePoint[]>(`/sensors/${mac(macAddress)}/timeline`, { params: { minutes, maxPoints } });
    return response.data;
  },

  logSearch: async (query: string, matchedMacs: string[]) => {
    const response = await apiClient.post('/operator/searches', { query, matchedMacs });
    return response.data;
  },

  getOperatorEvents: async (limit = 20): Promise<OperatorEvent[]> => {
    const response = await apiClient.get<OperatorEvent[]>('/operator/events', { params: { limit } });
    return response.data;
  },

  // ---------- Part 2: Predictive Action & Recommendation Engine ----------

  getRecommendations: async (): Promise<RecommendationSnapshot> => {
    const response = await apiClient.get<RecommendationSnapshot>('/recommendations');
    return response.data;
  },

  acceptSuggestion: async (id: string) => {
    const response = await apiClient.post(`/recommendations/${encodeURIComponent(id)}/accept`);
    return response.data;
  },

  dismissSuggestion: async (id: string) => {
    const response = await apiClient.post(`/recommendations/${encodeURIComponent(id)}/dismiss`);
    return response.data;
  }
};
