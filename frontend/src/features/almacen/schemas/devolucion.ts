import { z } from 'zod';

const UUID_SHAPE_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE_ONLY_RE = /^\d{4}-\d{2}-\d{2}$/;

// ─── Devoluciones internas (8.A) ────────────────────────────────────────────

export const AplicarDevolucionInternaSchema = z.object({
  salidaOrigenId: z.string().regex(UUID_SHAPE_RE, 'Salida origen requerida'),
  subAlmacenDestinoId: z
    .string()
    .regex(UUID_SHAPE_RE, 'Sub-almacén destino requerido'),
  fechaMovimiento: z
    .string()
    .regex(DATE_ONLY_RE, 'Fecha en formato YYYY-MM-DD'),
  estadoMaterial: z
    .string()
    .min(1, 'Estado del material requerido')
    .max(100, 'Máximo 100 caracteres'),
  motivo: z
    .string()
    .min(1, 'Motivo requerido')
    .max(500, 'Máximo 500 caracteres'),
  observaciones: z.string().max(500, 'Máximo 500 caracteres').nullish(),
  lineas: z
    .array(
      z.object({
        lineaSalidaOrigenId: z
          .string()
          .regex(UUID_SHAPE_RE, 'Línea de salida origen requerida'),
        cantidadADevolver: z
          .number({ message: 'Cantidad requerida' })
          .positive('La cantidad debe ser mayor a cero'),
        // C7.2b: bin real destino (entrada) — obligatorio.
        ubicacionId: z
          .string()
          .regex(UUID_SHAPE_RE, 'Ubicación (rack) destino requerida'),
      }),
    )
    .min(1, 'Agrega al menos una línea'),
});

export type AplicarDevolucionInternaValues = z.infer<
  typeof AplicarDevolucionInternaSchema
>;

// ─── MAT-REV (A15) ──────────────────────────────────────────────────────────

const MatRevLineaSchema = z.object({
  articuloId: z.string().regex(UUID_SHAPE_RE, 'Artículo requerido'),
  // Solo frontend (no se envía): unidadMedidaId para validación advisory (2c).
  unidadMedidaId: z.string().regex(UUID_SHAPE_RE).nullish(),
  cantidad: z
    .number({ message: 'Cantidad requerida' })
    .positive('La cantidad debe ser mayor a cero'),
  // C7.2b: bin real. Opcional en el schema compartido — BajaPorDano (salida)
  // lo deja libre; Reincorporación (entrada) lo exige vía superRefine.
  ubicacionId: z.string().regex(UUID_SHAPE_RE).nullish(),
});

export const BajaPorDanoSchema = z.object({
  subAlmacenMatRevId: z
    .string()
    .regex(UUID_SHAPE_RE, 'Sub-almacén MAT-REV requerido'),
  fechaMovimiento: z
    .string()
    .regex(DATE_ONLY_RE, 'Fecha en formato YYYY-MM-DD'),
  motivo: z.string().min(1).max(500),
  lineas: z.array(MatRevLineaSchema).min(1, 'Agrega al menos una línea'),
});

export type BajaPorDanoValues = z.infer<typeof BajaPorDanoSchema>;

export const ReincorporacionTrasRevisionSchema = z
  .object({
    subAlmacenDestinoId: z
      .string()
      .regex(UUID_SHAPE_RE, 'Sub-almacén destino requerido'),
    fechaMovimiento: z
      .string()
      .regex(DATE_ONLY_RE, 'Fecha en formato YYYY-MM-DD'),
    motivo: z.string().min(1).max(500),
    lineas: z.array(MatRevLineaSchema).min(1, 'Agrega al menos una línea'),
  })
  // Entrada: cada línea exige bin real (BajaPorDano no).
  .superRefine((v, ctx) => {
    v.lineas.forEach((l, i) => {
      if (!l.ubicacionId) {
        ctx.addIssue({
          code: 'custom',
          path: ['lineas', i, 'ubicacionId'],
          message: 'Elige la ubicación (rack) destino.',
        });
      }
    });
  });

export type ReincorporacionTrasRevisionValues = z.infer<
  typeof ReincorporacionTrasRevisionSchema
>;

// ─── Devoluciones a proveedor (8.B) ─────────────────────────────────────────

export const IniciarDevolucionAProveedorSchema = z.object({
  proveedorId: z.string().regex(UUID_SHAPE_RE, 'Proveedor requerido'),
  motivo: z
    .string()
    .min(1, 'Motivo requerido')
    .max(500, 'Máximo 500 caracteres'),
  recepcionOrigenId: z.string().regex(UUID_SHAPE_RE).nullish(),
  facturaProveedorOrigenId: z.string().regex(UUID_SHAPE_RE).nullish(),
  ordenCompraOrigenId: z.string().regex(UUID_SHAPE_RE).nullish(),
  subAlmacenOrigenId: z.string().regex(UUID_SHAPE_RE).nullish(),
  lineas: z
    .array(
      z.object({
        articuloId: z.string().regex(UUID_SHAPE_RE, 'Artículo requerido'),
        cantidad: z
          .number({ message: 'Cantidad requerida' })
          .positive('La cantidad debe ser mayor a cero'),
        unidadMedida: z
          .string()
          .min(1, 'UM requerida')
          .max(20, 'Máximo 20 caracteres'),
        costoUnitarioMxn: z
          .number({ message: 'Costo requerido' })
          .nonnegative('El costo no puede ser negativo'),
        lineaRecepcionOrigenId: z.string().regex(UUID_SHAPE_RE).nullish(),
      }),
    )
    .min(1, 'Agrega al menos una línea'),
});

export type IniciarDevolucionAProveedorValues = z.infer<
  typeof IniciarDevolucionAProveedorSchema
>;

export const AdjuntarEvidenciaSchema = z.object({
  tipoEvidencia: z
    .string()
    .min(1, 'Tipo de evidencia requerido')
    .max(50, 'Máximo 50 caracteres'),
  comentario: z.string().max(500, 'Máximo 500 caracteres').nullish(),
});

export type AdjuntarEvidenciaValues = z.infer<typeof AdjuntarEvidenciaSchema>;

export const RechazarDevolucionSchema = z.object({
  motivoRechazo: z
    .string()
    .min(1, 'Motivo de rechazo requerido')
    .max(500, 'Máximo 500 caracteres'),
});

export type RechazarDevolucionValues = z.infer<typeof RechazarDevolucionSchema>;

export const RegistrarSalidaDevolucionSchema = z.object({
  subAlmacenId: z.string().regex(UUID_SHAPE_RE, 'Sub-almacén requerido'),
  fechaMovimiento: z
    .string()
    .regex(DATE_ONLY_RE, 'Fecha en formato YYYY-MM-DD'),
});

export type RegistrarSalidaDevolucionValues = z.infer<
  typeof RegistrarSalidaDevolucionSchema
>;
