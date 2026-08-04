import { z } from 'zod';
import { TipoDestinoViatico } from '@/features/cxp/api/types';

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE_ONLY_RE = /^\d{4}-\d{2}-\d{2}$/;

export const SolicitarAnticipoViaticosSchema = z
  .object({
    empleadoId: z.string().regex(UUID_RE, 'UUID empleado inválido.'),
    puestoId: z.string().regex(UUID_RE, 'UUID puesto inválido.'),
    jefeDirectoId: z.string().regex(UUID_RE, 'UUID jefe directo inválido.'),
    destino: z
      .string()
      .trim()
      .min(2, 'Destino mínimo 2 caracteres.')
      .max(100),
    tipoDestino: z.union([
      z.literal(TipoDestinoViatico.Nacional),
      z.literal(TipoDestinoViatico.Internacional),
    ]),
    fechaSalida: z.string().regex(DATE_ONLY_RE),
    fechaRegreso: z.string().regex(DATE_ONLY_RE),
    moneda: z.string().trim().length(3),
    montoSolicitado: z.number().positive('Monto > 0.'),
    justificacionExceso: z.string().trim().max(1000).nullable(),
  })
  .refine((v) => v.fechaSalida <= v.fechaRegreso, {
    message: 'Fecha de regreso debe ser ≥ fecha de salida.',
    path: ['fechaRegreso'],
  });

export type SolicitarAnticipoViaticosValues = z.infer<
  typeof SolicitarAnticipoViaticosSchema
>;

export const ComprobacionLineaSchema = z.object({
  cfdiRecibidoId: z.string().regex(UUID_RE).nullable(),
  // El input de UUID es de texto libre: vacío ≡ sin CFDI (se normaliza
  // a null en el submit del sheet).
  uuidCfdi: z
    .string()
    .regex(UUID_RE, 'UUID CFDI inválido.')
    .nullable()
    .or(z.literal('')),
  proveedorId: z.string().regex(UUID_RE).nullable(),
  folioProveedor: z.string().trim().max(40).nullable(),
  fechaGasto: z.string().regex(DATE_ONLY_RE),
  subtotal: z.number().min(0),
  impuestosTrasladados: z.number().min(0),
  retenciones: z.number().min(0),
  total: z.number().positive('Total > 0.'),
  moneda: z.string().trim().length(3),
  concepto: z
    .string()
    .trim()
    .min(2, 'Concepto mínimo 2 caracteres.')
    .max(500),
  esTicketNoFiscal: z.boolean(),
});

export const CapturarComprobacionViaticosSchema = z.object({
  lineas: z
    .array(ComprobacionLineaSchema)
    .min(1, 'Agrega al menos un CFDI/ticket.')
    .max(50, 'Máximo 50 líneas.'),
});

export type CapturarComprobacionViaticosValues = z.infer<
  typeof CapturarComprobacionViaticosSchema
>;

export const RechazarViaticosSchema = z.object({
  motivo: z
    .string({ error: 'Motivo obligatorio.' })
    .trim()
    .min(5)
    .max(500),
});

export type RechazarViaticosValues = z.infer<typeof RechazarViaticosSchema>;
