import { z } from 'zod';
import {
  EstadoOrdenCompra,
  SubEstadoFacturacion,
  SubEstadoPago,
  SubEstadoRecepcion,
} from '@/features/compras/ordenes/api/types';

/**
 * Schema Zod de los <c>search params</c> de la pantalla P9 — Partidas
 * abiertas (UF7-PR1). Mirror de
 * <c>ListarPartidasAbiertasQuery</c> backend.
 *
 * <para>TanStack Router los valida al montar; valores inválidos en la
 * URL caen al default silenciosamente. Cambios de filtros generan una
 * nueva URL (preserva navegación adelante/atrás del browser).</para>
 */
export const PartidasAbiertasSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoOrdenCompra.Borrador),
      z.literal(EstadoOrdenCompra.EnAutorizacionJefeCompras),
      z.literal(EstadoOrdenCompra.EnAutorizacionDireccion),
      z.literal(EstadoOrdenCompra.Autorizada),
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
  fechaDesde: z.string().min(1).optional(),
  fechaHasta: z.string().min(1).optional(),
  numeroContenedor: z.string().min(1).optional(),
  codigoRuta: z.string().min(1).optional(),
  semanaEmbarque: z.string().min(1).optional(),
  diasAtrasadosMinimos: z.coerce.number().int().min(0).optional(),
  page: z.coerce.number().int().min(1).optional().default(1),
  pageSize: z.coerce.number().int().min(10).max(200).optional().default(50),
});

export type PartidasAbiertasSearch = z.infer<
  typeof PartidasAbiertasSearchSchema
>;

export const DEFAULT_PARTIDAS_ABIERTAS_SEARCH: PartidasAbiertasSearch = {
  page: 1,
  pageSize: 50,
};
