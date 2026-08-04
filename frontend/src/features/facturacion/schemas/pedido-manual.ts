import { z } from 'zod';
import { ComportamientoFiscal } from '@/features/facturacion/api/types';

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
  productoId: z.string().uuid().nullable(),
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

export const PedidoManualSchema = z.object({
  numeroPedido: z.string().max(50).nullable(),
  sucursalId: z.string().uuid('Selecciona una sucursal'),
  clienteId: z.string().uuid('ClienteId debe ser un GUID válido'),
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
});

export type PedidoManualValues = z.infer<typeof PedidoManualSchema>;

/**
 * Schema de edición (PUT): omite `sucursalId` y `numeroPedido`, que son
 * inmutables (el backend `EditarPedidoFacturableRequest` no los acepta y
 * el detalle GET no los devuelve). El resto se valida igual que al crear.
 */
export const EditarPedidoSchema = PedidoManualSchema.omit({
  sucursalId: true,
  numeroPedido: true,
});
