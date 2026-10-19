import { z } from 'zod';
import type {
  UmbralAprobacionResponse,
  UpsertUmbralRequest,
} from '@/features/compras/api/types';

/**
 * Id reservado del umbral general de empresa (respaldo de la OC cuando el
 * departamento no tiene fila propia). Mismo valor que <c>Guid.Empty</c> en backend.
 */
export const UMBRAL_GENERAL_ID = '00000000-0000-0000-0000-000000000000';

/** Monto no negativo con hasta 2 decimales (mismo criterio que el importe MXN). */
const MONTO_RE = /^\d+(\.\d{1,2})?$/;
const MSG_MONTO = 'Ingresa un monto válido (cero o más, hasta 2 decimales).';
const MSG_N1_MENOR_N2 =
  'El umbral N1 (menor cuantía) debe ser menor que el umbral N2.';

/**
 * Esquema Zod del formulario de una fila de umbrales. Los campos viajan
 * como texto para no perder lo tecleado; N1 vacío = sin umbral N1; mínimo por
 * disponibilidad vacío = sin mínimo.
 *
 * <para>Reglas: N2 obligatorio; N1 opcional; ambos montos con formato
 * válido; N1 estrictamente menor que N2 (mismo criterio que
 * <c>UpsertUmbralValidator</c> del backend).</para>
 */
export const UmbralAprobacionSchema = z
  .object({
    umbralMenorCuantia: z.string().trim(),
    umbralMonto: z.string().trim().min(1, 'Define el umbral N2.'),
    modoNivel2: z.enum(['SoloMonto', 'SoloDisponibilidad', 'Ambos']),
    montoMinimoDisponibilidad: z.string().trim(),
  })
  .superRefine((v, ctx) => {
    const n1Capturado = v.umbralMenorCuantia !== '';

    if (n1Capturado && !MONTO_RE.test(v.umbralMenorCuantia)) {
      ctx.addIssue({
        code: 'custom',
        path: ['umbralMenorCuantia'],
        message: MSG_MONTO,
      });
    }
    if (v.montoMinimoDisponibilidad !== '' && !MONTO_RE.test(v.montoMinimoDisponibilidad)) {
      ctx.addIssue({
        code: 'custom',
        path: ['montoMinimoDisponibilidad'],
        message: MSG_MONTO,
      });
    }
    if (!MONTO_RE.test(v.umbralMonto)) {
      ctx.addIssue({
        code: 'custom',
        path: ['umbralMonto'],
        message: MSG_MONTO,
      });
      return;
    }
    if (n1Capturado && MONTO_RE.test(v.umbralMenorCuantia)) {
      if (Number(v.umbralMenorCuantia) >= Number(v.umbralMonto)) {
        ctx.addIssue({
          code: 'custom',
          path: ['umbralMenorCuantia'],
          message: MSG_N1_MENOR_N2,
        });
      }
    }
  });

export type UmbralFormValues = z.infer<typeof UmbralAprobacionSchema>;

/** Valores iniciales del formulario a partir del umbral vigente (o vacío si no hay). */
export function valoresIniciales(
  umbral: UmbralAprobacionResponse | undefined,
): UmbralFormValues {
  return {
    umbralMenorCuantia:
      umbral?.umbralMenorCuantia == null
        ? ''
        : String(umbral.umbralMenorCuantia),
    umbralMonto: umbral ? String(umbral.umbralMonto) : '',
    modoNivel2: umbral?.modoNivel2 ?? 'SoloDisponibilidad',
    montoMinimoDisponibilidad:
      umbral?.montoMinimoDisponibilidad == null ? '' : String(umbral.montoMinimoDisponibilidad),
  };
}

/** Convierte los valores validados al body de <c>PUT /umbrales-aprobacion</c>. */
export function aUpsertUmbral(
  departamentoId: string,
  values: UmbralFormValues,
): UpsertUmbralRequest {
  return {
    departamentoId,
    umbralMenorCuantia:
      values.umbralMenorCuantia === ''
        ? null
        : Number(values.umbralMenorCuantia),
    umbralMonto: Number(values.umbralMonto),
    moneda: 'MXN',
    modoNivel2: values.modoNivel2,
    montoMinimoDisponibilidad:
      values.montoMinimoDisponibilidad === '' ? null : Number(values.montoMinimoDisponibilidad),
  };
}
