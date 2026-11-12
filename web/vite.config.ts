/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // `npm run dev` talks to the API started with `dotnet run` (see docs/DEVELOPMENT.md)
    proxy: { '/api': 'http://localhost:8080', '/mock-storage': 'http://localhost:8080' },
    host: true,
    allowedHosts: true, // dev server only: lets a cloud/remote preview hostname load the page
  },
  test: { environment: 'node' },
})
