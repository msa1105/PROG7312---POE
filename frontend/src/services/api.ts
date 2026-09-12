import axios from 'axios';
import type { SensorDevice, TelemetryPacket } from '../types/telemetry';

const API_BASE_URL = 'http://localhost:5000/api';

const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

export const api = {
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

  // Attachments
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
  
  // Topology Validation
  validateTopology: async (rootNode: any) => {
    const response = await apiClient.post('/topology/validate', rootNode);
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
