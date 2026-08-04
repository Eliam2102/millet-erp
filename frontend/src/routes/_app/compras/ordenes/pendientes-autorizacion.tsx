import { createFileRoute } from '@tanstack/react-router';
import { BandejaPendientesOc } from '@/features/compras/ordenes/pages/BandejaPendientesOc';

/**
 * Ruta P2 — Bandeja de pendientes de autorización OC (UF4-PR1).
 *
 * <para>Sin <c>validateSearch</c>: la bandeja maneja el nivel
 * (Nivel1/Nivel2) en estado local porque depende de los permisos del
 * usuario y se selecciona desde un tab switcher dentro de la página,
 * no del URL.</para>
 */
export const Route = createFileRoute(
  '/_app/compras/ordenes/pendientes-autorizacion',
)({
  component: BandejaPendientesOc,
});
