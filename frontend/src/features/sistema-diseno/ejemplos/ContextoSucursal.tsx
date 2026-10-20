import { useMemo } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useAuthStore } from '@/lib/auth/auth-store';
import { ContextoSucursal } from '@/components/erp/display/ContextoSucursal';

export default function Ejemplo() {
  const empresa = useAuthStore((s) => s.currentEmpresaId);
  const sucursal = useAuthStore((s) => s.currentSucursalId);
  const client = useMemo(() => {
    const c = new QueryClient({
      defaultOptions: {
        queries: {
          retry: false,
          refetchOnMount: false,
          refetchOnWindowFocus: false,
          refetchOnReconnect: false,
        },
      },
    });
    c.setQueryData(
      ['auth', 'sucursales', empresa],
      [{ id: sucursal, nombre: '[SUCURSAL FICTICIA]' }],
    );
    return c;
  }, [empresa, sucursal]);
  return (
    <QueryClientProvider client={client}>
      <ContextoSucursal />
      <p>
        Vista aislada: si hay sucursal activa, su nombre se sustituye por [SUCURSAL FICTICIA]. Sin
        selección muestra todas.
      </p>
    </QueryClientProvider>
  );
}
