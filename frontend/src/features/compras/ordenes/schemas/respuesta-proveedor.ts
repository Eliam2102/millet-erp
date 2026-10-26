import { z } from 'zod';
import { zId } from '@/lib/z-id';

export const CompromisoEntregaSchema = z.object({
  lineaOrdenCompraId: zId(),
  cantidad: z.number().positive('La cantidad prometida debe ser mayor a cero.'),
  fechaPrometida: z.string().min(1, 'Indica la fecha prometida.'),
});

/** F1-COM-11: respuesta del proveedor (1 = confirma, 2 = rechaza). */
export const RegistrarRespuestaProveedorSchema = z
  .object({
    tipo: z.union([z.literal(1), z.literal(2)]).nullable().optional(),
    fechaRespuesta: z.string().optional(),
    archivo: z.instanceof(File, { error: 'Adjunta el archivo de evidencia de la respuesta del proveedor.' }).refine(f => f.size > 0, 'El archivo está vacío.'),
    evidencia: z
      .string()
      .trim()
      .max(300).optional(),
    contactoProveedor: z.string().trim().max(120).optional(),
    motivo: z.string().trim().max(500).optional(),
    compromisos: z.array(CompromisoEntregaSchema).max(200),
  })
  .superRefine((v, ctx) => {
    if (v.tipo === 2) {
      if (v.compromisos.length > 0) {
        ctx.addIssue({ code: 'custom', path: ['compromisos'], message: 'Un rechazo no lleva fechas prometidas.' });
      }
    }
    if (v.fechaRespuesta && v.compromisos.some((c) => c.fechaPrometida < v.fechaRespuesta!)) {
      ctx.addIssue({
        code: 'custom',
        path: ['compromisos'],
        message: 'La fecha prometida no puede ser anterior a la fecha de respuesta.',
      });
    }
  });

export type RegistrarRespuestaProveedorValues = z.infer<typeof RegistrarRespuestaProveedorSchema>;
