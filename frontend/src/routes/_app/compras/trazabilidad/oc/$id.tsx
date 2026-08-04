import { createFileRoute } from '@tanstack/react-router';
import { TrazabilidadOc } from '@/features/compras/ordenes/pages/TrazabilidadOc';

/**
 * Ruta P10 — Trazabilidad de OC (UF7-PR2).
 *
 * <para>Path: <c>/compras/trazabilidad/oc/$id</c>. CxP/Recepción/
 * Tesorería tendrán rutas análogas (<c>/compras/trazabilidad/cxp/$id</c>,
 * etc.) usando el mismo componente <c>&lt;ArbolDocumentos/&gt;</c>.</para>
 */
export const Route = createFileRoute('/_app/compras/trazabilidad/oc/$id')({
  component: TrazabilidadOc,
});
