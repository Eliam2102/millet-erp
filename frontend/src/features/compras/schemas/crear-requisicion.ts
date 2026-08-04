import { z } from 'zod';
import {
  Clasificacion,
  Prioridad,
} from '@/features/compras/api/types';

/**
 * Regex laxo de UUID — formato <c>8-4-4-4-12</c> hex sin requerir
 * version/variant válidos (RFC 4122 v1-v8). Razón: el backend usa
 * GUIDs deterministas en seeds de catálogos (ej.
 * <c>00000005-0001-0000-0000-000000000001</c>) que NO califican como
 * UUID v1-v8 estricto pero son válidos como string opaco.
 *
 * <para>El frontend valida solo que el id <b>tenga shape de UUID</b>
 * (no string vacío, no garbage); el backend hace la verificación
 * real (ID existe en BD + permisos). Si Zod fuera más estricto que
 * el backend, los selectores con seeds reales fallarían en local con
 * "campo requerido" aunque el usuario haya seleccionado.</para>
 */
const UUID_SHAPE_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const idLike = (mensajeRequerido: string) =>
  z.string().regex(UUID_SHAPE_RE, mensajeRequerido);

const idLikeOpcional = z.string().regex(UUID_SHAPE_RE).nullish();

/**
 * Schema Zod del comando <c>POST /api/v1/compras/requisiciones</c>
 * (P4 — Nueva requisición). Doc 05 §11.2.
 *
 * <para><b>Solo validación estructural</b>: shape de UUIDs,
 * required vs nullable, longitud máxima de descripción. Las reglas
 * de negocio (matriz de aprobación, naturaleza más restrictiva, etc.)
 * las decide el backend (ADR-0018, doc 05 §11.1).</para>
 *
 * <para>Mirror del shape esperado por el handler backend:</para>
 * <list>
 *   <item><c>sucursalId</c>, <c>departamentoId</c> requeridos.
 *   (PR3: la RQ manual ya no captura <c>almacenDestinoId</c>.)</item>
 *   <item><c>requisitanteId</c> opcional (default = current_user del
 *   JWT en el backend; solo se manda si el usuario tiene
 *   <c>seleccionar-requisitante</c>).</item>
 *   <item><c>clasificacion</c> y <c>prioridad</c> requeridos como
 *   valor numérico del enum.</item>
 *   <item><c>fechaSolicitud</c> ISO UTC; el frontend lo construye con
 *   <c>parseLocalToUtc(now)</c> al submit.</item>
 *   <item><c>fechaEntregaDeseada</c> opcional, formato DateOnly
 *   (<c>YYYY-MM-DD</c>).</item>
 *   <item><c>proveedorSugeridoId</c> opcional.</item>
 *   <item><c>descripcion</c> opcional, máximo 500 caracteres.</item>
 * </list>
 */
export const CrearRequisicionSchema = z.object({
  sucursalId: idLike('Sucursal requerida'),
  departamentoId: idLike('Departamento requerido'),
  requisitanteId: idLikeOpcional,
  clasificacion: z.union([
    z.literal(Clasificacion.Servicio),
    z.literal(Clasificacion.OrdenCompra),
    z.literal(Clasificacion.MateriaPrima),
    z.literal(Clasificacion.Pinturas),
  ]),
  prioridad: z.union([
    z.literal(Prioridad.Baja),
    z.literal(Prioridad.Normal),
    z.literal(Prioridad.Alta),
  ]),
  fechaSolicitud: z.iso.datetime({ message: 'Fecha de solicitud requerida (ISO 8601 UTC)' }),
  fechaEntregaDeseada: z.iso.date().nullish(),
  proveedorSugeridoId: idLikeOpcional,
  descripcion: z.string().max(500, 'Máximo 500 caracteres').nullish(),
});

export type CrearRequisicionValues = z.infer<typeof CrearRequisicionSchema>;
