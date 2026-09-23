import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClientProvider } from '@tanstack/react-query';
import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { RouterProvider } from '@tanstack/react-router';
import { queryClient } from '@/lib/query-client';
import { AuthBootstrap } from '@/lib/auth/AuthBootstrap';
import { router } from '@/router';
import '@/index.css';

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
