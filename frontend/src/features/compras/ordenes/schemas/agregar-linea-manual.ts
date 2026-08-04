import { z } from 'zod';
import { DescuentoTipo } from '@/features/compras/ordenes/api/types';
import {
  cabeEnDecimales,
  DECIMALES_FALLBACK,
} from '@/components/erp/forms/decimales-unidad';

const UUID_SHAPE_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const idLike = (msg: string) =>
  z.string().regex(UUID_SHAPE_RE, msg);

/**
 * Schema Zod del comando <c>POST /api/v1/compras/ordenes/{id}/lineas</c>
 * (UF2-PR3 — agregar línea manual). Mirror del shape esperado por el
 * backend <c>AgregarLineaManualOcCommand</c>. La línea se agrega a una
 * OC en <c>Borrador</c>/<c>Rechazada</c> con
 * <c>sinRequisicionPrevia=true</c> (el endpoint valida).
 *
 * <para>Campos NO en este schema (los rellena el caller desde la URL
 * o el contexto): <c>OrdenCompraId</c> (URL param). El permiso
 * <c>compras.ordenes.crear</c> ya está gateado por la matriz.</para>
 */
export function crearAgregarLineaManualSchema(
  getDecimalesCantidad: () => number = () => DECIMALES_FALLBACK,
  ccRequerido = true,
) {
  return z.object({
  articuloId: idLike('Selecciona un artículo.'),
  cantidad: z
    .number()
    .positive('La cantidad debe ser mayor a 0.')
    .max(999_999_999, 'Cantidad fuera de rango.')
    .refine(
      (v) => cabeEnDecimales(v, getDecimalesCantidad()),
      'Demasiados decimales para la unidad seleccionada',
    ),
  unidadMedida: z
    .string()
    .min(1, 'La unidad de medida es requerida.')
    .max(20, 'Máximo 20 caracteres.'),
  precioUnitario: z
    .number()
    .nonnegative('El precio no puede ser negativo.')
    .max(999_999_999, 'Precio fuera de rango.'),
  departamentoSolicitanteId: idLike('Selecciona un departamento.'),
  // Descuento opcional. Si tipo es Porcentaje, valor 0-100. Si es
  // Monto, no negativo. Las dos validaciones combinadas en
  // superRefine.
  descuentoTipo: z
    .union([
      z.literal(DescuentoTipo.Porcentaje),
      z.literal(DescuentoTipo.Monto),
    ])
    .nullish(),
  descuentoValor: z
    .number()
    .nonnegative('El descuento no puede ser negativo.')
    .nullish(),
  indicadorImpuestos: z.string().max(50).nullish(),
  descripcionExtendida: z.string().max(1000).nullish(),
  fechaEntregaLinea: z.string().nullish(),
  textoAdicional: z.string().max(2000).nullish(),
  // Fase E PR3.1: CC-Máquina elegido por el comprador (proxy, selector
  // abierto). REQUERIDO en la línea manual. La exigencia es condicional
  // porque este mismo schema resuelve el form al EDITAR una línea heredada
  // de RQ, donde el campo se pinta read-only (el CC viene 1:1 de la RQ): si
  // fuera requerido siempre, una línea heredada legada con CC null quedaría
  // imposible de guardar — requerida pero no capturable. Ver ADR-0050.
  // El tipo inferido no cambia (string | null | undefined) en ninguna rama.
  centroCostoId: ccRequerido
    ? z
        .string()
        .regex(UUID_SHAPE_RE, 'CC-Máquina requerido')
        .nullish()
        .refine((v) => v != null, 'CC-Máquina requerido')
    : z.string().regex(UUID_SHAPE_RE).nullish(),
}).superRefine((data, ctx) => {
  if (data.descuentoTipo != null && data.descuentoValor == null) {
    ctx.addIssue({
      code: z.ZodIssueCode.custom,
      path: ['descuentoValor'],
      message: 'Captura el valor del descuento.',
    });
  }
  if (
    data.descuentoTipo === DescuentoTipo.Porcentaje &&
    data.descuentoValor != null &&
    data.descuentoValor > 100
  ) {
    ctx.addIssue({
      code: z.ZodIssueCode.custom,
      path: ['descuentoValor'],
      message: 'El porcentaje no puede exceder 100.',
    });
  }
  });
}

/** Schema estático con el fallback global (usos que no resuelven unidad). */
export const AgregarLineaManualSchema = crearAgregarLineaManualSchema();

export type AgregarLineaManualValues = z.infer<
  typeof AgregarLineaManualSchema
>;

/** Default puro para inicializar react-hook-form. */
export const DEFAULT_AGREGAR_LINEA_MANUAL: AgregarLineaManualValues = {
  articuloId: '',
  cantidad: 1,
  // Placeholder hasta elegir artículo: la UM se hereda de
  // UnidadMedidaDefault al seleccionar (campo read-only). No persiste
  // porque articuloId es required.
  unidadMedida: '',
  precioUnitario: 0,
  departamentoSolicitanteId: '',
  descuentoTipo: null,
  descuentoValor: null,
  indicadorImpuestos: null,
  descripcionExtendida: null,
  fechaEntregaLinea: null,
  textoAdicional: null,
  centroCostoId: null,
};
