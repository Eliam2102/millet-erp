import { z } from 'zod';
import { hoyLocalISO } from '@/lib/datetime';

/** Fecha de calendario YYYY-MM-DD (DateOnly del backend, ADR-0040). */
const FECHA_DATE_ONLY_RE = /^\d{4}-\d{2}-\d{2}$/;

/**
 * Regex laxo de UUID — formato 8-4-4-4-12 hex sin requerir version
 * válida. Razón documentada en
 * <c>features/compras/schemas/crear-requisicion.ts</c>: el backend usa
 * GUIDs deterministas en seeds (ej.
 * <c>00000005-0001-0000-0000-000000000001</c>) que NO califican como
 * UUID v1-v8 estricto pero son válidos como string opaco.
 */
const UUID_SHAPE_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const idLike = (msg: string) =>
  z.string().regex(UUID_SHAPE_RE, msg);

const idLikeOpcional = z.string().regex(UUID_SHAPE_RE).nullish();

/**
 * Schema Zod del comando <c>POST /api/v1/compras/ordenes</c> (UF2-PR1
 * — Sheet "Nueva OC"). Mirror del shape esperado por el backend
 * <c>CrearOrdenCompraVaciaCommand</c> (cabecera-only — las líneas se
 * agregan después en el detalle desde UF2-PR3).
 *
 * <para><b>Solo validación estructural</b>: shape de UUIDs, required
 * vs nullable, longitud máxima, regex de SucursalCodigo. Las reglas
 * de negocio (proveedor activo, FOC11 permiso crear-sin-rq, etc.) las
 * decide el backend.</para>
 *
 * <para>Campos derivados que el form NO pide al usuario y agrega el
 * caller: <c>sucursalCodigo</c> (clave de la sucursal seleccionada,
 * lookup desde useSucursales) y <c>folioAnio</c> (año actual).</para>
 *
 * <para><b>Campos NO en este schema</b> (los rellena el handler desde
 * el JWT): <c>EmpresaId</c>, <c>CompradorTitularId</c>. Cabe destacar
 * que <c>EncargadoComprasId</c> sí es opcional acá: si null, el
 * handler usa el current user.</para>
 */
export const CrearOrdenCompraVaciaSchema = z
  .object({
    // ---- Cabecera obligatoria ----
    sucursalDestinoId: idLike('Selecciona una sucursal de destino.'),
    proveedorId: idLike('Selecciona un proveedor.'),
    condicionesPagoId: idLike('Selecciona condiciones de pago.'),
    usoPrincipalId: idLike('Selecciona un uso principal.'),
    fechaDocumento: z
      .string()
      .regex(FECHA_DATE_ONLY_RE, 'La fecha del documento es requerida (YYYY-MM-DD).'),
    // Defaults se proveen vía useForm({ defaultValues: ... }) en el
    // caller (DEFAULT_CREAR_OC_VACIA abajo) — sin <c>.default()</c> en
    // el schema para que el input type del Zod resolver coincida con
    // el output type, evitando incompatibilidades con
    // <c>useForm&lt;CrearOrdenCompraVaciaValues&gt;</c>.
    moneda: z
      .string()
      .regex(/^[A-Z]{3}$/, 'La moneda debe ser un código ISO 4217 (3 letras).'),
    tipoCambio: z
      .number()
      .positive('El tipo de cambio debe ser mayor a 0.')
      .nullish(),
    sinRequisicionPrevia: z.boolean(),
    esImportacion: z.boolean(),
    cotizacionExcepcionada: z.boolean(),
    observaciones: z
      .string()
      .max(2000, 'Máximo 2000 caracteres.')
      .nullish(),
    motivoSinRequisicion: z
      .string()
      .max(1000, 'Máximo 1000 caracteres.')
      .nullish(),
    fechaEntregaEsperada: z
      .string()
      .regex(FECHA_DATE_ONLY_RE, 'Formato de fecha inválido (YYYY-MM-DD).')
      .nullish(),
    encargadoComprasId: idLikeOpcional,
    ocOrigenId: idLikeOpcional,
  })
  // Reglas de coherencia que validan combinaciones de campos.
  .superRefine((data, ctx) => {
    // Si moneda ≠ MXN, tipo de cambio es obligatorio y > 0. El backend
    // tiene CHECK constraint ck_oc_tipo_cambio que enforce esto a
    // nivel BD, pero validamos también en frontend para feedback
    // inmediato.
    if (data.moneda !== 'MXN') {
      if (data.tipoCambio == null) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ['tipoCambio'],
          message: 'Tipo de cambio requerido para monedas distintas a MXN.',
        });
      }
    }
    // Si sinRequisicionPrevia=true, motivoSinRequisicion es
    // obligatorio (CHECK constraint ck_oc_sin_rq_motivo backend +
    // FOC11 frontend). El permiso `compras.ordenes.crear-sin-rq`
    // queda gateado en el toggle UI.
    if (data.sinRequisicionPrevia) {
      if (
        data.motivoSinRequisicion == null ||
        data.motivoSinRequisicion.trim().length === 0
      ) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ['motivoSinRequisicion'],
          message:
            'Captura el motivo por el que esta OC no tiene requisición previa.',
        });
      }
    }
  });

/** Tipo inferido — los valores que el form maneja. */
export type CrearOrdenCompraVaciaValues = z.infer<
  typeof CrearOrdenCompraVaciaSchema
>;

/**
 * Default puro del form (todos los opcionales en su zero-state). Útil
 * para inicializar react-hook-form y para tests.
 */
export const DEFAULT_CREAR_OC_VACIA: CrearOrdenCompraVaciaValues = {
  sucursalDestinoId: '',
  proveedorId: '',
  condicionesPagoId: '',
  usoPrincipalId: '',
  fechaDocumento: hoyLocalISO(),
  moneda: 'MXN',
  tipoCambio: null,
  sinRequisicionPrevia: false,
  esImportacion: false,
  cotizacionExcepcionada: false,
  observaciones: null,
  motivoSinRequisicion: null,
  fechaEntregaEsperada: null,
  encargadoComprasId: null,
  ocOrigenId: null,
};
