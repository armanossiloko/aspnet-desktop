import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';

export default defineConfig({
  plugins: [vue()],
  // Must match Desktop:DevServerUrl in appsettings.Development.json.
  server: { port: 5173, strictPort: true },
  build: { outDir: 'dist', emptyOutDir: true },
});
