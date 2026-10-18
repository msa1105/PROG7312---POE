import { Undo2, History, CheckCircle2, Clock, XCircle, Send, Sparkles } from 'lucide-react';
import { clsx } from 'clsx';
import type { CommandBatch, CommandState, CommandStatus } from '../../types/telemetry';

type CommandHistoryPanelProps = {
  commands: CommandState | null;
  onUndo: () => void;
  undoing: boolean;
};

const statusStyle: Record<CommandStatus, string> = {
  Queued: 'text-zinc-400 border-zinc-700',
  Delivered: 'text-cyan-300 border-cyan-800',
  Acknowledged: 'text-emerald-300 border-emerald-800',
  Failed: 'text-rose-300 border-rose-800',
  Expired: 'text-amber-300 border-amber-800',
};

const StatusIcon = ({ status }: { status: CommandStatus }) => {
  if (status === 'Acknowledged') return <CheckCircle2 size={10} />;
  if (status === 'Delivered') return <Send size={10} />;
  if (status === 'Queued') return <Clock size={10} />;
  return <XCircle size={10} />;
};

const BatchRow = ({ batch }: { batch: CommandBatch }) => (
  <li className={clsx('p-2.5 rounded-sm border bg-zinc-950', batch.isReverted ? 'border-zinc-800 opacity-60' : 'border-zinc-800')}>
    <div className="flex items-center justify-between gap-2">
      <span className={clsx('font-bold truncate', batch.isReverted ? 'line-through text-zinc-500' : 'text-zinc-200')}>{batch.label}</span>
      <span className="text-[10px] text-zinc-500 shrink-0">{new Date(batch.issuedAt).toLocaleTimeString()}</span>
    </div>
    <div className="flex flex-wrap items-center gap-1 mt-1">
      {batch.source !== 'Operator' && (
        <span className={clsx('text-[9px] px-1 rounded border flex items-center gap-0.5', batch.source === 'Undo' ? 'border-purple-800 text-purple-300' : 'border-cyan-800 text-cyan-300')}>
          {batch.source === 'Suggestion' && <Sparkles size={9} />} {batch.source}
        </span>
      )}
      {batch.isReverted && <span className="text-[9px] px-1 rounded border border-zinc-700 text-zinc-400">Reverted</span>}
      {batch.commands.map(c => (
        <span key={c.id} className={clsx('text-[9px] px-1 rounded border flex items-center gap-0.5', statusStyle[c.status])} title={c.deviceMessage ?? c.status}>
          <StatusIcon status={c.status} /> {c.deviceName} {c.targetState ? 'ON' : 'OFF'} · {c.status}
        </span>
      ))}
    </div>
  </li>
);

/** Command history with the Stack&lt;T&gt;-backed Undo of the most recent operator action. */
export const CommandHistoryPanel = ({ commands, onUndo, undoing }: CommandHistoryPanelProps) => {
  const top = commands?.undoStack.top;
  const depth = commands?.undoStack.depth ?? 0;

  return (
    <section className="bg-zinc-900 border border-zinc-800 rounded-sm p-4 font-mono text-xs space-y-3" aria-label="Command history">
      <div className="flex items-center justify-between">
        <h3 className="text-zinc-300 font-bold uppercase tracking-wider flex items-center gap-2">
          <History size={14} className="text-purple-400" /> Command History
        </h3>
        <span className="text-[10px] text-zinc-500">Undo stack depth: {depth}</span>
      </div>

      <button
        onClick={onUndo}
        disabled={depth === 0 || undoing}
        className="w-full flex items-center justify-center gap-2 px-3 py-2 bg-purple-950/60 hover:bg-purple-900 border border-purple-800 text-purple-200 rounded-sm disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer"
        title="Ctrl+Z"
      >
        <Undo2 size={13} /> {top ? `Undo: ${top.label}` : 'Nothing to undo'}
      </button>

      <ul className="space-y-2 max-h-[340px] overflow-y-auto pr-1">
        {!commands || commands.history.length === 0
          ? <li className="text-zinc-600 italic text-center py-4">No commands issued yet.</li>
          : commands.history.map(batch => <BatchRow key={batch.id} batch={batch} />)}
      </ul>
    </section>
  );
};
