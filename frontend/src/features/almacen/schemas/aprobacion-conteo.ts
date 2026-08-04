import { z } from 'zod';

export const AgregarRecuentoSchema = z.object({
  cantidadRecontada: z
    .number({ message: 'Cantidad requerida' })
    .nonnegative('La cantidad no puede ser negativa'),
});

export type AgregarRecuentoValues = z.infer<typeof AgregarRecuentoSchema>;

export const AprobarLineaIndividualmenteSchema = z.object({
  justificacion: z
    .string()
    .min(1, 'Justificación requerida')
    .max(500, 'Máximo 500 caracteres'),
});

export type AprobarLineaIndividualmenteValues = z.infer<
  typeof AprobarLineaIndividualmenteSchema
>;

export const RechazarConteoSchema = z.object({
  motivo: z
    .string()
    .min(1, 'Motivo requerido')
    .max(500, 'Máximo 500 caracteres'),
});

export type RechazarConteoValues = z.infer<typeof RechazarConteoSchema>;
