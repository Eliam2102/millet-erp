import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { tanstackRouter } from '@tanstack/router-plugin/vite';
import path from 'node:path';

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    // El plugin de TanStack Router DEBE ir antes que react() para que el
    // codegen del routeTree.gen.ts esté listo cuando React procese las rutas.
    tanstackRouter({
      target: 'react',
      autoCodeSplitting: true,
      // Folder donde viven las rutas. Default 'src/routes' pero lo dejamos
      // explícito para grep-abilidad.
      routesDirectory: 'src/routes',
      generatedRouteTree: 'src/routeTree.gen.ts',
    }),
    react(),
    tailwindcss(),
  ],
  resolve: {
    alias: {
      '@': path.resolve(import.meta.dirname, './src'),
    },
  },
});
