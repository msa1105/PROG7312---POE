import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useSearchParams } from 'react-router-dom';
import { Radio } from 'lucide-react';
import { PipelineMonitor } from '../components/commands/PipelineMonitor';
import { DeviceControlPanel } from '../components/commands/DeviceControlPanel';
import { ALERT_FILTERS, matchesFilters, type AlertStateFilter } from '../utils/commandFilters';
import { CommandHistoryPanel } from '../components/commands/CommandHistoryPanel';
import { RecommendationPanel } from '../components/commands/RecommendationPanel';
import { TimelineChart } from '../components/commands/TimelineChart';
import { api, getApiErrorMessage } from '../services/api';
import { useGatewayStore } from '../state/gatewayContext';
import type { ActionSuggestion } from '../types/telemetry';

const SEARCH_LOG_DELAY_MS = 1200;
const MAX_LEARNABLE_MATCHES = 3;

/**
 * Pillar 2: Real-Time Command Stream & History.
 * Filters and the selected device live in the URL query string, so they survive navigation, refresh and back/forward.
 * Reference: useSearchParams (React Router) - https://reactrouter.com/api/hooks/useSearchParams
 */
const CommandStreamPage = () => {
  const store = useGatewayStore();
  const [params, setParams] = useSearchParams();
  const [feedback, setFeedback] = useState<{ text: string; tone: 'ok' | 'error' } | null>(null);
  const [undoing, setUndoing] = useState(false);
  const lastLoggedSearch = useRef<string>('');
  const location = useLocation();
  const { setCommandStreamSearch } = store;

  // Remember this module's filters so returning via the navigation restores them
  useEffect(() => {
    setCommandStreamSearch(location.search);
  }, [location.search, setCommandStreamSearch]);

  const query = params.get('q') ?? '';
  const category = params.get('category') ?? 'all';
  const alertParam = params.get('alert') as AlertStateFilter | null;
  const alertState: AlertStateFilter = alertParam && ALERT_FILTERS.includes(alertParam) ? alertParam : 'all';
  const windowMinutes = Number(params.get('window')) || 5;

  const updateParam = useCallback((key: string, value: string | null, defaultValue = '') => {
    setParams(prev => {
      const next = new URLSearchParams(prev);
      if (value === null || value === defaultValue) next.delete(key);
      else next.set(key, value);
      return next;
    }, { replace: true });
  }, [setParams]);

  const matches = useMemo(
    () => store.devices.filter(d => matchesFilters(d, query, category, alertState)),
    [store.devices, query, category, alertState]
  );

  // Selected device for the timeline: explicit choice, else the most urgent match
  const selectedMac = params.get('device')
    ?? matches.find(d => d.activeAlerts.some(a => a.severity === 'Critical'))?.device.macAddress
    ?? matches[0]?.device.macAddress
    ?? null;
  const selectedDevice = selectedMac ? store.deviceMap[selectedMac] ?? null : null;

  // Log specific searches (after the operator stops typing) so the engine can learn search -> action patterns
  const matchKey = matches.map(m => m.device.macAddress).join(',');
  useEffect(() => {
    const term = query.trim();
    if (!term || term === lastLoggedSearch.current) return;
    const macs = matchKey ? matchKey.split(',') : [];
    if (macs.length === 0 || macs.length > MAX_LEARNABLE_MATCHES) return;

    const timer = setTimeout(() => {
      lastLoggedSearch.current = term;
      api.logSearch(term, macs).catch(() => undefined);
    }, SEARCH_LOG_DELAY_MS);
    return () => clearTimeout(timer);
  }, [query, matchKey]);

  const flash = (text: string, tone: 'ok' | 'error' = 'ok') => {
    setFeedback({ text, tone });
    setTimeout(() => setFeedback(null), 4500);
  };

  const issueCommand = async (targets: { macAddress: string; targetState: boolean }[], label?: string) => {
    try {
      const batch = await api.issueCommand(targets, label);
      flash(`Queued “${batch.label}” — waiting for device acknowledgement.`);
    } catch (err) {
      flash(getApiErrorMessage(err, 'Command failed'), 'error');
    }
  };

  const undo = useCallback(async () => {
    setUndoing(true);
    try {
      const result = await api.undoLastCommand();
      flash(`Reverted “${result.reverted.label}”.`);
    } catch (err) {
      flash(getApiErrorMessage(err, 'Nothing to undo'), 'error');
    }
    setUndoing(false);
  }, []);

  // Ctrl/Cmd+Z undoes the last command (ignored while typing in a field)
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      if (target && ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName)) return;
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z') {
        e.preventDefault();
        undo();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [undo]);

  const accept = async (suggestion: ActionSuggestion) => {
    try {
      await api.acceptSuggestion(suggestion.id);
      if (suggestion.kind === 'Inspect') updateParam('device', suggestion.macAddress);
      flash(suggestion.kind === 'Command' ? `Applied suggestion: ${suggestion.title}` : `Inspecting ${suggestion.deviceName}`);
    } catch (err) {
      flash(getApiErrorMessage(err, 'Suggestion is no longer available'), 'error');
    }
  };

  const dismiss = async (suggestion: ActionSuggestion) => {
    try {
      await api.dismissSuggestion(suggestion.id);
      flash(`Dismissed “${suggestion.title}” for 10 minutes.`);
    } catch (err) {
      flash(getApiErrorMessage(err, 'Suggestion is no longer available'), 'error');
    }
  };

  const highlighted = useMemo(
    () => new Set((store.recommendations?.suggestions ?? []).map(s => s.macAddress)),
    [store.recommendations]
  );

  return (
    <div className="mt-4 space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-zinc-800/80 pb-2">
        <div className="text-xs font-mono font-bold uppercase tracking-wider text-zinc-300 flex items-center gap-2">
          <Radio size={14} className="text-cyan-400" />
          Real-Time Command Stream &amp; History
        </div>
        {feedback && (
          <div role="status" className={`text-[11px] font-mono px-2 py-1 rounded-sm border ${feedback.tone === 'ok' ? 'border-emerald-800 text-emerald-300 bg-emerald-950/40' : 'border-rose-800 text-rose-300 bg-rose-950/40'}`}>
            {feedback.text}
          </div>
        )}
      </div>

      <div className="grid grid-cols-1 xl:grid-cols-12 gap-4 items-start">
        <div className="xl:col-span-8 space-y-4">
          <DeviceControlPanel
            devices={store.devices}
            history={store.history}
            query={query}
            category={category}
            alertState={alertState}
            selectedMac={selectedMac}
            highlightedMacs={highlighted}
            onQueryChange={q => updateParam('q', q)}
            onCategoryChange={c => updateParam('category', c, 'all')}
            onAlertStateChange={a => updateParam('alert', a, 'all')}
            onSelect={mac => updateParam('device', mac)}
            onCommand={issueCommand}
          />
          <TimelineChart device={selectedDevice} windowMinutes={windowMinutes} onWindowChange={w => updateParam('window', String(w), '5')} />
          <PipelineMonitor pipeline={store.pipeline} deviceMap={store.deviceMap} />
        </div>

        <div className="xl:col-span-4 space-y-4">
          <RecommendationPanel
            recommendations={store.recommendations}
            onAccept={accept}
            onDismiss={dismiss}
            onFocusDevice={mac => updateParam('device', mac)}
          />
          <CommandHistoryPanel commands={store.commands} onUndo={undo} undoing={undoing} />
        </div>
      </div>
    </div>
  );
};

export default CommandStreamPage;
