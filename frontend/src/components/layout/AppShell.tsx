import { useState } from 'react';
import { Outlet, useNavigate } from 'react-router-dom';
import { AlertTriangle, X } from 'lucide-react';
import { Header } from './Header';
import { PillarNavigation } from './PillarNavigation';
import { DiagnosticDrawer } from '../triage/DiagnosticDrawer';
import { IncidentTriageCenter } from '../triage/IncidentTriageCenter';
import { RegisterDeviceModal } from '../modals/RegisterDeviceModal';
import { api } from '../../services/api';
import { useGatewayStore } from '../../state/gatewayContext';

const SPIKE_TARGET_MAC = "24:6F:28:9A:12:03";
const HEARTBEAT_TARGET_MAC = "24:6F:28:9A:12:01";

/**
 * Persistent layout for every module route. The header, pillar navigation, triage deck, diagnostic drawer and
 * alert toasts live here, so switching between modules only swaps the <Outlet /> content.
 * Reference: React Router - Layout routes / Outlet - https://reactrouter.com/start/declarative/routing
 */
export const AppShell = () => {
  const navigate = useNavigate();
  const store = useGatewayStore();
  const [isRegisterOpen, setIsRegisterOpen] = useState(false);

  const handleBurst = async () => {
    const result = await api.simulationBurst();
    await store.refreshDevices();
    return `${result.processedCount.toLocaleString()} packets ingested in ${result.elapsedMs}ms via Jagged Buffer`;
  };

  const handleInjectSpike = async () => {
    await api.simulationSpike(SPIKE_TARGET_MAC, 4.5);
    return `+4.5σ spike scheduled on ${SPIKE_TARGET_MAC}`;
  };

  const handleDropHeartbeat = async () => {
    await api.simulationDropHeartbeat(HEARTBEAT_TARGET_MAC);
    return `Heartbeat dropped on ${HEARTBEAT_TARGET_MAC} (lease expires after 2 missed windows)`;
  };

  const handleReset = async () => {
    await api.simulationReset();
    store.clearHistory();
    await Promise.all([store.refreshDevices(), store.refreshCommands(), store.refreshRecommendations(), store.refreshPipeline()]);
    return 'Simulation baseline reset and fleet re-seeded';
  };

  const selectedDevice = store.selectedMac ? store.deviceMap[store.selectedMac] ?? null : null;
  const activeAlertCount = store.devices.filter(d => d.activeAlerts.length > 0).length;

  return (
    <>
      <Header
        onReturnToLanding={() => navigate('/')}
        onRegisterClick={() => setIsRegisterOpen(true)}
        onBurst={handleBurst}
        onInjectSpike={handleInjectSpike}
        onDropHeartbeat={handleDropHeartbeat}
        onReset={handleReset}
        onToggleTriage={() => store.setShowTriageDeck(!store.showTriageDeck)}
        activeTriageCount={activeAlertCount}
        isBackendOnline={store.isBackendOnline}
        connection={store.connection}
      />

      <main className="flex-1 p-4 max-w-[1600px] w-full mx-auto">
        <PillarNavigation />

        {/* Dynamic Incident Triage & Engagement Deck */}
        {store.showTriageDeck && (
          <div className="mt-4 mb-6 animate-in slide-in-from-top duration-300">
            <IncidentTriageCenter
              key={store.reportRequest?.id ?? 'deck'}
              initialReportMac={store.reportRequest?.mac}
              openReportOnMount={store.reportRequest !== null}
              knownMacs={store.devices.map(d => d.device.macAddress)}
            />
          </div>
        )}

        <Outlet />
      </main>

      {/* Real-time critical alert toasts (pushed over SignalR) */}
      {store.toasts.length > 0 && (
        <div className="fixed bottom-4 right-4 z-[130] flex flex-col gap-2 w-80 font-mono text-xs">
          {store.toasts.map(({ id, alert, deviceName }) => (
            <div
              key={id}
              role="alert"
              className={`flex items-start gap-2 p-3 rounded-sm border shadow-xl ${alert.severity === 'Critical' ? 'bg-rose-950 border-rose-600 text-rose-200' : 'bg-amber-950 border-amber-700 text-amber-200'}`}
            >
              <AlertTriangle size={14} className="shrink-0 mt-0.5" />
              <button className="flex-1 text-left cursor-pointer" onClick={() => store.setSelectedMac(alert.macAddress)}>
                <div className="font-bold uppercase">{alert.severity} • {deviceName}</div>
                <div className="text-[11px] opacity-90">{alert.message}</div>
              </button>
              <button onClick={() => store.dismissToast(id)} className="opacity-70 hover:opacity-100 cursor-pointer" aria-label="Dismiss alert">
                <X size={12} />
              </button>
            </div>
          ))}
        </div>
      )}

      {selectedDevice && (
        <DiagnosticDrawer
          deviceData={selectedDevice}
          allDevices={store.devices}
          fetchedAt={store.receivedAt[selectedDevice.device.macAddress] ?? 0}
          onClose={() => store.setSelectedMac(null)}
          onRefresh={store.refreshDevices}
          onReportIncident={store.requestIncidentReport}
          onSelectDevice={store.setSelectedMac}
        />
      )}

      {isRegisterOpen && (
        <RegisterDeviceModal
          existingMacs={store.devices.map(d => d.device.macAddress)}
          onClose={() => setIsRegisterOpen(false)}
          onSuccess={store.refreshDevices}
        />
      )}
    </>
  );
};
