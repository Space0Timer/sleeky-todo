import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: 'https://localhost:7238',
        changeOrigin: true,
        secure: false,
      },
      '/health': {
        target: 'https://localhost:7238',
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
