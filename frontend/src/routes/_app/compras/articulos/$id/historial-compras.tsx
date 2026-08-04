import { createFileRoute } from '@tanstack/react-router';
import { HistorialComprasMaterial } from '@/features/compras/ordenes/pages/HistorialComprasMaterial';

/**
 * Ruta P11 — Historial de últimas 100 compras del material (UF7-PR3).
 *
 * Path: <c>/compras/articulos/$id/historial-compras</c>.
 */
export const Route = createFileRoute(
  '/_app/compras/articulos/$id/historial-compras',
)({
  component: HistorialComprasMaterial,
});
