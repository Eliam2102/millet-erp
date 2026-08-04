import { createFileRoute, redirect } from '@tanstack/react-router';
import { CapturaConteoPage } from '@/features/almacen/pages/CapturaConteoPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P9 — Captura sin sesgo (doc 07 §FE-F5-PR1, doc 00 §A6).
 * Gate: <c>almacen.inventarios.capturar</c>. Sin este permiso el
 * usuario no puede llamar al endpoint subyacente
 * <c>/lineas-para-capturar</c> y por lo tanto no carga la pantalla.
 *
 * <para>El rol del contador tiene <c>capturar</c> pero NO
 * <c>aprobar-nivel1+</c>. La pantalla de comparación (con teórico)
 * exige <c>aprobar-nivel1+</c> y vive en otra ruta.</para>
 */
export const Route = createFileRoute('/_app/almacen/inventarios/$id/captura')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenInventariosCapturar)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: CapturaConteoPage,
});
