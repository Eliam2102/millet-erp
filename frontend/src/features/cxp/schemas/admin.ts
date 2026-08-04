import { z } from 'zod';
import {
  TipoDestinoViatico,
  TipoGastoAprobador,
} from '@/features/cxp/api/types';

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE_ONLY_RE = /^\d{4}-\d{2}-\d{2}$/;

export const CrearAprobadorLimiteSchema = z
  .object({
    empleadoId: z.string().regex(UUID_RE, 'UUID empleado inválido.'),
    tipoGasto: z.union([
      z.literal(TipoGastoAprobador.ReembolsoCajaChica),
      z.literal(TipoGastoAprobador.Viaticos),
      z.literal(TipoGastoAprobador.TarjetaCreditoEmpresarial),
      z.literal(TipoGastoAprobador.OtrosSinOc),
    ]),
    montoMax: z.number().positive('Monto > 0.'),
    moneda: z.string().trim().length(3),
    vigenciaDesde: z.string().regex(DATE_ONLY_RE),
    vigenciaHasta: z.string().regex(DATE_ONLY_RE).nullable(),
  })
  .refine(
    (v) => v.vigenciaHasta == null || v.vigenciaHasta >= v.vigenciaDesde,
    {
      message: 'Vigencia hasta debe ser ≥ desde.',
      path: ['vigenciaHasta'],
    },
  );

export type CrearAprobadorLimiteValues = z.infer<
  typeof CrearAprobadorLimiteSchema
>;

export const ActualizarAprobadorLimiteSchema = z.object({
  montoMax: z.number().positive('Monto > 0.'),
  moneda: z.string().trim().length(3),
});

export type ActualizarAprobadorLimiteValues = z.infer<
  typeof ActualizarAprobadorLimiteSchema
>;

export const CrearPoliticaViaticosSchema = z.object({
  puestoId: z.string().regex(UUID_RE, 'UUID puesto inválido.'),
  tipoDestino: z.union([
    z.literal(TipoDestinoViatico.Nacional),
    z.literal(TipoDestinoViatico.Internacional),
  ]),
  montoMaxDia: z.number().positive('Monto > 0.'),
  diasMax: z.number().int().positive('Días > 0.'),
  moneda: z.string().trim().length(3),
});

export type CrearPoliticaViaticosValues = z.infer<
  typeof CrearPoliticaViaticosSchema
>;

export const ActualizarPoliticaViaticosSchema = z.object({
  montoMaxDia: z.number().positive('Monto > 0.'),
  diasMax: z.number().int().positive('Días > 0.'),
  moneda: z.string().trim().length(3),
});

export type ActualizarPoliticaViaticosValues = z.infer<
  typeof ActualizarPoliticaViaticosSchema
>;
