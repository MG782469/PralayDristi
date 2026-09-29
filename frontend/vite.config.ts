import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const api = process.env.PD_API ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': api,
      '/wms': api,
      '/hubs': { target: api, ws: true },
    },
  },
  build: { chunkSizeWarningLimit: 2500 },
})
