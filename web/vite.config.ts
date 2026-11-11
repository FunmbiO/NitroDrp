/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // `npm run dev` talks to the API started with `dotnet run` (see README)
    proxy: { '/api': 'http://localhost:8080' },
  },
  test: { environment: 'node' },
})
