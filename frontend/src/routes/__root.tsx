import { createRootRoute, Outlet } from '@tanstack/react-router';

/**
 * Ruta raíz: el layout que envuelve TODAS las rutas (públicas y protegidas).
 * En este PR solo renderiza un <c>Outlet</c>; el shell de la app autenticada
 * (header con EmpresaSelector + logout) vive en cada ruta protegida porque
 * NO debe aparecer en la pantalla de login.
 */
export const Route = createRootRoute({
  component: RootComponent,
});

function RootComponent() {
  return <Outlet />;
}
