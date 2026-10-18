import type { DeviceView } from '../types/telemetry';

export type AlertStateFilter = 'all' | 'alerting' | 'critical' | 'offline' | 'nominal';
export const ALERT_FILTERS: AlertStateFilter[] = ['all', 'alerting', 'critical', 'offline', 'nominal'];

/** Same semantics as the API's /api/devices/search endpoint, evaluated on the live store for instant feedback. */
export const matchesFilters = (view: DeviceView, query: string, category: string, alertState: AlertStateFilter): boolean => {
  const term = query.trim().toLowerCase();
  if (term && !view.device.macAddress.toLowerCase().includes(term) && !view.device.deploymentLocation.toLowerCase().includes(term)) return false;
  if (category !== 'all' && String(view.device.category) !== category) return false;

  const offline = !view.status.isOnline;
  const alerting = view.activeAlerts.length > 0 || offline;
  switch (alertState) {
    case 'alerting': return alerting;
    case 'critical': return view.activeAlerts.some(a => a.severity === 'Critical');
    case 'offline': return offline;
    case 'nominal': return !alerting;
    default: return true;
  }
};
