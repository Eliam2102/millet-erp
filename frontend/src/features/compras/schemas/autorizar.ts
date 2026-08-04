import { z } from 'zod';
import { NivelAutorizacion } from '@/features/compras/api/types';

/**
 * Schema Zod del comando <c>POST /api/v1/compras/requisiciones/{id}/autorizaciones</c>
 * (mirror de <c>AutorizarRequisicionRequest</c> backend). Doc 05 §6 +
 * §11.2.
 *
 * <para><b>Validación estructural</b>: nivel ∈ {1, 2}; notas ≤ 500
 * caracteres. Las reglas de negocio (estado válido, permiso del
 * nivel, no firmar 2x el mismo nivel, N2 después de N1) las decide
 * el backend (handler + endpoint). Frontend gate vía
 * <c>acciones-disponibles.ts</c>.</para>
 */
export const AutorizarSchema = z.object({
  nivel: z.union([
    z.literal(NivelAutorizacion.Nivel1),
    z.literal(NivelAutorizacion.Nivel2),
  ]),
  notas: z.string().max(500, 'Máximo 500 caracteres').nullish(),
});

export type AutorizarValues = z.infer<typeof AutorizarSchema>;
