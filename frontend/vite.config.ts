import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Local .NET backend (dotnet run --launch-profile http)
const apiTarget = process.env.SMARTX_API_TARGET || 'http://localhost:5240'
const apiProxy = {
  '/api': {
    target: apiTarget,
    changeOrigin: true,
  },
  // SignalR negotiate + WebSocket upgrade
  '/hubs': {
    target: apiTarget,
    changeOrigin: true,
    ws: true,
  },
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    tailwindcss(),
    react()
  ],
  server: {
    proxy: apiProxy,
  },
  preview: {
    proxy: apiProxy,
  },
})
