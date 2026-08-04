import { z } from 'zod';
import {
  ReinicioPeriodo,
  TipoDocumentoSerie,
} from '@/modules/administracion/api/types';

/**
 * Schemas Zod del módulo Administración → Series. Mirror de los
 * validators FluentValidation backend (<c>CrearSerieValidator</c>,
 * <c>ActualizarSerieValidator</c>).
 *
 * <para><b>Reglas backend</b>: <c>Prefijo</c> 1-10; <c>Sufijo</c> 0-10
 * opcional; <c>TipoDocumento</c> y <c>ReinicioPeriodo</c> deben ser
 * valores válidos del enum. Empresa, Sucursal, TipoDocumento son
 * inmutables tras el alta (no aparecen en
 * <c>ActualizarSerieSchema</c>).</para>
 */

/** Sufijo: trim, transform a null si vacío. */
const sufijoOptional = z
  .string()
  .trim()
  .max(10, 'Máximo 10 caracteres')
  .nullable()
  .transform((v) => (v != null && v.length === 0 ? null : v));

/**
 * Regex laxo de UUID — formato <c>8-4-4-4-12</c> hex sin requerir
 * version/variant válidos. El backend usa GUIDs seed deterministas
 * (ej. <c>00000003-0000-0000-0000-000000000001</c>) que Zod v4
 * <c>.uuid()</c> rechazaría. Mismo patrón que <c>UUID_SHAPE_RE</c>
 * en otros módulos.
 */
const UUID_SHAPE_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const reinicioPeriodoSchema = z.union([
  z.literal(ReinicioPeriodo.None),
  z.literal(ReinicioPeriodo.Anual),
  z.literal(ReinicioPeriodo.Mensual),
]);

const tipoDocumentoSchema = z.union([
  z.literal(TipoDocumentoSerie.OrdenCompra),
  z.literal(TipoDocumentoSerie.Cfdi),
  z.literal(TipoDocumentoSerie.NotaCredito),
  z.literal(TipoDocumentoSerie.Poliza),
  z.literal(TipoDocumentoSerie.FacturaAnticipo),
]);

export const CrearSerieSchema = z.object({
  empresaId: z.string().regex(UUID_SHAPE_RE, 'Empresa inválida'),
  sucursalId: z.string().regex(UUID_SHAPE_RE, 'Sucursal inválida').nullable(),
  tipoDocumento: tipoDocumentoSchema,
  prefijo: z
    .string()
    .trim()
    .min(1, 'Prefijo requerido')
    .max(10, 'Máximo 10 caracteres'),
  sufijo: sufijoOptional,
  reinicioPeriodo: reinicioPeriodoSchema,
});

export type CrearSerieValues = z.infer<typeof CrearSerieSchema>;

/**
 * Schema del PATCH /admin/series/{id}. Solo Prefijo, Sufijo y
 * ReinicioPeriodo son mutables. <c>limpiarSufijo</c> NO se modela
 * acá: el caller (inline form) lo deriva de Sufijo vacío.
 */
export const ActualizarSerieSchema = z.object({
  prefijo: z
    .string()
    .trim()
    .min(1, 'Prefijo requerido')
    .max(10, 'Máximo 10 caracteres'),
  sufijo: sufijoOptional,
  reinicioPeriodo: reinicioPeriodoSchema,
});

export type ActualizarSerieValues = z.infer<typeof ActualizarSerieSchema>;
