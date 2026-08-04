import { createFileRoute, redirect } from '@tanstack/react-router';
import { InventariosPage } from '@/features/almacen/pages/InventariosPage';
import {
  ConteosSearchSchema,
  type ConteosSearch,
} from '@/features/almacen/lib/conteos-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P7 — Bandeja de conteos (doc 07 §FE-F5-PR1).
 * Gate: <c>almacen.inventarios.leer</c>.
 */
export const Route = createFileRoute('/_app/almacen/inventarios/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenInventariosLeer)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: InventariosPage,
  validateSearch: (input: Record<string, unknown>): ConteosSearch =>
    ConteosSearchSchema.parse(input),
});
