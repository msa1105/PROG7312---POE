import { lazy, Suspense } from 'react';
import { Navigate, Route, Routes, useNavigate } from 'react-router-dom';
import { GatewayLandingView } from './components/layout/GatewayLandingView';
import { AppShell } from './components/layout/AppShell';
import { useGatewayStore } from './state/gatewayContext';

// Route-level code splitting: each module is loaded on first visit, then kept in the browser cache.
// Reference: React, [s.a.]a. lazy. [online] Available at: <https://react.dev/reference/react/lazy> [Accessed 19 October 2026].
// Reference: React Router, [s.a.]a. Routing. [online] Available at: <https://reactrouter.com/start/declarative/routing> [Accessed 19 October 2026].
const IngestionPage = lazy(() => import('./pages/IngestionPage'));
const CommandStreamPage = lazy(() => import('./pages/CommandStreamPage'));
const TopologyLockedPage = lazy(() => import('./pages/TopologyLockedPage'));

const PageFallback = () => (
  <div className="mt-10 text-center text-xs font-mono text-zinc-500 animate-pulse">Loading module…</div>
);

const LandingRoute = () => {
  const navigate = useNavigate();
  const { devices } = useGatewayStore();
  return (
    <GatewayLandingView
      onEnterIngestion={() => navigate('/ingestion')}
      onEnterCommandStream={() => navigate('/commands')}
      registeredDeviceCount={devices.length}
    />
  );
};

function App() {
  return (
    <div className="min-h-screen bg-zinc-950 text-zinc-100 flex flex-col font-sans selection:bg-zinc-800">
      <Routes>
        <Route path="/" element={<LandingRoute />} />
        <Route element={<AppShell />}>
          <Route path="/ingestion" element={<Suspense fallback={<PageFallback />}><IngestionPage /></Suspense>} />
          <Route path="/commands" element={<Suspense fallback={<PageFallback />}><CommandStreamPage /></Suspense>} />
          <Route path="/topology" element={<Suspense fallback={<PageFallback />}><TopologyLockedPage /></Suspense>} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </div>
  );
}

export default App;
