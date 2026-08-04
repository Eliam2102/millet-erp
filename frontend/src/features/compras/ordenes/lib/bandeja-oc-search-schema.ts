import { z } from 'zod';
import {
  EstadoOrdenCompra,
  SubEstadoFacturacion,
  SubEstadoPago,
  SubEstadoRecepcion,
} from '@/features/compras/ordenes/api/types';

/**
 * Schema Zod de los <c>search params</c> de la bandeja P1 de OCs
 * (<c>routes/_app/compras/ordenes/index.tsx</c>). TanStack Router los
 * valida al montar; valores inválidos en la URL se filtran
 * silenciosamente al default.
 *
 * <para>Diferencia con RQ (<c>bandeja-search-schema.ts</c>): la bandeja
 * de OC usa <c>page</c>/<c>pageSize</c> (mirror del backend
 * <c>ListarOrdenesCompraQuery</c>), mientras que RQ usa
 * <c>offset</c>/<c>limit</c>. Cada submódulo respeta el shape de su
 * endpoint.</para>
 *
 * <para><b>Filtros soportados</b> (mirror de
 * <c>OrdenesCompraEndpoints.cs</c> §F6-PR3): estado, los 3 sub-estados,
 * proveedorId, compradorTitularId, fechaDesde/Hasta (ISO 8601),
 * referenciaProveedor (texto libre — alimentado por el search input
 * del topbar global vía <c>q</c>). NO se incluye contenedor/ruta/semana
 * porque esos campos no existen en el response de cabecera y el
 * endpoint de bandeja general tampoco los acepta como filtro
 * (los maneja el endpoint de partidas abiertas — UF7-PR1).</para>
 */
export const BandejaOcSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoOrdenCompra.Borrador),
      z.literal(EstadoOrdenCompra.EnAutorizacionJefeCompras),
      z.literal(EstadoOrdenCompra.EnAutorizacionDireccion),
      z.literal(EstadoOrdenCompra.Autorizada),
      z.literal(EstadoOrdenCompra.Cerrada),
      z.literal(EstadoOrdenCompra.Cancelada),
      z.literal(EstadoOrdenCompra.Rechazada),
    ])
    .optional(),
  subEstadoRecepcion: z
    .union([
      z.literal(SubEstadoRecepcion.SinRecepcion),
      z.literal(SubEstadoRecepcion.Parcial),
      z.literal(SubEstadoRecepcion.Completa),
    ])
    .optional(),
  subEstadoFacturacion: z
    .union([
      z.literal(SubEstadoFacturacion.SinFactura),
      z.literal(SubEstadoFacturacion.Parcial),
      z.literal(SubEstadoFacturacion.Completa),
    ])
    .optional(),
  subEstadoPago: z
    .union([
      z.literal(SubEstadoPago.SinPago),
      z.literal(SubEstadoPago.Parcial),
      z.literal(SubEstadoPago.Pagada),
    ])
    .optional(),
  proveedorId: z.string().min(1).optional(),
  compradorTitularId: z.string().min(1).optional(),
  /** ISO 8601 (date o datetime), validado por el backend. */
  fechaDesde: z.string().min(1).optional(),
  fechaHasta: z.string().min(1).optional(),
  /**
   * Búsqueda por <c>referenciaProveedor</c> (free text). Alimentado
   * por el input <c>q</c> del topbar global (UF0-PR1 ya registró las
   * 3 rutas OC en <c>SEARCHABLE_ROUTES</c>).
   */
  q: z.string().min(1).optional(),
  /** Default backend = 1. */
  page: z.coerce.number().int().min(1).optional().default(1),
  /** Default backend = 50. Mín 10, máx 200. */
  pageSize: z.coerce.number().int().min(10).max(200).optional().default(50),
});

export type BandejaOcSearch = z.infer<typeof BandejaOcSearchSchema>;

/**
 * Default puro (todos los filtros vacíos, paginación al inicio). Útil
 * para tests, para "limpiar todos los filtros" en la UI y para el
 * redirect de <c>/compras/ordenes</c> sin search params.
 */
export const DEFAULT_BANDEJA_OC_SEARCH: BandejaOcSearch = {
  page: 1,
  pageSize: 50,
};
