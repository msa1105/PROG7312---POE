import { createContext, useContext } from 'react';
import type { ActionSuggestion, CommandState, DeviceAlert, DeviceView, PipelineState, RecommendationSnapshot, SensorCategory } from '../types/telemetry';
import type { CommandTarget } from './optimistic';

export type SensorHistory = Record<string, { lastPacketId: string | null; values: number[] }>;
export type ConnectionState = 'connecting' | 'live' | 'reconnecting' | 'offline';
export type AlertToast = { id: string; alert: DeviceAlert; deviceName: string };
export type Notice = { id: number; text: string; tone: 'ok' | 'error' };

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

  // Optimistic actions: the UI updates immediately; the API confirms or the change is rolled back
  notices: Notice[];
  pushNotice: (text: string, tone?: 'ok' | 'error') => void;
  /** Issues override commands. Resolves to false (after rolling back) when the API rejects them. */
  issueCommand: (targets: CommandTarget[], label?: string) => Promise<boolean>;
  undoLastCommand: () => Promise<boolean>;
  canUndo: boolean;
  acceptSuggestion: (suggestion: ActionSuggestion) => Promise<boolean>;
  dismissSuggestion: (suggestion: ActionSuggestion) => Promise<boolean>;
  acknowledgeAlerts: (macAddress: string, notes?: string) => Promise<boolean>;
  registerDevice: (device: { macAddress: string; deploymentLocation: string; category: SensorCategory }) => Promise<boolean>;

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

