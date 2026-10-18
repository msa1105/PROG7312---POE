import { useState } from 'react';
import { Sparkles, Lightbulb, Check, X, Eye, BrainCircuit } from 'lucide-react';
import { clsx } from 'clsx';
import type { ActionSuggestion, AlertSeverity, RecommendationSnapshot } from '../../types/telemetry';

type RecommendationPanelProps = {
  recommendations: RecommendationSnapshot | null;
  onAccept: (suggestion: ActionSuggestion) => Promise<void>;
  onDismiss: (suggestion: ActionSuggestion) => Promise<void>;
  onFocusDevice: (mac: string) => void;
};

const severityStyle: Record<AlertSeverity, string> = {
  Critical: 'border-rose-800 bg-rose-950/30 text-rose-200',
  High: 'border-amber-800 bg-amber-950/20 text-amber-200',
  Warning: 'border-zinc-700 bg-zinc-950 text-zinc-300',
};

/** "Suggested Actions" (learned from operator history) and "Automated Insights" (rule and trend analysis). */
export const RecommendationPanel = ({ recommendations, onAccept, onDismiss, onFocusDevice }: RecommendationPanelProps) => {
  const [busyId, setBusyId] = useState<string | null>(null);

  const act = async (id: string, action: () => Promise<void>) => {
    setBusyId(id);
    try { await action(); } finally { setBusyId(null); }
  };

  const suggestions = recommendations?.suggestions ?? [];
  const insights = recommendations?.insights ?? [];
  const model = recommendations?.model;

  return (
    <section className="bg-zinc-900 border border-cyan-900/60 rounded-sm p-4 font-mono text-xs space-y-4" aria-label="Predictive recommendations">
      <div>
        <h3 className="text-zinc-300 font-bold uppercase tracking-wider flex items-center gap-2 mb-2">
          <Sparkles size={14} className="text-cyan-400" /> Suggested Actions
        </h3>
        {suggestions.length === 0 ? (
          <p className="text-zinc-600 italic">No learned action applies to the current conditions.</p>
        ) : (
          <ul className="space-y-2">
            {suggestions.map(s => (
              <li key={s.id} className="p-2.5 rounded-sm border border-cyan-900/70 bg-cyan-950/20">
                <div className="flex items-start justify-between gap-2">
                  <span className="text-cyan-100 font-bold">{s.title}</span>
                  <span className="text-[10px] text-cyan-400 shrink-0">{Math.round(s.confidence * 100)}%</span>
                </div>
                <div className="w-full bg-zinc-900 h-1 mt-1.5 rounded-full overflow-hidden">
                  <div className="bg-cyan-500 h-full" style={{ width: `${Math.round(s.confidence * 100)}%` }} />
                </div>
                <p className="text-[11px] text-zinc-400 mt-1.5 leading-relaxed">{s.reason}</p>
                <div className="flex gap-1.5 mt-2">
                  <button
                    disabled={busyId === s.id}
                    onClick={() => act(s.id, () => onAccept(s))}
                    className="flex items-center gap-1 px-2 py-1 rounded-sm bg-cyan-600 hover:bg-cyan-500 text-zinc-950 font-bold disabled:opacity-50 cursor-pointer"
                  >
                    {s.kind === 'Command' ? <><Check size={11} /> Apply</> : <><Eye size={11} /> Inspect</>}
                  </button>
                  <button
                    disabled={busyId === s.id}
                    onClick={() => act(s.id, () => onDismiss(s))}
                    className="flex items-center gap-1 px-2 py-1 rounded-sm border border-zinc-700 text-zinc-400 hover:text-zinc-200 disabled:opacity-50 cursor-pointer"
                  >
                    <X size={11} /> Dismiss
                  </button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>

      <div>
        <h3 className="text-zinc-300 font-bold uppercase tracking-wider flex items-center gap-2 mb-2">
          <Lightbulb size={14} className="text-amber-400" /> Automated Insights
        </h3>
        {insights.length === 0 ? (
          <p className="text-zinc-600 italic">No problematic devices detected.</p>
        ) : (
          <ul className="space-y-1.5">
            {insights.map(i => (
              <li key={i.id}>
                <button onClick={() => onFocusDevice(i.macAddress)} className={clsx('w-full text-left p-2 rounded-sm border cursor-pointer hover:brightness-125', severityStyle[i.severity])}>
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-bold">{i.title}</span>
                    <span className="text-[9px] uppercase opacity-80 shrink-0">{i.category}</span>
                  </div>
                  <div className="text-[10px] opacity-80 mt-0.5">{i.detail}</div>
                  <div className="text-[10px] mt-0.5">→ {i.recommendation}</div>
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>

      {model && (
        <div className="text-[10px] text-zinc-500 border-t border-zinc-800 pt-2 flex items-start gap-1.5">
          <BrainCircuit size={12} className="shrink-0 mt-px" />
          <span>
            Learned from {model.eventsLearned} operator event(s) ({model.seededEvents} seeded training) • {model.conditionPatterns} condition patterns •
            {' '}{model.sequencePatterns} sequence patterns • {model.activeConditions} live condition(s)
          </span>
        </div>
      )}
    </section>
  );
};
