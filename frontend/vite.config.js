import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // During development, API requests go to the backend running in Docker
    proxy: {
      '/api': 'http://127.0.0.1:8080',
    },
  },
})
