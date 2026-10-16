import type {} from '@/router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';

vi.mock('@/features/comercio-exterior/pages/ExpedientesPage', () => ({
  ExpedientesPage: () => null,
}));
vi.mock('@/features/comercio-exterior/pages/ExpedienteDetallePage', () => ({
  ExpedienteDetallePage: () => null,
}));

import { Route as listado } from '@/routes/_app/comercio-exterior/expedientes/index';
import { Route as detalle } from '@/routes/_app/comercio-exterior/expedientes/$id';

const permisosAnteriores = useAuthStore.getState().permisos;
afterEach(() => useAuthStore.setState({ permisos: permisosAnteriores }));

describe.each([
  ['listado', listado],
  ['detalle', detalle],
] as const)('guarda CE de %s', (_, route) => {
  it('redirige a inicio sin permiso de lectura', () => {
    useAuthStore.setState({ permisos: [P.ComercioExteriorExpedientesCrear] });
    const guard = route.options.beforeLoad as () => void;
    expect(guard).toBeTypeOf('function');
    try {
      guard();
      expect.fail('Debe redirigir a inicio');
    } catch (redirect) {
      expect(redirect).toMatchObject({ options: { to: '/' } });
    }
  });
  it('permite entrar con permiso de lectura', () => {
    useAuthStore.setState({ permisos: [P.ComercioExteriorExpedientesLeer] });
    const guard = route.options.beforeLoad as () => void;
    expect(guard).not.toThrow();
  });
});

import { Route as relacionIva } from '@/routes/_app/comercio-exterior/relacion-iva';
it('relación IVA usa su permiso propio', () => {
  const guard = relacionIva.options.beforeLoad as () => void;
  useAuthStore.setState({ permisos: [P.ComercioExteriorExpedientesLeer] });
  expect(guard).toThrow();
  useAuthStore.setState({ permisos: [P.ComercioExteriorRelacionIvaLeer] });
  expect(guard).not.toThrow();
});
