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
        target: process.env.DATA_GENERATOR_HTTPS || process.env.DATA_GENERATOR_HTTP,
        changeOrigin: true,
        secure: false,
      },
      '/agenta2a': {
        target: process.env.ADVISOR_AGENT_DOTNET_HTTPS || process.env.ADVISOR_AGENT_DOTNET_HTTP,
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
