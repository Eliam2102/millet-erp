import { z } from 'zod';
import { zId } from '@/lib/z-id';
import {
  ClasePedidoManual,
  ComportamientoFiscal,
  SubtipoVentaDirecta,
} from '@/features/facturacion/api/types';

/**
 * Schema del form "Nuevo pedido manual" (FE-F1-PR1). Mirror de
 * <c>CrearPedidoFacturableManualValidator</c> del backend
 * (Application/Pedidos/CrearPedidoFacturableManual). El backend es la
 * barrera real; este schema da feedback inmediato y limpia el payload.
 *
 * <para>Cliente: mientras no exista el maestro de clientes (auto-provisión
 * A+W pendiente), <c>clienteId</c> se captura como GUID a mano +
 * <c>clienteNombre</c> libre. PLATFORM-TODO(&lt;ClienteSelector&gt;) en
 * el form.</para>
 */
export const PedidoLineaSchema = z.object({
  productoId: zId().nullable(),
  productoDescripcion: z
    .string()
    .min(1, 'Describe el producto o servicio')
    .max(1000),
  claveProdServSat: z.string().max(10).nullable(),
  claveUnidadSat: z.string().max(10).nullable(),
  cantidad: z.number().positive('La cantidad debe ser mayor a 0'),
  precio: z.number().min(0, 'El precio no puede ser negativo'),
  descuento: z.number().min(0, 'El descuento no puede ser negativo'),
  requierePedimento: z.boolean(),
});

export type PedidoLineaValues = z.infer<typeof PedidoLineaSchema>;

const comportamientoValues = Object.values(ComportamientoFiscal) as [
  number,
  ...number[],
];

const PedidoManualBaseSchema = z.object({
  numeroPedido: z.string().max(50).nullable(),
  sucursalId: zId('Selecciona una sucursal'),
  clienteId: zId('ClienteId debe ser un GUID válido'),
  clienteNombre: z
    .string()
    .min(1, 'Captura el nombre o razón social del cliente')
    .max(254),
  // Canal de venta = id del catálogo administrable (FAC-ING-PR3); la
  // existencia/actividad la valida el backend contra la BD.
  canalVenta: z
    .number()
    .int('Selecciona un canal de venta')
    .positive('Selecciona un canal de venta'),
  comportamientoFiscal: z
    .number()
    .refine(
      (v) => comportamientoValues.includes(v),
      'Selecciona un comportamiento fiscal',
    ),
  moneda: z.string().length(3, 'Usa el código ISO de 3 letras (MXN, USD…)'),
  obraId: z.number().int().positive().nullable(),
  obraNombre: z.string().max(254).nullable(),
  comentarios: z.string().max(1000).nullable(),
  lineas: z.array(PedidoLineaSchema).min(1, 'Agrega al menos una línea'),
  // ── F6.1: clasificación del pedido manual ──────────────────────────
  // Nullable en el form para obligar a elegir; el superRefine exige valor.
  claseManual: z
    .union([
      z.literal(ClasePedidoManual.VentaDirecta),
      z.literal(ClasePedidoManual.Emergencia),
    ])
    .nullable(),
  subtipoVentaDirecta: z
    .union([
      z.literal(SubtipoVentaDirecta.Activo),
      z.literal(SubtipoVentaDirecta.Almacenable),
      z.literal(SubtipoVentaDirecta.Servicio),
    ])
    .nullable(),
  motivoEmergencia: z
    .string()
    .max(500, 'El motivo admite hasta 500 caracteres')
    .nullable(),
  referenciaPedidoAw: z
    .string()
    .max(50, 'La referencia admite hasta 50 caracteres')
    .nullable(),
});

type PedidoManualBase = z.infer<typeof PedidoManualBaseSchema>;
type ClasificacionCampos = Pick<
  PedidoManualBase,
  | 'claseManual'
  | 'subtipoVentaDirecta'
  | 'motivoEmergencia'
  | 'comportamientoFiscal'
>;

/**
 * Reglas condicionales de clase (F6.1), espejo de
 * <c>ClasificacionPedidoManualValidator</c> del backend: clase obligatoria;
 * venta directa exige subtipo; emergencia exige motivo y no lleva subtipo;
 * subtipo Activo ⇔ comportamiento «Venta de activo fijo» (en ambos
 * sentidos — una emergencia no puede usar ese comportamiento).
 */
export function validarClasificacionPedido(
  v: ClasificacionCampos,
  ctx: z.RefinementCtx,
): void {
  if (v.claseManual == null) {
    ctx.addIssue({
      code: 'custom',
      path: ['claseManual'],
      message: 'Indica la clase del pedido: venta directa o emergencia.',
    });
    return;
  }
  if (v.claseManual === ClasePedidoManual.VentaDirecta) {
    if (v.subtipoVentaDirecta == null) {
      ctx.addIssue({
        code: 'custom',
        path: ['subtipoVentaDirecta'],
        message: 'Elige el subtipo de la venta directa: activo, almacenable o servicio.',
      });
    }
  } else {
    if ((v.motivoEmergencia ?? '').trim().length === 0) {
      ctx.addIssue({
        code: 'custom',
        path: ['motivoEmergencia'],
        message: 'Indica el motivo de la captura de emergencia.',
      });
    }
    if (v.subtipoVentaDirecta != null) {
      ctx.addIssue({
        code: 'custom',
        path: ['subtipoVentaDirecta'],
        message: 'Un pedido de emergencia no lleva subtipo de venta directa.',
      });
    }
  }
  const esActivo = v.subtipoVentaDirecta === SubtipoVentaDirecta.Activo;
  const esComportamientoActivo =
    v.comportamientoFiscal === ComportamientoFiscal.VentaActivoFijo;
  if (esActivo !== esComportamientoActivo) {
    ctx.addIssue({
      code: 'custom',
      path: ['comportamientoFiscal'],
      message: esActivo
        ? 'La venta de un activo fijo va con el comportamiento «Venta de activo fijo».'
        : '«Venta de activo fijo» solo aplica a una venta directa de subtipo Activo.',
    });
  }
}

export const PedidoManualSchema = PedidoManualBaseSchema.superRefine(
  validarClasificacionPedido,
);

export type PedidoManualValues = z.infer<typeof PedidoManualSchema>;

/**
 * Schema de edición (PUT): omite `sucursalId` y `numeroPedido`, que son
 * inmutables (el backend `EditarPedidoFacturableRequest` no los acepta y
 * el detalle GET no los devuelve). El resto se valida igual que al crear.
 */
export const EditarPedidoSchema = PedidoManualBaseSchema.omit({
  sucursalId: true,
  numeroPedido: true,
}).superRefine(validarClasificacionPedido);
