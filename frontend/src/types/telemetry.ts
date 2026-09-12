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
