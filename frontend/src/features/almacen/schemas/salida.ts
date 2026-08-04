import { z } from 'zod';

const UUID_SHAPE_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE_ONLY_RE = /^\d{4}-\d{2}-\d{2}$/;

/**
 * Schema de una línea de salida — compartido por Variante A (con RQ)
 * y B (vale). Mirror de <c>RegistrarSalidaLineaInput</c>.
 *
 * <para>Para Variante A las líneas vienen pre-pobladas desde el
 * detalle de la RQ y son read-only excepto por <c>incluida</c>,
 * <c>cantidad</c>, <c>centroCostoId</c>, <c>proyectoId</c>,
 * <c>ubicacionReferencia</c> y <c>comentario</c>. Para Variante B
 * (vale urgente) el almacenista las captura libremente (sin RQ origen
 * por diseño — A14).</para>
 */
export const LineaSalidaSchema = z.object({
  articuloId: z.string().regex(UUID_SHAPE_RE, 'Artículo requerido'),
  // Solo frontend (no se envía al backend): el unidadMedidaId del artículo
  // seleccionado, para la validación advisory de decimales (ADR-0046 2c).
  unidadMedidaId: z.string().regex(UUID_SHAPE_RE).nullish(),
  lineaRqId: z.string().regex(UUID_SHAPE_RE).nullish(),
  cantidad: z
    .number({ message: 'Cantidad requerida' })
    .positive('La cantidad debe ser mayor a cero'),
  centroCostoId: z.string().regex(UUID_SHAPE_RE).nullish(),
  proyectoId: z.string().regex(UUID_SHAPE_RE).nullish(),
  ubicacionReferencia: z.string().max(100, 'Máximo 100 caracteres').nullish(),
  // C7.2b: bin real de salida (rack con saldo o la ÚNICA). Obligatorio: el
  // almacenista elige de dónde sale.
  ubicacionId: z
    .string()
    .regex(UUID_SHAPE_RE, 'Elige la ubicación de donde sale'),
  comentario: z.string().max(500, 'Máximo 500 caracteres').nullish(),
});

export type LineaSalidaValues = z.infer<typeof LineaSalidaSchema>;

/**
 * Fila de salida derivada de una línea de RQ (Variante A). Carrea el
 * snapshot de la línea de RQ (artículo, UM, planeado almacén, ya
 * surtido, pendiente) para visualizar; sólo los campos editables se
 * envían al backend.
 *
 * <para>Validaciones cuando <c>incluida = true</c>:</para>
 * <list>
 *   <item><c>cantidad > 0</c>.</item>
 *   <item><c>cantidad ≤ pendienteEntregar</c> — no se puede surtir más
 *   de lo planeado/pendiente.</item>
 * </list>
 */
export const FilaSalidaSchema = z
  .object({
    lineaRqId: z.string().regex(UUID_SHAPE_RE),
    articuloId: z.string().regex(UUID_SHAPE_RE),
    // Etiqueta legible resuelta en backend (ADR-0042); null → id truncado.
    // Solo visualización: no viaja en el comando de registro. Mismo patrón
    // que FilaRecepcionSchema (#506).
    articuloClave: z.string().nullish(),
    articuloNombre: z.string().nullish(),
    posicion: z.number().int(),
    unidadMedida: z.string(),
    cantidadSolicitada: z.number(),
    cantidadPlaneadaAlmacen: z.number(),
    cantidadYaEntregada: z.number(),
    pendienteEntregar: z.number(),
    incluida: z.boolean(),
    cantidad: z
      .number({ message: 'Cantidad requerida' })
      .nonnegative('La cantidad no puede ser negativa'),
    centroCostoId: z.string().regex(UUID_SHAPE_RE).nullish(),
    // Fase E PR5: solo display del CC heredado (read-only en filas de RQ).
    centroCostoClave: z.string().nullish(),
    centroCostoNombre: z.string().nullish(),
    proyectoId: z.string().regex(UUID_SHAPE_RE).nullish(),
    ubicacionReferencia: z
      .string()
      .max(100, 'Máximo 100 caracteres')
      .nullish(),
    // C7.2b: bin real de salida. Obligatorio en las filas incluidas.
    ubicacionId: z.string().regex(UUID_SHAPE_RE).nullish(),
    comentario: z.string().max(500, 'Máximo 500 caracteres').nullish(),
  })
  .superRefine((v, ctx) => {
    if (!v.incluida) return;
    if (v.cantidad <= 0) {
      ctx.addIssue({
        code: 'custom',
        path: ['cantidad'],
        message: 'La cantidad debe ser mayor a cero.',
      });
      return;
    }
    if (v.cantidad > v.pendienteEntregar) {
      ctx.addIssue({
        code: 'custom',
        path: ['cantidad'],
        message: `Excede el pendiente de entregar (${v.pendienteEntregar}).`,
      });
    }
    if (!v.ubicacionId) {
      ctx.addIssue({
        code: 'custom',
        path: ['ubicacionId'],
        message: 'Elige la ubicación de donde sale.',
      });
    }
  });

export type FilaSalidaValues = z.infer<typeof FilaSalidaSchema>;

const alMenosUnaFilaIncluida: (
  v: { filas: FilaSalidaValues[] },
  ctx: z.RefinementCtx,
) => void = (v, ctx) => {
  if (!v.filas.some((f) => f.incluida)) {
    ctx.addIssue({
      code: 'custom',
      path: ['filas'],
      message: 'Marca al menos una línea para entregar.',
    });
  }
};

/**
 * Variante A — salida normal con RQ. Mirror de
 * <c>RegistrarSalidaConRequisicionCommand</c>. Las filas vienen del
 * detalle de la RQ; el submit filtra <c>incluida=true</c> y mapea al
 * shape del backend.
 */
export const RegistrarSalidaConRqSchema = z
  .object({
    requisicionId: z.string().regex(UUID_SHAPE_RE, 'Requisición requerida'),
    // Salida-por-línea C2: el sub-almacén ya NO se pide en la cabecera — el
    // backend lo deriva del bin de cada línea (obligatorio). Sólo el vale
    // (variante B) conserva subAlmacenId.
    fechaMovimiento: z
      .string()
      .regex(DATE_ONLY_RE, 'Fecha en formato YYYY-MM-DD'),
    personaDestinatariaId: z.string().regex(UUID_SHAPE_RE).nullish(),
    observaciones: z.string().max(500, 'Máximo 500 caracteres').nullish(),
    // PR5: helper de cabecera — bin que el almacenista auto-aplica a las filas
    // vacías cuyo artículo tenga existencia ahí. Estado SÓLO del formulario:
    // NO viaja al backend (a diferencia del helper de recepción, que sí se
    // persiste en movimientos_inventario.ubicacion_helper_id desde PR4). Aquí
    // el dato que manda es el ubicacionId de cada fila; el helper es andamiaje
    // de captura y no tiene columna donde vivir.
    ubicacionHelperId: z.string().regex(UUID_SHAPE_RE).nullable().optional(),
    filas: z.array(FilaSalidaSchema),
  })
  .superRefine(alMenosUnaFilaIncluida);

export type RegistrarSalidaConRqValues = z.infer<
  typeof RegistrarSalidaConRqSchema
>;

/**
 * Variante B — vale urgente (A14). Mirror de
 * <c>RegistrarSalidaPorValeCommand</c>. Captura libre — no hay RQ
 * origen por diseño. Sigue usando <c>LineaSalidaSchema</c> sin
 * cambios (el almacenista escribe artículo + cantidad para cada
 * renglón).
 */
export const RegistrarSalidaPorValeSchema = z.object({
  // Salida-por-línea (vale): sin subAlmacenId — el backend lo deriva del bin de
  // cada línea (obligatorio). Igual que la salida-con-RQ.
  fechaMovimiento: z
    .string()
    .regex(DATE_ONLY_RE, 'Fecha en formato YYYY-MM-DD'),
  valeBlobRef: z
    .string()
    .min(1, 'Adjunto del vale requerido')
    .max(500, 'Máximo 500 caracteres'),
  personaDestinatariaId: z.string().regex(UUID_SHAPE_RE).nullish(),
  observaciones: z.string().max(500, 'Máximo 500 caracteres').nullish(),
  // Helper de cabecera "Ubicación por defecto" (form-only, NO viaja al backend);
  // mismo andamiaje que la variante A. El dato que manda es el ubicacionId de
  // cada línea.
  ubicacionHelperId: z.string().regex(UUID_SHAPE_RE).nullable().optional(),
  lineas: z.array(LineaSalidaSchema).min(1, 'Agrega al menos una línea'),
});

export type RegistrarSalidaPorValeValues = z.infer<
  typeof RegistrarSalidaPorValeSchema
>;

/**
 * Schema para regularizar un vale con RQ aprobada posterior.
 */
export const RegularizarValeSchema = z.object({
  rqRegularizadoraId: z.string().regex(UUID_SHAPE_RE, 'RQ requerida'),
});

export type RegularizarValeValues = z.infer<typeof RegularizarValeSchema>;
