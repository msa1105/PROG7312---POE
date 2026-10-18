import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr';
import { api, getApiErrorMessage, HUB_URL } from '../services/api';
import type { ActionSuggestion, CommandBatch, CommandState, DeviceAlert, DeviceView, PipelineState, RecommendationSnapshot, SensorCategory } from '../types/telemetry';
import { GatewayStoreContext, type AlertToast, type ConnectionState, type GatewayStoreValue, type Notice, type SensorHistory } from './gatewayContext';
import {
  buildOptimisticBatch, defaultCommandLabel, findUndoTarget, inverseTargets, mergeCommandState, overlayDevice,
  placeholderDevice, resolvePending, type CommandTarget, type PendingState
} from './optimistic';

/**
 * Application-wide gateway state, mounted above the router so it survives navigation between modules.
 * Live updates arrive over a SignalR hub; REST polling takes over automatically while the hub is unavailable.
 * User actions are optimistic: they update a local overlay at once and are reconciled with (or rolled back by) the API.
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

  // Optimistic overlay
  const [pendingStates, setPendingStates] = useState<Record<string, PendingState>>({});
  const [optimisticBatches, setOptimisticBatches] = useState<CommandBatch[]>([]);
  const [locallyReverted, setLocallyReverted] = useState<Record<string, number>>({});
  const [hiddenSuggestions, setHiddenSuggestions] = useState<Record<string, number>>({});
  const [acknowledgedAt, setAcknowledgedAt] = useState<Record<string, number>>({});
  const [optimisticDevices, setOptimisticDevices] = useState<Record<string, DeviceView>>({});
  const [recommendationsReceivedAt, setRecommendationsReceivedAt] = useState(0);
  const [notices, setNotices] = useState<Notice[]>([]);

  const deviceMapRef = useRef(deviceMap);
  const commandsRef = useRef(commands);
  useEffect(() => {
    deviceMapRef.current = deviceMap;
    commandsRef.current = commands;
  }, [deviceMap, commands]);

  const pushNotice = useCallback((text: string, tone: 'ok' | 'error' = 'ok') => {
    const notice = { id: Date.now() + Math.random(), text, tone };
    setNotices(prev => [notice, ...prev].slice(0, 3));
    setTimeout(() => setNotices(prev => prev.filter(n => n.id !== notice.id)), tone === 'error' ? 7000 : 4000);
  }, []);

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
    try {
      setRecommendations(await api.getRecommendations());
      setRecommendationsReceivedAt(Date.now());
    } catch { /* surfaced by connection state */ }
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
    hub.on('recommendationsUpdated', (snapshot: RecommendationSnapshot) => {
      setRecommendations(snapshot);
      setRecommendationsReceivedAt(Date.now());
    });
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

  // Reconcile optimistic device states: drop them once the device reports the target state,
  // roll them back when the command failed/expired or was never confirmed
  useEffect(() => {
    const interval = setInterval(() => {
      setPendingStates(prev => {
        const entries = Object.entries(prev);
        if (entries.length === 0) return prev;
        const now = Date.now();
        const next: Record<string, PendingState> = {};
        for (const [mac, pending] of entries) {
          const outcome = resolvePending(mac, pending, deviceMapRef.current[mac], commandsRef.current, now);
          const name = deviceMapRef.current[mac]?.deviceName ?? mac;
          if (outcome === 'pending') next[mac] = pending;
          else if (outcome === 'failed') pushNotice(`${name} did not apply the command; showing its reported state again.`, 'error');
          else if (outcome === 'timeout') pushNotice(`${name} has not confirmed the command yet; showing its reported state.`, 'error');
        }
        return Object.keys(next).length === entries.length ? prev : next;
      });
    }, 1000);
    return () => clearInterval(interval);
  }, [pushNotice]);

  // ---------- Effective (server + optimistic overlay) state ----------

  const effectiveDeviceMap = useMemo(() => {
    const merged: Record<string, DeviceView> = { ...deviceMap, ...optimisticDevices };
    for (const mac of Object.keys(merged)) {
      merged[mac] = overlayDevice(merged[mac], pendingStates[mac], acknowledgedAt[mac], receivedAt[mac]);
    }
    return merged;
  }, [deviceMap, optimisticDevices, pendingStates, acknowledgedAt, receivedAt]);

  const devices = useMemo(
    () => Object.values(effectiveDeviceMap).sort((a, b) => a.device.macAddress.localeCompare(b.device.macAddress)),
    [effectiveDeviceMap]
  );

  const effectiveCommands = useMemo(
    () => mergeCommandState(commands, optimisticBatches, locallyReverted),
    [commands, optimisticBatches, locallyReverted]
  );

  const effectiveRecommendations = useMemo(() => {
    if (!recommendations) return recommendations;
    const visible = recommendations.suggestions.filter(s => !(hiddenSuggestions[s.id] > recommendationsReceivedAt));
    return visible.length === recommendations.suggestions.length ? recommendations : { ...recommendations, suggestions: visible };
  }, [recommendations, hiddenSuggestions, recommendationsReceivedAt]);

  // ---------- Optimistic actions ----------

  const setPending = (targets: CommandTarget[]) => {
    const since = Date.now();
    setPendingStates(prev => {
      const next = { ...prev };
      for (const t of targets) next[t.macAddress] = { target: t.targetState, since };
      return next;
    });
  };

  const clearPending = (targets: CommandTarget[]) => {
    setPendingStates(prev => {
      const next = { ...prev };
      for (const t of targets) delete next[t.macAddress];
      return next;
    });
  };

  /** Records a server-confirmed batch immediately (the SignalR push may arrive slightly later). */
  const mergeServerBatch = (batch: CommandBatch) => {
    setCommands(prev => {
      if (!prev || prev.history.some(b => b.id === batch.id)) return prev;
      return { ...prev, history: [batch, ...prev.history] };
    });
  };

  const issueCommand = async (targets: CommandTarget[], label?: string): Promise<boolean> => {
    if (targets.length === 0) return false;
    const effectiveLabel = label ?? defaultCommandLabel(targets, effectiveDeviceMap);
    const placeholder = buildOptimisticBatch(effectiveLabel, 'Operator', targets, effectiveDeviceMap);

    setPending(targets);
    setOptimisticBatches(prev => [placeholder, ...prev]);
    try {
      const batch = await api.issueCommand(targets, label);
      mergeServerBatch(batch);
      return true;
    } catch (err) {
      clearPending(targets);
      pushNotice(`"${effectiveLabel}" was rejected: ${getApiErrorMessage(err, 'command failed')}`, 'error');
      return false;
    } finally {
      setOptimisticBatches(prev => prev.filter(b => b.id !== placeholder.id));
    }
  };

  const undoLastCommand = async (): Promise<boolean> => {
    const target = findUndoTarget(commands, locallyReverted);
    if (!target) {
      pushNotice('Nothing to undo.', 'error');
      return false;
    }

    const inverse = inverseTargets(target);
    const placeholder = buildOptimisticBatch(`Undo: ${target.label}`, 'Undo', inverse, effectiveDeviceMap);
    setLocallyReverted(prev => ({ ...prev, [target.id]: Date.now() }));
    setPending(inverse);
    if (inverse.length > 0) setOptimisticBatches(prev => [placeholder, ...prev]);

    try {
      const result = await api.undoLastCommand();
      if (result.undoBatch.commands.length > 0) mergeServerBatch(result.undoBatch);
      return true;
    } catch (err) {
      setLocallyReverted(prev => {
        const next = { ...prev };
        delete next[target.id];
        return next;
      });
      clearPending(inverse);
      pushNotice(`Undo failed: ${getApiErrorMessage(err, 'request failed')}`, 'error');
      return false;
    } finally {
      setOptimisticBatches(prev => prev.filter(b => b.id !== placeholder.id));
    }
  };

  // While a request is in flight the overlay wins unconditionally (Infinity); once the API answers, the overlay only
  // hides server snapshots that are older than that answer
  const hideSuggestion = (id: string, until = Number.POSITIVE_INFINITY) => setHiddenSuggestions(prev => ({ ...prev, [id]: until }));
  const unhideSuggestion = (id: string) => setHiddenSuggestions(prev => {
    const next = { ...prev };
    delete next[id];
    return next;
  });

  const acceptSuggestion = async (suggestion: ActionSuggestion): Promise<boolean> => {
    hideSuggestion(suggestion.id);
    const targets = suggestion.kind === 'Command' && suggestion.targetState !== null
      ? [{ macAddress: suggestion.macAddress, targetState: suggestion.targetState }]
      : [];
    const placeholder = buildOptimisticBatch(suggestion.title, 'Suggestion', targets, effectiveDeviceMap);
    setPending(targets);
    if (targets.length > 0) setOptimisticBatches(prev => [placeholder, ...prev]);

    try {
      const result = await api.acceptSuggestion(suggestion.id);
      if (result?.batch) mergeServerBatch(result.batch);
      hideSuggestion(suggestion.id, Date.now());
      return true;
    } catch (err) {
      unhideSuggestion(suggestion.id);
      clearPending(targets);
      pushNotice(`Could not apply "${suggestion.title}": ${getApiErrorMessage(err, 'suggestion is no longer available')}`, 'error');
      return false;
    } finally {
      setOptimisticBatches(prev => prev.filter(b => b.id !== placeholder.id));
    }
  };

  const dismissSuggestion = async (suggestion: ActionSuggestion): Promise<boolean> => {
    hideSuggestion(suggestion.id);
    try {
      await api.dismissSuggestion(suggestion.id);
      hideSuggestion(suggestion.id, Date.now());
      return true;
    } catch (err) {
      unhideSuggestion(suggestion.id);
      pushNotice(`Could not dismiss "${suggestion.title}": ${getApiErrorMessage(err, 'request failed')}`, 'error');
      return false;
    }
  };

  const acknowledgeAlerts = async (macAddress: string, notes?: string): Promise<boolean> => {
    const previous = acknowledgedAt[macAddress];
    setAcknowledgedAt(prev => ({ ...prev, [macAddress]: Number.POSITIVE_INFINITY }));
    try {
      await api.acknowledgeAlerts(macAddress, notes);
      setAcknowledgedAt(prev => ({ ...prev, [macAddress]: Date.now() }));
      return true;
    } catch (err) {
      setAcknowledgedAt(prev => {
        const next = { ...prev };
        if (previous === undefined) delete next[macAddress];
        else next[macAddress] = previous;
        return next;
      });
      pushNotice(`Acknowledgement failed: ${getApiErrorMessage(err, 'request failed')}`, 'error');
      return false;
    }
  };

  const registerDevice = async (device: { macAddress: string; deploymentLocation: string; category: SensorCategory }): Promise<boolean> => {
    const mac = device.macAddress.toUpperCase();
    const existing = deviceMapRef.current[mac];
    const optimisticView: DeviceView = existing
      ? { ...existing, device: { ...existing.device, deploymentLocation: device.deploymentLocation, category: device.category } }
      : placeholderDevice(mac, device.deploymentLocation, device.category);
    setOptimisticDevices(prev => ({ ...prev, [mac]: optimisticView }));

    try {
      await api.registerDevice({ ...device, macAddress: mac });
      await refreshDevices();
      return true;
    } catch (err) {
      pushNotice(`Registration of ${mac} was rejected: ${getApiErrorMessage(err, 'request failed')}`, 'error');
      return false;
    } finally {
      setOptimisticDevices(prev => {
        const next = { ...prev };
        delete next[mac];
        return next;
      });
    }
  };

  const value: GatewayStoreValue = {
    devices,
    deviceMap: effectiveDeviceMap,
    receivedAt,
    history,
    commands: effectiveCommands,
    pipeline,
    recommendations: effectiveRecommendations,
    connection,
    isBackendOnline,
    toasts,
    dismissToast: (id: string) => setToasts(prev => prev.filter(t => t.id !== id)),
    refreshDevices,
    refreshCommands,
    refreshRecommendations,
    refreshPipeline,
    clearHistory: () => setHistory({}),
    notices,
    pushNotice,
    issueCommand,
    undoLastCommand,
    canUndo: optimisticBatches.length === 0 && findUndoTarget(commands, locallyReverted) !== null,
    acceptSuggestion,
    dismissSuggestion,
    acknowledgeAlerts,
    registerDevice,
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
