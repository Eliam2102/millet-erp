import { z } from 'zod';

/**
 * Regex laxo de UUID (8-4-4-4-12 hex) — los seeds del backend usan
 * GUIDs deterministas que no califican como v1-v8 estricto.
 */
const UUID_SHAPE_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * DateOnly serializado <c>YYYY-MM-DD</c> (sin tiempo, sin zona).
 * Backend lo parsea con <c>DateOnly.Parse</c>.
 */
const DATE_ONLY_RE = /^\d{4}-\d{2}-\d{2}$/;

/**
 * Tolerancia client-side default para sobre-recepción (5%). El backend
 * revalida con la tolerancia real del artículo (<c>IArticuloReadPort</c>)
 * y rechaza si excede; el cliente usa este valor solo para feedback
 * inmediato al usuario.
 */
export const TOLERANCIA_RECEPCION_DEFAULT = 0.05;

/**
 * Schema de una fila de recepción derivada de una línea de OC. La fila
 * es read-only excepto por <c>incluida</c>, <c>cantidad</c>,
 * <c>ubicacionReferencia</c> y <c>comentario</c>. El resto viene del
 * detalle de OC y se persiste en el form solo para validación y
 * visualización (artículo, UM, solicitada, ya recibida, pendiente).
 */
export const FilaRecepcionSchema = z
  .object({
    lineaOcId: z.string().regex(UUID_SHAPE_RE),
    articuloId: z.string().regex(UUID_SHAPE_RE),
    // Etiqueta legible resuelta en backend (ADR-0042); null → id truncado.
    // Solo visualización: no viaja en el comando de registro.
    articuloClave: z.string().nullish(),
    articuloNombre: z.string().nullish(),
    posicion: z.number().int(),
    unidadMedida: z.string(),
    cantidadSolicitada: z.number(),
    cantidadYaRecibida: z.number(),
    pendiente: z.number(),
    incluida: z.boolean(),
    cantidad: z
      .number({ message: 'Cantidad requerida' })
      .nonnegative('La cantidad no puede ser negativa'),
    ubicacionReferencia: z
      .string()
      .max(100, 'Máximo 100 caracteres')
      .nullish(),
    // C7.2b: bin real destino (rack). Obligatorio en las filas incluidas.
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
    if (!v.ubicacionId) {
      ctx.addIssue({
        code: 'custom',
        path: ['ubicacionId'],
        message: 'Elige la ubicación (rack) destino.',
      });
    }
    const max = v.pendiente * (1 + TOLERANCIA_RECEPCION_DEFAULT);
    if (v.cantidad > max) {
      ctx.addIssue({
        code: 'custom',
        path: ['cantidad'],
        message: `Excede el pendiente (${v.pendiente}) + 5% de tolerancia (${max.toFixed(4)}).`,
      });
    }
  });

export type FilaRecepcionValues = z.infer<typeof FilaRecepcionSchema>;

/**
 * Refinement compartido por Variante A y B: al menos UNA fila debe
 * estar incluida en la recepción. Tipado explícito sin
 * <c>z.SuperRefinement</c> porque Zod v4 no lo exporta como tipo
 * nombrado (sólo el método <c>.superRefine()</c>).
 */
const alMenosUnaFilaIncluida = (
  v: { filas: FilaRecepcionValues[] },
  ctx: z.RefinementCtx,
): void => {
  if (!v.filas.some((f) => f.incluida)) {
    ctx.addIssue({
      code: 'custom',
      path: ['filas'],
      message: 'Marca al menos una línea para recibir.',
    });
  }
};

/**
 * Variante A — recepción con factura/CFDI ya conocido (insumos /
 * refacciones). Mirror de
 * <c>RegistrarRecepcionConFacturaCommand</c>.
 */
export const RegistrarRecepcionFacturaSchema = z
  .object({
    ordenCompraId: z.string().regex(UUID_SHAPE_RE, 'OC requerida'),
    fechaMovimiento: z
      .string()
      .regex(DATE_ONLY_RE, 'Fecha en formato YYYY-MM-DD'),
    // Vínculo fiscal obligatorio (§5.4): CFDI del repositorio de CxP o
    // folio fiscal (UUID SAT) capturado del impreso — al menos uno. El
    // refine de abajo lo exige; el backend revalida (RECEPCION_SIN_CFDI).
    cfdiRecibidoId: z.string().regex(UUID_SHAPE_RE).nullish(),
    cfdiUuidFiscal: z
      .string()
      .regex(UUID_SHAPE_RE, 'Folio fiscal con formato UUID (8-4-4-4-12 hex)')
      .nullish(),
    observaciones: z.string().max(500, 'Máximo 500 caracteres').nullish(),
    // PR4: helper de cabecera — bin N4 que el almacenista auto-aplica a las
    // filas sin ubicación. Opcional; null = no usó el helper. No sustituye a
    // ubicacionId por fila (que sigue siendo el dato que manda).
    ubicacionHelperId: z.string().regex(UUID_SHAPE_RE).nullish(),
    filas: z.array(FilaRecepcionSchema),
  })
  .superRefine(alMenosUnaFilaIncluida)
  .superRefine((v, ctx) => {
    if (!v.cfdiRecibidoId && !v.cfdiUuidFiscal) {
      ctx.addIssue({
        code: 'custom',
        path: ['cfdiRecibidoId'],
        message:
          'Vincula el CFDI del proveedor o captura su folio fiscal (UUID del impreso).',
      });
    }
  });

export type RegistrarRecepcionFacturaValues = z.infer<
  typeof RegistrarRecepcionFacturaSchema
>;

/**
 * Variante B — recepción con packing list, factura pendiente
 * (materiales directos no-vidrio). Mirror de
 * <c>RegistrarRecepcionConPackingListCommand</c>.
 */
export const RegistrarRecepcionPackingListSchema = z
  .object({
    ordenCompraId: z.string().regex(UUID_SHAPE_RE, 'OC requerida'),
    fechaMovimiento: z
      .string()
      .regex(DATE_ONLY_RE, 'Fecha en formato YYYY-MM-DD'),
    packingListBlobRef: z
      .string()
      .min(1, 'Adjunto del packing list requerido')
      .max(500, 'Máximo 500 caracteres'),
    observaciones: z.string().max(500, 'Máximo 500 caracteres').nullish(),
    // PR4: helper de cabecera (ver variante A).
    ubicacionHelperId: z.string().regex(UUID_SHAPE_RE).nullish(),
    filas: z.array(FilaRecepcionSchema),
  })
  .superRefine(alMenosUnaFilaIncluida);

export type RegistrarRecepcionPackingListValues = z.infer<
  typeof RegistrarRecepcionPackingListSchema
>;
