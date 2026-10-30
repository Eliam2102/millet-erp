import { z } from 'zod';
import { FraccionArancelariaSchema } from '@/features/compras/ordenes/schemas/comercio-exterior';

export const ImportacionRecepcionSchema = z.object({
  numeroPedimento: z.string().refine((v) => /^[0-9]{15}$/.test(v.replaceAll(' ', '')), 'Captura el pedimento de 15 dígitos.'),
  numeroBl: z.string().trim().min(1, 'Captura el BL o guía aérea.').max(50),
  numeroContenedor: z.string().trim().min(1, 'Captura el número de contenedor.').max(40),
  aduana: z.string().min(1, 'Selecciona la aduana.'),
  clavePedimento: z.string().min(1, 'Selecciona la clave de pedimento.'),
});
export type ImportacionRecepcionValues = z.infer<typeof ImportacionRecepcionSchema>;


/**
 * Regex laxo de UUID (8-4-4-4-12 hex) — los seeds del backend usan
 * GUIDs deterministas que no califican como v1-v8 estricto.
 */
const UUID_SHAPE_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * DateOnly serializado <c>YYYY-MM-DD</c> (sin tiempo, sin zona).
 * Backend lo parsea con <c>DateOnly.Parse</c>.
 */
const DATE_ONLY_RE = /^\d{4}-\d{2}-\d{2}$/;

/**
 * Schema de una fila de recepción derivada de una línea de OC. La fila
 * es read-only excepto por <c>incluida</c>, <c>cantidad</c>,
 * <c>ubicacionReferencia</c> y <c>comentario</c>. El resto viene del
 * detalle de OC y se persiste en el form solo para validación y
 * visualización (artículo, UM, solicitada, ya recibida, pendiente).
 */
export const FilaRecepcionSchema = z
  .object({
    fraccionArancelaria: FraccionArancelariaSchema.nullish(),
    esActivoFijo: z.boolean().optional(),
    lineaOcId: z.string().regex(UUID_SHAPE_RE),
    articuloId: z.string().regex(UUID_SHAPE_RE),
    // Etiqueta legible resuelta en backend (ADR-0042); null → id truncado.
    // Solo visualización: no viaja en el comando de registro.
    articuloClave: z.string().nullish(),
    articuloNombre: z.string().nullish(),
    posicion: z.number().int(),
    unidadMedida: z.string(),
    unidadCapturada: z.string().max(20).nullish(),
    cantidadSolicitada: z.number(),
    cantidadYaRecibida: z.number(),
    pendiente: z.number(),
    // Máximo recibible del servidor (LineaRecibible): igual al pendiente;
    // en piezas no hay tolerancia, Almacén recibe exacto.
    maximo: z.number(),
    incluida: z.boolean(),
    cantidad: z
      .number({ message: 'Cantidad requerida' })
      .nonnegative('La cantidad no puede ser negativa'),
    ubicacionReferencia: z.string().max(100, 'Máximo 100 caracteres').nullish(),
    // C7.2b: bin real destino (rack). Obligatorio en las filas incluidas.
    ubicacionId: z.string().regex(UUID_SHAPE_RE).nullish(),
    comentario: z.string().max(500, 'Máximo 500 caracteres').nullish(),
    // F1-MP-01 (E1): medida de hoja fijada por la línea de OC (solo lectura). Con medida, `cantidad` y `cantidadDanada` van en hojas enteras.
    medida: z
      .object({
        varianteId: z.string(),
        anchoMm: z.number(),
        altoMm: z.number(),
        areaHojaM2: z.number(),
      })
      .nullish(),
    // Línea de vidrio de la OC sin medida (OC previa a la migración): la fila no se puede incluir.
    requiereMedida: z.boolean().nullish(),
    // ALM-04: parte de la cantidad que llegó dañada; va a material en revisión.
    cantidadDanada: z.number().nonnegative('La cantidad dañada no puede ser negativa'),
    motivoDano: z.string().max(500, 'Máximo 500 caracteres').nullish(),
    // F1-MP-01 S4 (E3): lote del proveedor. Obligatorio con medida (artículo ligado a A+W; el servidor revalida con LOTE_REQUERIDO).
    lote: z.string().max(50, 'Máximo 50 caracteres').nullish(),
  })
  .superRefine((v, ctx) => {
    if (!v.incluida) return;
    if (v.medida && !v.lote?.trim()) {
      ctx.addIssue({ code: 'custom', path: ['lote'], message: 'Captura el lote del proveedor.' });
    }
    if (v.medida) {
      const hojasMax = Math.floor(v.pendiente / v.medida.areaHojaM2 + 1e-6);
      const mensaje = !Number.isInteger(v.cantidad) || v.cantidad <= 0
        ? 'Con medida, captura la cantidad en hojas enteras.'
        : !Number.isInteger(v.cantidadDanada) || v.cantidadDanada > v.cantidad
          ? 'Las hojas dañadas son enteras y no pueden exceder las recibidas.'
          : v.cantidad > hojasMax
            ? `Excede lo pendiente de la orden de compra (${hojasMax} hojas). Almacén recibe exacto.`
            : null;
      if (mensaje) {
        ctx.addIssue({ code: 'custom', path: ['cantidad'], message: mensaje });
        return;
      }
    }
    if (v.cantidad <= 0) {
      ctx.addIssue({
        code: 'custom',
        path: ['cantidad'],
        message: 'La cantidad debe ser mayor a cero.',
      });
      return;
    }
    if (v.cantidadDanada > v.cantidad) {
      ctx.addIssue({
        code: 'custom',
        path: ['cantidadDanada'],
        message: 'La cantidad dañada no puede ser mayor que la cantidad recibida.',
      });
    }
    if (v.cantidadDanada > 0 && !v.motivoDano?.trim()) {
      ctx.addIssue({
        code: 'custom',
        path: ['motivoDano'],
        message: 'Indica el motivo del daño.',
      });
    }
    if (v.esActivoFijo && (!Number.isInteger(v.cantidad) || v.cantidadDanada !== 0)) {
      ctx.addIssue({
        code: 'custom',
        path: ['cantidad'],
        message: 'Recibe unidades enteras de activos, sin cantidad dañada.',
      });
    }
    if (!v.esActivoFijo && !v.ubicacionId) {
      ctx.addIssue({
        code: 'custom',
        path: ['ubicacionId'],
        message: v.medida ? 'Elige el rack destino.' : 'Elige el almacén general destino.',
      });
    }
    if (!v.medida && !v.unidadCapturada?.trim() && v.cantidad > v.pendiente) {
      ctx.addIssue({
        code: 'custom',
        path: ['cantidad'],
        message: `Excede lo pendiente de la orden de compra (${Number(v.pendiente.toFixed(4))}). Almacén recibe exacto.`,
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
    importacion: ImportacionRecepcionSchema.nullish(),
    ordenCompraId: z.string().regex(UUID_SHAPE_RE, 'OC requerida'),
    fechaMovimiento: z.string().regex(DATE_ONLY_RE, 'Fecha en formato YYYY-MM-DD'),
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
        message: 'Vincula el CFDI del proveedor o captura su folio fiscal (UUID del impreso).',
      });
    }
  });

export type RegistrarRecepcionFacturaValues = z.infer<typeof RegistrarRecepcionFacturaSchema>;

/**
 * Variante B — recepción con packing list, factura pendiente
 * (materiales directos no-vidrio). Mirror de
 * <c>RegistrarRecepcionConPackingListCommand</c>.
 */
export const RegistrarRecepcionPackingListSchema = z
  .object({
    importacion: ImportacionRecepcionSchema.nullish(),
    ordenCompraId: z.string().regex(UUID_SHAPE_RE, 'OC requerida'),
    fechaMovimiento: z.string().regex(DATE_ONLY_RE, 'Fecha en formato YYYY-MM-DD'),
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
