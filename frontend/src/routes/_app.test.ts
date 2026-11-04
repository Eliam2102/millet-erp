import type {} from '@/router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { toast } from 'sonner';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { Route } from '@/routes/_app';

vi.mock('sonner', () => ({ toast: { info: vi.fn() } }));

const anterior = useAuthStore.getState();
afterEach(() => {
  useAuthStore.setState({ status: anterior.status, permisos: anterior.permisos });
  vi.mocked(toast.info).mockClear();
});

// F-FIN-01: la URL sin permiso regresa a Inicio, pero avisando.
describe('guard general de _app', () => {
  const guard = (pathname: string) =>
    (Route.options.beforeLoad as (a: { location: { pathname: string } }) => void)({ location: { pathname } });

  it('sin permiso avisa y redirige a inicio', () => {
    useAuthStore.setState({ status: 'authenticated', permisos: [P.CompartidoCatalogosAdministrar] });
    expect(() => guard('/tesoreria/reportes/flujo-efectivo')).toThrow(
      expect.objectContaining({ options: expect.objectContaining({ to: '/' }) }),
    );
    expect(toast.info).toHaveBeenCalledWith('No tienes acceso a ese módulo.', expect.anything());
  });

  it('con permiso entra sin aviso', () => {
    useAuthStore.setState({ status: 'authenticated', permisos: Object.values(P) });
    expect(() => guard('/tesoreria/reportes/flujo-efectivo')).not.toThrow();
    expect(toast.info).not.toHaveBeenCalled();
  });
});
