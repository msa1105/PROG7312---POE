import type { CommandBatch, CommandSource, CommandState, DeviceCommand, DeviceView, SensorCategory } from '../types/telemetry';

/**
 * Optimistic UI helpers. User actions are applied to a local overlay immediately; the overlay is dropped once the
 * server confirms the change (via SignalR push or the API response) or rolled back if the API rejects it.
 * Reference: useOptimistic / optimistic updates pattern (react.dev) - https://react.dev/reference/react/useOptimistic
 */

/** How long an optimistic device state may wait for the device to report it before it is abandoned. */
export const PENDING_STATE_TIMEOUT_MS = 25_000;

export type PendingState = { target: boolean; since: number };

export type CommandTarget = { macAddress: string; targetState: boolean };

let optimisticSequence = 0;

/** A local stand-in for the batch the API will create, shown in the history until the real one arrives. */
export const buildOptimisticBatch = (
  label: string,
  source: CommandSource,
  targets: CommandTarget[],
  deviceMap: Record<string, DeviceView>
): CommandBatch => {
  const id = `optimistic-${++optimisticSequence}`;
  const issuedAt = new Date().toISOString();
  const commands: DeviceCommand[] = targets.map((t, i) => {
    const deviceName = deviceMap[t.macAddress]?.deviceName ?? t.macAddress;
    return {
      id: `${id}-${i}`,
      batchId: id,
      macAddress: t.macAddress,
      deviceName,
      targetState: t.targetState,
      previousState: deviceMap[t.macAddress]?.reportedState ?? null,
      source,
      issuedBy: 'Operator-Gateway',
      issuedAt,
      status: 'Queued',
      deliveredAt: null,
      completedAt: null,
      appliedState: null,
      deviceMessage: null,
      description: `${deviceName} → ${t.targetState ? 'ON' : 'OFF'}`,
      isPending: true,
    };
  });

  return { id, label, source, issuedAt, commands, revertsBatchId: null, isReverted: false, revertedAt: null, optimistic: true };
};

/** Default label for a command batch, matching the server's naming. */
export const defaultCommandLabel = (targets: CommandTarget[], deviceMap: Record<string, DeviceView>): string =>
  targets.length === 1
    ? `${deviceMap[targets[0].macAddress]?.deviceName ?? targets[0].macAddress} → ${targets[0].targetState ? 'ON' : 'OFF'}`
    : `${targets.length} devices`;

/** The batch Undo would revert: the newest non-reverted operator/suggestion batch. */
export const findUndoTarget = (commands: CommandState | null, locallyReverted: Record<string, number>): CommandBatch | null =>
  commands?.history.find(b => b.source !== 'Undo' && !b.isReverted && !b.optimistic && !(b.id in locallyReverted)) ?? null;

/** Inverse commands for an undo, mirroring the server: only devices whose previous state is known and different. */
export const inverseTargets = (batch: CommandBatch): CommandTarget[] =>
  batch.commands
    .filter(c => c.previousState !== null && c.previousState !== c.targetState)
    .map(c => ({ macAddress: c.macAddress, targetState: c.previousState as boolean }));

export type PendingOutcome = 'confirmed' | 'failed' | 'timeout' | 'pending';

/** Decides whether an optimistic device state has been confirmed by the device, rejected, or should be abandoned. */
export const resolvePending = (
  mac: string,
  pending: PendingState,
  view: DeviceView | undefined,
  commands: CommandState | null,
  now: number
): PendingOutcome => {
  if (view && view.reportedState === pending.target) return 'confirmed';

  const failed = commands?.history.some(batch => batch.commands.some(c =>
    c.macAddress === mac &&
    c.targetState === pending.target &&
    (c.status === 'Failed' || c.status === 'Expired') &&
    new Date(c.issuedAt).getTime() >= pending.since - 5_000));
  if (failed) return 'failed';

  return now - pending.since > PENDING_STATE_TIMEOUT_MS ? 'timeout' : 'pending';
};

/** Applies the optimistic overlay (pending state, locally acknowledged alerts) to one device view. */
export const overlayDevice = (
  view: DeviceView,
  pending: PendingState | undefined,
  acknowledgedAt: number | undefined,
  receivedAt: number | undefined
): DeviceView => {
  let next = view;
  if (pending && view.reportedState !== pending.target) {
    next = { ...next, reportedState: pending.target, optimistic: { ...next.optimistic, pendingState: pending.target } };
  }
  // Alerts acknowledged locally stay hidden until a newer server view arrives
  if (acknowledgedAt !== undefined && (receivedAt ?? 0) < acknowledgedAt && next.activeAlerts.length > 0) {
    next = { ...next, activeAlerts: [] };
  }
  return next;
};

/** Placeholder view for a device registered locally but not yet confirmed by the API. */
export const placeholderDevice = (macAddress: string, deploymentLocation: string, category: SensorCategory): DeviceView => {
  const segments = deploymentLocation.split('>').map(s => s.trim()).filter(Boolean);
  return {
    device: { macAddress, deploymentLocation, category, attachments: [] },
    deviceName: segments.at(-1) ?? macAddress,
    latestTelemetry: null,
    status: { isOnline: false, secondsSinceLastPacket: null, leaseSeconds: 3, packetsReceived: 0, packetDeliveryPercent: null },
    activeAlerts: [],
    isControllable: category !== 0,
    reportedState: null,
    pendingCommands: 0,
    optimistic: { unconfirmedRegistration: true },
  };
};

/** Merges optimistic batches into the server command state and applies local reverts. */
export const mergeCommandState = (
  server: CommandState | null,
  optimisticBatches: CommandBatch[],
  locallyReverted: Record<string, number>
): CommandState | null => {
  if (!server && optimisticBatches.length === 0) return server;

  const serverHistory = server?.history ?? [];
  const serverIds = new Set(serverHistory.map(b => b.id));
  const history = [...optimisticBatches.filter(b => !serverIds.has(b.id)), ...serverHistory]
    .map(b => (b.id in locallyReverted && !b.isReverted ? { ...b, isReverted: true } : b));

  const pendingOperatorBatches = optimisticBatches.filter(b => b.source !== 'Undo').length;
  const localReverts = serverHistory.filter(b => b.id in locallyReverted && !b.isReverted).length;
  const top = history.find(b => b.source !== 'Undo' && !b.isReverted) ?? null;

  return {
    history,
    undoStack: { depth: Math.max(0, (server?.undoStack.depth ?? 0) + pendingOperatorBatches - localReverts), top },
  };
};
