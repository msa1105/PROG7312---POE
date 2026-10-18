import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import './index.css'
import App from './App.tsx'
import { GatewayStoreProvider } from './state/GatewayStore'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <GatewayStoreProvider>
        <App />
      </GatewayStoreProvider>
    </BrowserRouter>
  </StrictMode>,
)
