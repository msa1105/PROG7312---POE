import { createContext, useContext } from 'react';
import type { CommandState, DeviceAlert, DeviceView, PipelineState, RecommendationSnapshot } from '../types/telemetry';

export type SensorHistory = Record<string, { lastPacketId: string | null; values: number[] }>;
export type ConnectionState = 'connecting' | 'live' | 'reconnecting' | 'offline';
export type AlertToast = { id: string; alert: DeviceAlert; deviceName: string };

export type GatewayStoreValue = {
  devices: DeviceView[];
  deviceMap: Record<string, DeviceView>;
  /** Client time each device view was last received; used to advance offline timers between pushes. */
  receivedAt: Record<string, number>;
  history: SensorHistory;
  commands: CommandState | null;
  pipeline: PipelineState | null;
  recommendations: RecommendationSnapshot | null;
  connection: ConnectionState;
  isBackendOnline: boolean;
  toasts: AlertToast[];
  dismissToast: (id: string) => void;
  refreshDevices: () => Promise<void>;
  refreshCommands: () => Promise<void>;
  refreshRecommendations: () => Promise<void>;
  refreshPipeline: () => Promise<void>;
  clearHistory: () => void;

  // Per-module UI state that must persist across route changes
  zoneFilter: string | null;
  setZoneFilter: (zone: string | null) => void;
  topologyTab: 'tree' | 'console';
  setTopologyTab: (tab: 'tree' | 'console') => void;
  selectedMac: string | null;
  setSelectedMac: (mac: string | null) => void;
  showTriageDeck: boolean;
  setShowTriageDeck: (show: boolean) => void;
  reportRequest: { id: number; mac: string } | null;
  requestIncidentReport: (mac: string) => void;
  /** Last query string of the Command Stream page, restored when navigating back to the module. */
  commandStreamSearch: string;
  setCommandStreamSearch: (search: string) => void;
};

export const GatewayStoreContext = createContext<GatewayStoreValue | null>(null);

export const useGatewayStore = (): GatewayStoreValue => {
  const store = useContext(GatewayStoreContext);
  if (!store) throw new Error('useGatewayStore must be used inside <GatewayStoreProvider>');
  return store;
};

