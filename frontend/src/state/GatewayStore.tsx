import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr';
import { api, HUB_URL } from '../services/api';
import type { CommandState, DeviceAlert, DeviceView, PipelineState, RecommendationSnapshot } from '../types/telemetry';
import { GatewayStoreContext, type AlertToast, type ConnectionState, type GatewayStoreValue, type SensorHistory } from './gatewayContext';

/**
 * Application-wide gateway state, mounted above the router so it survives navigation between modules.
 * Live updates arrive over a SignalR hub; REST polling takes over automatically while the hub is unavailable.
 * Reference: ASP.NET Core SignalR JavaScript client (Microsoft Learn) - https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client
 * Reference: Passing data deeply with context (react.dev) - https://react.dev/learn/passing-data-deeply-with-context
 */

const HISTORY_LENGTH = 30;
const FALLBACK_POLL_MS = 3000;

export const GatewayStoreProvider = ({ children }: { children: ReactNode }) => {
  const [deviceMap, setDeviceMap] = useState<Record<string, DeviceView>>({});
  const [receivedAt, setReceivedAt] = useState<Record<string, number>>({});
  const [history, setHistory] = useState<SensorHistory>({});
  const [commands, setCommands] = useState<CommandState | null>(null);
  const [pipeline, setPipeline] = useState<PipelineState | null>(null);
  const [recommendations, setRecommendations] = useState<RecommendationSnapshot | null>(null);
  const [connection, setConnection] = useState<ConnectionState>('connecting');
  const [isBackendOnline, setIsBackendOnline] = useState(true);
  const [toasts, setToasts] = useState<AlertToast[]>([]);

  const [zoneFilter, setZoneFilter] = useState<string | null>(null);
  const [topologyTab, setTopologyTab] = useState<'tree' | 'console'>('tree');
  const [selectedMac, setSelectedMac] = useState<string | null>(null);
  const [showTriageDeck, setShowTriageDeck] = useState(false);
  const [reportRequest, setReportRequest] = useState<{ id: number; mac: string } | null>(null);
  const [commandStreamSearch, setCommandStreamSearch] = useState('');

  const deviceMapRef = useRef(deviceMap);
  useEffect(() => {
    deviceMapRef.current = deviceMap;
  }, [deviceMap]);

  /** Merges device views (full list or SignalR delta) and appends new numeric readings to the sparkline history. */
  const applyDevices = useCallback((views: DeviceView[]) => {
    if (views.length === 0) return;
    const now = Date.now();

    setDeviceMap(prev => {
      const next = { ...prev };
      for (const view of views) next[view.device.macAddress] = view;
      return next;
    });
    setReceivedAt(prev => {
      const next = { ...prev };
      for (const view of views) next[view.device.macAddress] = now;
      return next;
    });
    setHistory(prev => {
      let changed = false;
      const next: SensorHistory = { ...prev };
      for (const view of views) {
        const snapshot = view.latestTelemetry;
        if (!snapshot || typeof snapshot.value !== 'number') continue;
        const entry = next[view.device.macAddress] ?? { lastPacketId: null, values: [] };
        if (entry.lastPacketId === snapshot.packetId) continue;
        next[view.device.macAddress] = { lastPacketId: snapshot.packetId, values: [...entry.values, snapshot.value].slice(-HISTORY_LENGTH) };
        changed = true;
      }
      return changed ? next : prev;
    });
  }, []);

  const refreshDevices = useCallback(async () => {
    try {
      applyDevices(await api.getAllDevices());
      setIsBackendOnline(true);
    } catch {
      setIsBackendOnline(false);
    }
  }, [applyDevices]);

  const refreshCommands = useCallback(async () => {
    try { setCommands(await api.getCommandState()); } catch { /* surfaced by connection state */ }
  }, []);

  const refreshRecommendations = useCallback(async () => {
    try { setRecommendations(await api.getRecommendations()); } catch { /* surfaced by connection state */ }
  }, []);

  const refreshPipeline = useCallback(async () => {
    try { setPipeline(await api.getPipelineState()); } catch { /* surfaced by connection state */ }
  }, []);

  const refreshAll = useCallback(async () => {
    await Promise.all([refreshDevices(), refreshCommands(), refreshRecommendations(), refreshPipeline()]);
  }, [refreshDevices, refreshCommands, refreshRecommendations, refreshPipeline]);

  // SignalR hub lifecycle
  useEffect(() => {
    let disposed = false;
    const hub: HubConnection = new HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
      .configureLogging(LogLevel.Warning)
      .build();

    hub.on('devicesUpdated', (views: DeviceView[]) => applyDevices(views));
    hub.on('commandsUpdated', (state: CommandState) => setCommands(state));
    hub.on('pipelineUpdated', (state: PipelineState) => setPipeline(state));
    hub.on('recommendationsUpdated', (snapshot: RecommendationSnapshot) => setRecommendations(snapshot));
    hub.on('alertRaised', (alert: DeviceAlert) => {
      // Only urgent alerts interrupt the operator; routine 3σ outliers stay latched on their cards
      if (alert.severity === 'Warning') return;
      const deviceName = deviceMapRef.current[alert.macAddress]?.deviceName ?? alert.macAddress;
      const toast = { id: alert.id, alert, deviceName };
      setToasts(prev => (prev.some(t => t.id === toast.id) ? prev : [toast, ...prev].slice(0, 4)));
      setTimeout(() => setToasts(prev => prev.filter(t => t.id !== toast.id)), 7000);
    });

    hub.onreconnecting(() => !disposed && setConnection('reconnecting'));
    hub.onreconnected(() => {
      if (disposed) return;
      setConnection('live');
      refreshAll();
    });
    hub.onclose(() => !disposed && setConnection('offline'));

    const start = async () => {
      await refreshAll();
      try {
        await hub.start();
        if (!disposed) setConnection('live');
      } catch {
        if (!disposed) setConnection('offline');
      }
    };
    start();

    return () => {
      disposed = true;
      if (hub.state !== HubConnectionState.Disconnected) hub.stop();
    };
  }, [applyDevices, refreshAll]);

  // Fallback polling while the push channel is unavailable (and a slow retry to re-establish it)
  useEffect(() => {
    if (connection === 'live') return;
    const interval = setInterval(() => {
      refreshDevices();
      refreshPipeline();
      refreshCommands();
    }, FALLBACK_POLL_MS);
    return () => clearInterval(interval);
  }, [connection, refreshDevices, refreshPipeline, refreshCommands]);

  const devices = useMemo(
    () => Object.values(deviceMap).sort((a, b) => a.device.macAddress.localeCompare(b.device.macAddress)),
    [deviceMap]
  );

  const value: GatewayStoreValue = {
    devices,
    deviceMap,
    receivedAt,
    history,
    commands,
    pipeline,
    recommendations,
    connection,
    isBackendOnline,
    toasts,
    dismissToast: (id: string) => setToasts(prev => prev.filter(t => t.id !== id)),
    refreshDevices,
    refreshCommands,
    refreshRecommendations,
    refreshPipeline,
    clearHistory: () => setHistory({}),
    zoneFilter,
    setZoneFilter,
    topologyTab,
    setTopologyTab,
    selectedMac,
    setSelectedMac,
    showTriageDeck,
    setShowTriageDeck,
    reportRequest,
    commandStreamSearch,
    setCommandStreamSearch,
    requestIncidentReport: (mac: string) => {
      setReportRequest({ id: Date.now(), mac });
      setShowTriageDeck(true);
      setSelectedMac(null);
    },
  };

  return <GatewayStoreContext.Provider value={value}>{children}</GatewayStoreContext.Provider>;
};
