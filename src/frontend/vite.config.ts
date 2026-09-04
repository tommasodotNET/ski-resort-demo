import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: parseInt(process.env.PORT ?? '5173'),
    host: true,
    proxy: {
      '/api': {
        target: process.env.DATAGENERATOR_HTTPS || process.env.DATAGENERATOR_HTTP,
        changeOrigin: true,
        secure: false,
      },
      '/responses/a2a': {
        target: process.env.SKIADVISORA2A_HTTPS || process.env.SKIADVISORA2A_HTTP,
        changeOrigin: true,
        secure: false,
        rewrite: (path) => path.replace(/^\/responses\/a2a/, '/responses'),
      },
      '/responses/skill': {
        target: process.env.SKIADVISORSKILL_HTTPS || process.env.SKIADVISORSKILL_HTTP,
        changeOrigin: true,
        secure: false,
        rewrite: (path) => path.replace(/^\/responses\/skill/, '/responses'),
      },
      '/ws/voice/a2a': {
        target: process.env.VOICEADVISORA2A_HTTPS || process.env.VOICEADVISORA2A_HTTP,
        changeOrigin: true,
        secure: false,
        ws: true,
        rewrite: (path) => path.replace(/^\/ws\/voice\/a2a/, '/ws/voice'),
      },
      '/ws/voice/skill': {
        target: process.env.VOICEADVISORSKILL_HTTPS || process.env.VOICEADVISORSKILL_HTTP,
        changeOrigin: true,
        secure: false,
        ws: true,
        rewrite: (path) => path.replace(/^\/ws\/voice\/skill/, '/ws/voice'),
      },
    },
  },
})
