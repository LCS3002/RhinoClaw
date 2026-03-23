import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../rhino_plugin/www',
    emptyOutDir: true,
  },
})
