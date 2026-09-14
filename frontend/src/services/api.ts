import axios from 'axios';
import type { 
  SensorDevice, 
  TelemetryPacket, 
  IncidentIssue, 
  OperatorEngagementStats, 
  PowerAggregationResult, 
  JaggedBufferStatus, 
  GatewayHealth 
} from '../types/telemetry';

const API_BASE_URL = import.meta.env.VITE_API_URL || (typeof window !== 'undefined' && window.location.port === '3000' ? '/api' : 'http://localhost:5240/api');

const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

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

  getAllDevices: async (): Promise<{ device: SensorDevice, latestTelemetry: any }[]> => {
    const response = await apiClient.get('/sensors');
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
    const response = await apiClient.post(`/sensors/${macAddress}/attachments`, formData, {
      headers: {
        'Content-Type': 'multipart/form-data',
      },
    });
    return response.data;
  },

  getAttachmentDownloadUrl: (macAddress: string, fileName: string): string => {
    return `${API_BASE_URL}/sensors/${encodeURIComponent(macAddress)}/attachments/${encodeURIComponent(fileName)}`;
  },
  
  // Topology Validation
  validateTopology: async (rootNode: any) => {
    const response = await apiClient.post('/topology/validate', rootNode);
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
    const response = await apiClient.patch<IncidentIssue>(`/issues/${id}/status`, { status, notes });
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

  simulationBurst: async () => {
    const response = await apiClient.post('/simulation/burst-batch');
    return response.data;
  },

  simulationReset: async () => {
    const response = await apiClient.post('/simulation/reset');
    return response.data;
  }
};
