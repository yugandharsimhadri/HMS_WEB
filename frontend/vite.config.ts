import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    rollupOptions: {
      output: {
        // React and the router in their own chunk.
        //
        // They change when a dependency is upgraded, which is rarely; the
        // application's own code changes on every deployment. Kept together
        // in one file, every deployment invalidates both and a clinic
        // re-downloads the framework it already had. Split, the vendor chunk
        // keeps its filename hash across releases and comes from cache.
        // The function form, not the object form: Vite 8 builds with rolldown,
        // whose manualChunks only accepts a function.
        manualChunks: (id: string) =>
          id.includes('node_modules') ? 'vendor' : undefined,
      },
    },
  },
})
