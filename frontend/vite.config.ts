import { defineConfig, loadEnv, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { tanstackRouter } from '@tanstack/router-plugin/vite';
import fs from 'node:fs';
import path from 'node:path';
import { cabecerasSeguridad } from './src/lib/seguridad/csp';

/**
 * 86e3x819w: al compilar, suma la Content-Security-Policy (con el dominio de
 * la API de ese ambiente) y las demás cabeceras de seguridad a
 * dist/staticwebapp.config.json, que Static Web Apps aplica a cada respuesta.
 * En `vite dev` no aplica: allí las cabeceras no pasan por Static Web Apps.
 */
function cabecerasStaticWebApps(apiBaseUrl: string | undefined): Plugin {
  let salida = '';
  return {
    name: 'millet-cabeceras-static-web-apps',
    apply: 'build',
    configResolved(config) {
      salida = path.resolve(config.root, config.build.outDir, 'staticwebapp.config.json');
    },
    closeBundle() {
      if (!fs.existsSync(salida)) return;
      const swa = JSON.parse(fs.readFileSync(salida, 'utf8')) as { globalHeaders?: Record<string, string> };
      swa.globalHeaders = { ...swa.globalHeaders, ...cabecerasSeguridad(apiBaseUrl) };
      fs.writeFileSync(salida, `${JSON.stringify(swa, null, 2)}\n`);
    },
  };
}

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  // El entorno del proceso (CI / script de despliegue) gana a los .env.
  const env = { ...loadEnv(mode, import.meta.dirname, 'VITE_'), ...process.env };
  return {
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
      cabecerasStaticWebApps(env.VITE_API_BASE_URL),
    ],
    resolve: {
      alias: {
        '@': path.resolve(import.meta.dirname, './src'),
      },
    },
  };
});
