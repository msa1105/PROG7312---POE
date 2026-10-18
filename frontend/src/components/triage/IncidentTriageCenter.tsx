import { useState, useEffect, useRef } from 'react';
import { 
  AlertTriangle, 
  Award, 
  CheckCircle2, 
  Clock, 
  Plus, 
  ShieldAlert, 
  UserCheck, 
  Zap, 
  Sparkles,
  Filter,
  Timer
} from 'lucide-react';
import type { IncidentIssue, OperatorEngagementStats } from '../../types/telemetry';
import { api, getApiErrorMessage } from '../../services/api';
import { formatBenchmark } from '../../utils/telemetry';

const XP_BY_SEVERITY: Record<number, number> = { 0: 25, 1: 50, 2: 100, 3: 150 };

type IncidentCenterProps = {
  initialReportMac?: string;
  openReportOnMount?: boolean;
  knownMacs?: string[];
};

export const IncidentTriageCenter = ({ initialReportMac, openReportOnMount = false, knownMacs = [] }: IncidentCenterProps) => {
  const [issues, setIssues] = useState<IncidentIssue[]>([]);
  const [stats, setStats] = useState<OperatorEngagementStats | null>(null);
  const [filter, setFilter] = useState<'all' | 'active' | 'resolved'>('all');
  const [isReportingOpen, setIsReportingOpen] = useState(openReportOnMount);
  const [newTitle, setNewTitle] = useState('');
  const [newMac, setNewMac] = useState(initialReportMac || '24:6F:28:9A:12:03');
  const [reportError, setReportError] = useState<string | null>(null);
  const [newDesc, setNewDesc] = useState('');
  const [newSeverity, setNewSeverity] = useState<number>(2); // High
  const [toast, setToast] = useState<{ text: string; type: 'success' | 'error' } | null>(null);
  // Optimistic updates in flight: background refreshes must not overwrite them until the API answers
  const inFlight = useRef(0);

  const showToast = (text: string, type: 'success' | 'error' = 'success') => {
    setToast({ text, type });
    setTimeout(() => setToast(null), type === 'error' ? 6000 : 3500);
  };

  const fetchIssuesAndStats = async () => {
    try {
      const [issuesData, statsData] = await Promise.all([
        api.getIssues(),
        api.getEngagementStats()
      ]);
      if (inFlight.current > 0) return;
      setIssues(issuesData);
      setStats(statsData);
    } catch (err) {
      console.error('Failed to load incident engagement data:', err);
    }
  };

  useEffect(() => {
    fetchIssuesAndStats();
    const interval = setInterval(fetchIssuesAndStats, 4000);
    return () => clearInterval(interval);
  }, []);

  // Optimistic: the incident appears in the list at once; it is replaced by the server copy or removed on rejection
  const handleReport = async (e: React.FormEvent) => {
    e.preventDefault();
    setReportError(null);

    const draft = { macAddress: newMac.trim().toUpperCase(), title: newTitle, description: newDesc, severity: newSeverity };
    const expectedXp = XP_BY_SEVERITY[newSeverity] ?? 25;
    const tempId = `PENDING-${Date.now()}`;
    const placeholder: IncidentIssue = {
      id: tempId, ...draft, status: 0, reportedBy: 'Operator-Gateway', reportedAt: new Date().toISOString(), xpAwarded: expectedXp
    };

    inFlight.current++;
    setIssues(prev => [placeholder, ...prev]);
    setIsReportingOpen(false);
    setNewTitle('');
    setNewDesc('');
    showToast(`Incident logged! +${expectedXp} XP awarded to Operator profile`);

    try {
      const created = await api.reportIssue({ ...draft, reportedBy: 'Operator-Gateway' });
      setIssues(prev => prev.map(i => (i.id === tempId ? created : i)));
    } catch (err: unknown) {
      setIssues(prev => prev.filter(i => i.id !== tempId));
      // Restore the form so the operator can correct the rejected report
      setNewTitle(draft.title);
      setNewDesc(draft.description);
      setNewMac(draft.macAddress);
      setNewSeverity(draft.severity);
      setReportError(getApiErrorMessage(err, 'Failed to report issue.'));
      setIsReportingOpen(true);
      showToast('Incident report was rejected by the gateway.', 'error');
    } finally {
      inFlight.current--;
    }
    await fetchIssuesAndStats();
  };

  // Optimistic: the status changes immediately and is reverted if the gateway rejects it
  const handleStatusChange = async (id: string, newStatus: number, notes?: string) => {
    const snapshot = issues;
    const now = new Date().toISOString();
    inFlight.current++;
    setIssues(prev => prev.map(i => i.id !== id ? i : {
      ...i,
      status: newStatus,
      triagedAt: newStatus >= 1 ? (i.triagedAt ?? now) : null,
      resolvedAt: newStatus === 2 ? now : null,
      resolutionNotes: newStatus === 2 && notes ? notes : i.resolutionNotes,
    }));
    showToast(newStatus === 2 ? 'Incident Resolved! +150 Operator XP bonus!' : 'Triage Status Updated.');

    try {
      await api.updateIssueStatus(id, newStatus, notes);
    } catch (err) {
      setIssues(snapshot);
      showToast(`Status change reverted: ${getApiErrorMessage(err, 'request failed')}`, 'error');
    } finally {
      inFlight.current--;
    }
    await fetchIssuesAndStats();
  };

  const filteredIssues = issues.filter(issue => {
    const s = typeof issue.status === 'number' ? issue.status : 0;
    if (filter === 'active') return s === 0 || s === 1;
    if (filter === 'resolved') return s === 2;
    return true;
  });

  return (
    <div className="bg-zinc-900/60 border border-zinc-800 rounded-sm p-5 font-mono text-xs text-zinc-300">
      {/* Toast Feedback */}
      {toast && (
        <div className={`mb-4 p-3 rounded flex items-center gap-2 animate-in fade-in duration-200 border ${toast.type === 'error' ? 'bg-rose-950 border-rose-600 text-rose-200' : 'bg-emerald-950 border-emerald-500 text-emerald-300'}`}>
          <Sparkles size={16} className={`shrink-0 ${toast.type === 'error' ? 'text-rose-400' : 'text-emerald-400'}`} />
          <span className="font-bold">{toast.text}</span>
        </div>
      )}

      {/* Operator Engagement Scoreboard */}
      <div className="bg-zinc-950 border border-zinc-800/80 rounded p-4 mb-5 flex flex-wrap items-center justify-between gap-4">
        <div className="flex items-center gap-4">
          <div className="w-12 h-12 rounded bg-purple-950/60 border border-purple-500/40 flex items-center justify-center text-purple-400">
            <Award size={24} />
          </div>
          <div>
            <div className="text-[10px] text-zinc-500 uppercase tracking-widest flex items-center gap-1.5">
              <UserCheck size={12} className="text-purple-400" />
              Dynamic Engagement Strategy • Incident Triage Command
            </div>
            <div className="text-base font-bold text-zinc-100 uppercase flex items-center gap-2">
              Rank: <span className="text-purple-400">{stats?.operatorRank || 'Operations Apprentice'}</span>
              <span className="text-xs bg-purple-900/40 text-purple-300 px-2 py-0.5 rounded border border-purple-800">
                Lvl {stats?.operatorLevel || 1}
              </span>
            </div>
          </div>
        </div>

        <div className="flex items-center gap-6 font-mono text-xs">
          <div className="text-center">
            <div className="text-[10px] text-zinc-500 uppercase">Operator XP</div>
            <div className="text-lg font-bold text-yellow-400 flex items-center gap-1">
              <Zap size={14} className="fill-yellow-400 text-yellow-400" />
              {stats?.operatorXp || 0} XP
            </div>
          </div>
          <div className="h-8 w-px bg-zinc-800"></div>
          <div className="text-center">
            <div className="text-[10px] text-zinc-500 uppercase">Resolution Rate</div>
            <div className="text-lg font-bold text-emerald-400">
              {stats?.resolutionRatePercent || 0}%
            </div>
          </div>
          <div className="h-8 w-px bg-zinc-800"></div>
          <div className="text-center" title="Mean-Time-To-Detect (alert raised → operator acknowledgement) / Mean-Time-To-Triage (report → claimed)">
            <div className="text-[10px] text-zinc-500 uppercase flex items-center justify-center gap-1"><Timer size={10} /> MTTD / MTTT</div>
            <div className="text-lg font-bold text-zinc-200">
              {formatBenchmark(stats?.meanTimeToDetectSeconds)} / {formatBenchmark(stats?.meanTimeToTriageSeconds)}
            </div>
          </div>
          <div className="h-8 w-px bg-zinc-800"></div>
          <div className="text-center">
            <div className="text-[10px] text-zinc-500 uppercase">Queue Depth</div>
            <div className="text-lg font-bold text-cyan-400">
              {stats?.totalInTriage || 0} Triage / {stats?.totalReported || 0} Total
            </div>
          </div>
        </div>
      </div>

      {/* Badges Ribbon */}
      <div className="flex flex-wrap items-center gap-2 mb-5">
        <span className="text-[10px] text-zinc-500 uppercase mr-1">Earned Badges:</span>
        {stats?.badges && stats.badges.map((b, i) => (
          <span key={i} className="inline-flex items-center gap-1 px-2.5 py-1 bg-zinc-950 border border-zinc-800 text-zinc-300 text-[11px] rounded-full">
            <Award size={11} className="text-yellow-400" />
            {b}
          </span>
        ))}
      </div>

      {/* Controls: Filter & Report Button */}
      <div className="flex flex-wrap items-center justify-between gap-3 mb-4 pb-3 border-b border-zinc-800/80">
        <div className="flex items-center gap-2">
          <Filter size={14} className="text-zinc-500" />
          <div className="flex bg-zinc-950 p-0.5 rounded border border-zinc-800">
            <button
              onClick={() => setFilter('all')}
              className={`px-3 py-1 rounded text-xs transition-colors ${filter === 'all' ? 'bg-zinc-800 text-zinc-100 font-bold' : 'text-zinc-500 hover:text-zinc-300'}`}
            >
              All ({issues.length})
            </button>
            <button
              onClick={() => setFilter('active')}
              className={`px-3 py-1 rounded text-xs transition-colors ${filter === 'active' ? 'bg-zinc-800 text-zinc-100 font-bold' : 'text-zinc-500 hover:text-zinc-300'}`}
            >
              Active Triage ({issues.filter(i => (typeof i.status === 'number' ? i.status : 0) !== 2).length})
            </button>
            <button
              onClick={() => setFilter('resolved')}
              className={`px-3 py-1 rounded text-xs transition-colors ${filter === 'resolved' ? 'bg-zinc-800 text-zinc-100 font-bold' : 'text-zinc-500 hover:text-zinc-300'}`}
            >
              Resolved ({issues.filter(i => (typeof i.status === 'number' ? i.status : 0) === 2).length})
            </button>
          </div>
        </div>

        <button
          onClick={() => setIsReportingOpen(true)}
          className="bg-purple-600 hover:bg-purple-500 text-white px-3 py-1.5 rounded flex items-center gap-1.5 font-bold transition-all shadow-sm"
        >
          <Plus size={14} /> Report Sensor Incident
        </button>
      </div>

      {/* User-Reported Issue List (Directly backed by List<IncidentIssue> on .NET API) */}
      <div className="space-y-2.5 max-h-[380px] overflow-y-auto pr-1">
        {filteredIssues.length === 0 ? (
          <div className="text-center py-8 text-zinc-500">
            No incidents found in this filter category.
          </div>
        ) : (
          filteredIssues.map((issue) => {
            const statusNum = typeof issue.status === 'number' ? issue.status : 0;
            const severityNum = typeof issue.severity === 'number' ? issue.severity : 1;

            return (
              <div 
                key={issue.id} 
                className={`p-3.5 rounded border transition-all ${
                  statusNum === 2 
                    ? 'bg-zinc-950/40 border-zinc-800/60 opacity-70' 
                    : statusNum === 1 
                      ? 'bg-amber-950/20 border-amber-800/60' 
                      : 'bg-zinc-950 border-zinc-800 hover:border-zinc-700'
                }`}
              >
                <div className="flex items-start justify-between gap-2 mb-1.5">
                  <div className="flex items-center gap-2">
                    <span className="font-bold text-zinc-200">{issue.id.startsWith('PENDING-') ? 'Logging…' : issue.id}</span>
                    <span className="text-[10px] text-cyan-400 bg-cyan-950/50 px-1.5 py-0.5 rounded border border-cyan-800">
                      {issue.macAddress}
                    </span>
                    <span className={`text-[10px] px-1.5 py-0.5 rounded uppercase font-bold border ${
                      severityNum === 3 ? 'bg-rose-950 text-rose-400 border-rose-800' :
                      severityNum === 2 ? 'bg-amber-950 text-amber-400 border-amber-800' :
                      severityNum === 1 ? 'bg-yellow-950 text-yellow-400 border-yellow-800' :
                      'bg-zinc-800 text-zinc-400 border-zinc-700'
                    }`}>
                      {severityNum === 3 ? 'Critical' : severityNum === 2 ? 'High' : severityNum === 1 ? 'Medium' : 'Low'}
                    </span>
                  </div>

                  <div className="flex items-center gap-2">
                    {statusNum === 0 && (
                      <span className="inline-flex items-center gap-1 text-[10px] bg-blue-950 text-blue-400 px-2 py-0.5 rounded border border-blue-800">
                        <Clock size={10} /> Reported
                      </span>
                    )}
                    {statusNum === 1 && (
                      <span className="inline-flex items-center gap-1 text-[10px] bg-amber-950 text-amber-400 px-2 py-0.5 rounded border border-amber-800 animate-pulse">
                        <AlertTriangle size={10} /> In Triage
                      </span>
                    )}
                    {statusNum === 2 && (
                      <span className="inline-flex items-center gap-1 text-[10px] bg-emerald-950 text-emerald-400 px-2 py-0.5 rounded border border-emerald-800">
                        <CheckCircle2 size={10} /> Resolved (+150 XP)
                      </span>
                    )}
                  </div>
                </div>

                <div className="text-zinc-200 font-bold text-xs mb-1">
                  {issue.title}
                </div>
                <div className="text-zinc-400 text-[11px] leading-relaxed mb-2">
                  {issue.description}
                </div>

                {issue.resolutionNotes && (
                  <div className="bg-emerald-950/30 border border-emerald-800/40 p-2 rounded text-[11px] text-emerald-300 mb-2">
                    <strong>Resolution Log:</strong> {issue.resolutionNotes}
                  </div>
                )}

                <div className="flex items-center justify-between text-[10px] text-zinc-500 pt-2 border-t border-zinc-800/60">
                  <span>Reported by {issue.reportedBy} • {new Date(issue.reportedAt).toLocaleTimeString()}</span>
                  
                  <div className="flex items-center gap-2">
                    {statusNum === 0 && (
                      <button
                        onClick={() => handleStatusChange(issue.id, 1)}
                        className="text-amber-400 hover:text-amber-300 font-bold uppercase underline"
                      >
                        Claim &amp; Triage →
                      </button>
                    )}
                    {statusNum === 1 && (
                      <button
                        onClick={() => {
                          const notes = prompt('Enter resolution notes for audit trail:', 'Calibrated parameters and validated stability.');
                          if (notes !== null) {
                            handleStatusChange(issue.id, 2, notes);
                          }
                        }}
                        className="text-emerald-400 hover:text-emerald-300 font-bold uppercase underline flex items-center gap-1"
                      >
                        <CheckCircle2 size={12} /> Mark Resolved
                      </button>
                    )}
                  </div>
                </div>
              </div>
            );
          })
        )}
      </div>

      {/* Report Incident Modal */}
      {isReportingOpen && (
        <div className="fixed inset-0 z-[120] flex items-center justify-center p-4 bg-zinc-950/80 backdrop-blur-sm">
          <div className="bg-zinc-900 border border-zinc-700 rounded p-6 max-w-md w-full shadow-2xl font-mono text-xs">
            <div className="flex items-center justify-between mb-4 border-b border-zinc-800 pb-2">
              <div className="text-zinc-100 font-bold uppercase tracking-widest flex items-center gap-2">
                <ShieldAlert size={16} className="text-purple-400" />
                Report Sensor Incident
              </div>
              <button onClick={() => setIsReportingOpen(false)} className="text-zinc-500 hover:text-zinc-300">✕</button>
            </div>

            <form onSubmit={handleReport} className="space-y-3">
              <div>
                <label className="block text-zinc-400 mb-1">Target Sensor MAC</label>
                <input
                  required
                  list="incident-known-macs"
                  value={newMac}
                  onChange={e => setNewMac(e.target.value)}
                  className="w-full bg-zinc-950 border border-zinc-800 p-2 text-zinc-200 uppercase rounded"
                  placeholder="24:6F:28:9A:12:03"
                />
                <datalist id="incident-known-macs">
                  {knownMacs.map(mac => <option key={mac} value={mac} />)}
                </datalist>
              </div>

              <div>
                <label className="block text-zinc-400 mb-1">Incident Title / Symptom</label>
                <input
                  required
                  value={newTitle}
                  onChange={e => setNewTitle(e.target.value)}
                  className="w-full bg-zinc-950 border border-zinc-800 p-2 text-zinc-200 rounded"
                  placeholder="e.g. Transient Outlier / Voltage Harmonic"
                />
              </div>

              <div>
                <label className="block text-zinc-400 mb-1">Severity Level</label>
                <select
                  value={newSeverity}
                  onChange={e => setNewSeverity(parseInt(e.target.value))}
                  className="w-full bg-zinc-950 border border-zinc-800 p-2 text-zinc-200 rounded"
                >
                  <option value={0}>0 - Low (25 XP)</option>
                  <option value={1}>1 - Medium (50 XP)</option>
                  <option value={2}>2 - High (100 XP)</option>
                  <option value={3}>3 - Critical (+4σ Outlier) (150 XP)</option>
                </select>
              </div>

              <div>
                <label className="block text-zinc-400 mb-1">Detailed Technical Log</label>
                <textarea
                  required
                  rows={3}
                  value={newDesc}
                  onChange={e => setNewDesc(e.target.value)}
                  className="w-full bg-zinc-950 border border-zinc-800 p-2 text-zinc-200 rounded"
                  placeholder="Describe observed telemetry drift, hardware state, or sub-zone anomaly..."
                />
              </div>

              {reportError && (
                <div className="p-2 bg-rose-950/80 border border-rose-800 text-rose-300 rounded text-[11px]">{reportError}</div>
              )}

              <div className="flex gap-2 pt-2">
                <button
                  type="button"
                  onClick={() => setIsReportingOpen(false)}
                  className="flex-1 bg-zinc-800 hover:bg-zinc-700 text-zinc-300 py-2 rounded"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="flex-1 bg-purple-600 hover:bg-purple-500 text-white font-bold py-2 rounded"
                >
                  Commit to Issue List
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
