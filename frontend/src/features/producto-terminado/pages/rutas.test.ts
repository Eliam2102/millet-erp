import type {} from '@/router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';

vi.mock('@/features/producto-terminado/pages/PiezasPage', () => ({ PiezasPage: () => null }));

import { Route as piezas } from '@/routes/_app/producto-terminado/piezas';

const permisosAnteriores = useAuthStore.getState().permisos;
afterEach(() => useAuthStore.setState({ permisos: permisosAnteriores }));

describe('guarda de Piezas', () => {
  it('redirige a inicio sin piezas.ver', () => {
    useAuthStore.setState({ permisos: [P.ProductoTerminadoExcepcionesGestionar] });
    const guard = piezas.options.beforeLoad as () => void;
    try {
      guard();
      expect.fail('Debe redirigir a inicio');
    } catch (redirect) {
      expect(redirect).toMatchObject({ options: { to: '/' } });
    }
  });
  it('permite entrar con piezas.ver', () => {
    useAuthStore.setState({ permisos: [P.ProductoTerminadoPiezasVer] });
    expect(piezas.options.beforeLoad as () => void).not.toThrow();
  });
});
