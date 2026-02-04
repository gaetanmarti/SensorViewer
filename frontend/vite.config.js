import { defineConfig, loadEnv } from 'vite'
import { svelte } from '@sveltejs/vite-plugin-svelte'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  
  return {
    base: mode === 'production' ? '/webapp/' : '/',
    plugins: [svelte()],
    optimizeDeps: {
      exclude: ['fsevents'],
      force: true
    },
    build: {
      outDir: 'dist',
      assetsDir: 'assets',
      sourcemap: false,
      minify: 'esbuild',
      rollupOptions: {
        output: {
          manualChunks: {
            vendor: ['svelte']
          }
        }
      }
    },
    server: {
      proxy: {
        '/api': {
          target: 'http://192.168.4.50:8080',
          changeOrigin: true,
          secure: false
        }
      },
      fs: {
        strict: false
      }
    }
  }
})
