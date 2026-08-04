import { z } from 'zod';

/**
 * Mirror del enum backend <c>NivelAutorizacion</c> (smallint).
 * <c>Nivel1 = 1</c>, <c>Nivel2 = 2</c>.
 *
 * <para><b>Importante</b>: el backend NO tiene
 * <c>JsonStringEnumConverter</c> configurado, así que System.Text.Json
 * deserializa enums como números. Mandar el valor numérico, no el
 * string del case.</para>
 */
export const NivelAutorizacion = {
  Nivel1: 1,
  Nivel2: 2,
} as const satisfies Record<string, number>;
export type NivelAutorizacion =
  (typeof NivelAutorizacion)[keyof typeof NivelAutorizacion];

/**
 * Schema Zod del body del POST <c>/api/v1/compras/ordenes/{id}/autorizaciones</c>
 * (UF4-PR1). El backend valida también el permiso correspondiente
 * (<c>autorizar-nivel1</c> o <c>autorizar-nivel2</c>) según el campo
 * <c>nivel</c>.
 */
export const AutorizarOcSchema = z.object({
  /** Valor numérico del enum <c>NivelAutorizacion</c> (1 o 2). */
  nivel: z.union([
    z.literal(NivelAutorizacion.Nivel1),
    z.literal(NivelAutorizacion.Nivel2),
  ]),
  /** Notas opcionales del autorizador. */
  notas: z.string().max(500).nullish(),
});

export type AutorizarOcValues = z.infer<typeof AutorizarOcSchema>;
