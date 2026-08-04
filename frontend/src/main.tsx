import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClientProvider } from '@tanstack/react-query';
import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { createRouter, RouterProvider } from '@tanstack/react-router';
import { queryClient } from '@/lib/query-client';
import { AuthBootstrap } from '@/lib/auth/AuthBootstrap';
import { routeTree } from '@/routeTree.gen';
import '@/index.css';

// Router instance. El plugin de Vite genera routeTree.gen.ts watcheando
// src/routes/*.tsx; cada archivo es una ruta vía createFileRoute.
const router = createRouter({
  routeTree,
  defaultPreload: 'intent',
});

// Type registration para autocompletado/validación en useNavigate, Link, etc.
declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AuthBootstrap>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
        <ReactQueryDevtools initialIsOpen={false} />
      </QueryClientProvider>
    </AuthBootstrap>
  </StrictMode>,
);
