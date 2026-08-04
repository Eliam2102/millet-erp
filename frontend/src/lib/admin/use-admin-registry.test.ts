import { afterEach, describe, expect, it } from 'vitest';
import { renderHook } from '@testing-library/react';
import {
  useAdminAccess,
  useAdminRegistry,
} from '@/lib/admin/use-admin-registry';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Tests del registry de cards del área de Administración. El registry
 * vive como una constante exportada (no se mockea); la única variable es
 * la lista de permisos del usuario en el auth store.
 *
 * <para>UF-Admin-PR1 incluye una sola card real (Compras). Estos tests
 * blindan el filtrado para que cuando llegue UF-Admin-PR2 con más cards,
 * el patrón siga siendo defensivo.</para>
 */
describe('useAdminRegistry / useAdminAccess', () => {
  afterEach(() => {
    useAuthStore.getState().clearSession();
  });

  it('retorna la card de Compras cuando el usuario tiene compras.configuracion.leer', () => {
    useAuthStore.setState({
      permisos: [PermisosCanonicos.ComprasConfiguracionLeer],
    });

    const { result } = renderHook(() => useAdminRegistry());

    expect(result.current).toHaveLength(1);
    expect(result.current[0]?.id).toBe('compras-configuracion');
    expect(result.current[0]?.modulo).toBe('compras');
    expect(result.current[0]?.displayMode).toBe('custom');
    expect(result.current[0]?.href).toBe('/compras/configuracion');
  });

  it('retorna lista vacía cuando el usuario no tiene ningún permiso registrado', () => {
    useAuthStore.setState({ permisos: [] });

    const { result } = renderHook(() => useAdminRegistry());

    expect(result.current).toHaveLength(0);
  });

  it('descarta cards cuyo permiso el usuario no tiene', () => {
    // Permiso de Compras requisiciones NO habilita la card de configuración.
    useAuthStore.setState({
      permisos: [PermisosCanonicos.ComprasRequisicionesLeer],
    });

    const { result } = renderHook(() => useAdminRegistry());

    expect(result.current).toHaveLength(0);
  });

  it('useAdminAccess es true si y solo si useAdminRegistry tiene al menos una card', () => {
    useAuthStore.setState({ permisos: [] });
    const sinPermisos = renderHook(() => useAdminAccess());
    expect(sinPermisos.result.current).toBe(false);

    useAuthStore.setState({
      permisos: [PermisosCanonicos.ComprasConfiguracionLeer],
    });
    const conPermisos = renderHook(() => useAdminAccess());
    expect(conPermisos.result.current).toBe(true);
  });
});
