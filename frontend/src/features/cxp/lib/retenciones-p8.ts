import { z } from 'zod';
import { zId } from '@/lib/z-id';
import type { RetencionCfdi } from '@/features/cxp/api/types';

export const RetencionSchema = z.object({
  id: zId().optional(),
  version: z.number().int().min(0).optional(),
  concepto: z.string().trim().min(1, 'Concepto obligatorio.').max(80),
  descripcion: z.string().trim().min(1, 'Descripción obligatoria.').max(300),
  impuesto: z.enum(['001', '002', '003']),
  tasa: z.number().min(0).max(1),
  fuente: z.string().trim().min(1, 'Fuente obligatoria.').max(1000),
  activa: z.boolean(),
  motivo: z.string().trim().min(10, 'Explica el cambio en al menos 10 caracteres.').max(500),
});
export type RetencionForm = z.infer<typeof RetencionSchema>;
export interface ReglaRetencion {
  concepto: string;
  impuesto: string;
  tasa: number;
  activa: boolean;
}
export function proponerRetenciones(
  base: number,
  concepto: string,
  reglas: readonly ReglaRetencion[],
): RetencionCfdi[] {
  return reglas
    .filter((r) => r.activa && r.concepto === concepto)
    .map((r) => ({
      impuesto: r.impuesto,
      tasa: r.tasa,
      importe: Math.round((base * r.tasa + Number.EPSILON) * 100) / 100,
    }));
}
export function totalRetenciones(detalle: readonly RetencionCfdi[]): number {
  return Math.round(detalle.reduce((sum, r) => sum + r.importe, 0) * 100) / 100;
}
export function alertaRetenciones(total: number, propuesta: readonly RetencionCfdi[]): boolean {
  return propuesta.length > 0 && Math.abs(total - totalRetenciones(propuesta)) > 0.010000001;
}
