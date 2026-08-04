import { z } from 'zod';
import { ComportamientoFiscal } from '@/features/facturacion/api/types';

/**
 * Schema del form "Emitir factura" (FE-F1-PR2). Mirror de
 * <c>EmitirFacturaVentaValidator</c> del backend. Es captura directa
 * (D del owner): receptor + emisor + pago + líneas con claves SAT y
 * tasas se capturan a mano (los puertos fiscales de cliente/producto
 * son NoOp en backend F1). El backend valida además FormaPago / UsoCfdi /
 * RegimenFiscalEmisor / Moneda contra los catálogos SAT.
 */
export const EmitirFacturaLineaSchema = z.object({
  productoId: z.string().uuid().nullable(),
  claveProdServSat: z.string().min(1, 'Clave SAT requerida').max(10),
  descripcion: z.string().min(1, 'Describe el concepto').max(1000),
  claveUnidadSat: z.string().min(1, 'Unidad SAT requerida').max(10),
  cantidad: z.number().positive('La cantidad debe ser mayor a 0'),
  valorUnitario: z.number().min(0, 'El valor no puede ser negativo'),
  descuento: z.number().min(0, 'El descuento no puede ser negativo'),
  objetoImp: z.string().min(1).max(2),
  tasaIvaTraslado: z.number().min(0).max(1).nullable(),
  tasaRetencionIva: z.number().min(0).max(1).nullable(),
  tasaRetencionIsr: z.number().min(0).max(1).nullable(),
  requierePedimento: z.boolean(),
  // Datos de aduana (CCE) — solo se exigen en exportación con CCE; la
  // validación de obligatoriedad la aplica el form según el comportamiento.
  fraccionArancelaria: z.string().max(13).nullable(),
  unidadAduana: z.string().max(10).nullable(),
  cantidadAduana: z.number().min(0).nullable(),
  valorUnitarioAduana: z.number().min(0).nullable(),
  valorDolares: z.number().min(0).nullable(),
  aplicaIva0: z.boolean(),
  // Carrier FE (no viaja al backend): peso unitario del artículo, base para
  // derivar la cantidad aduanera (peso × cantidad). Fase 1c.
  pesoUnitarioKg: z.number().min(0).nullable(),
});

export type EmitirFacturaLineaValues = z.infer<typeof EmitirFacturaLineaSchema>;

const comportamientoValues = Object.values(ComportamientoFiscal) as [
  number,
  ...number[],
];

export const EmitirFacturaSchema = z.object({
  sucursalId: z.string().uuid('Selecciona una sucursal'),
  // Emisor
  rfcEmisor: z.string().min(12, 'RFC del emisor').max(13),
  regimenFiscalEmisor: z.string().min(1, 'Régimen fiscal del emisor').max(5),
  // Receptor
  receptorRfc: z.string().min(12, 'RFC del receptor').max(13),
  receptorNombre: z.string().min(1, 'Nombre / razón social').max(254),
  receptorRegimenFiscal: z.string().min(1, 'Régimen fiscal del receptor').max(5),
  receptorCodigoPostal: z.string().min(5, 'Código postal').max(10),
  receptorUsoCfdi: z.string().min(1, 'Uso CFDI').max(5),
  receptorPais: z.string().min(3, 'País').max(5),
  // Pago
  metodoPago: z.string().min(1, 'Método de pago').max(5),
  formaPago: z.string().min(1, 'Forma de pago').max(5),
  moneda: z.string().length(3, 'Código ISO de 3 letras'),
  tipoCambio: z.number().positive().nullable(),
  // Generales
  // Canal de venta = id del catálogo administrable (FAC-ING-PR3); la
  // existencia/actividad la valida el backend contra la BD.
  canalVenta: z
    .number()
    .int('Selecciona un canal')
    .positive('Selecciona un canal'),
  comportamientoFiscal: z
    .number()
    .refine(
      (v) => comportamientoValues.includes(v),
      'Selecciona un comportamiento fiscal',
    ),
  obraNombre: z.string().max(254).nullable(),
  // Encabezado CCE (Comercio Exterior) — solo exportación con CCE.
  cceTipoOperacion: z.string().max(2).nullable(),
  cceIncoterm: z.string().max(10).nullable(),
  cceTcDof: z.number().min(0).nullable(),
  cceReceptorNumRegIdTrib: z.string().max(40).nullable(),
  cceReceptorPaisResidencia: z.string().max(5).nullable(),
  // F12-PR3 (CCE 2.0): clave de pedimento, certificado de origen y domicilio
  // del receptor extranjero. Estado/CP los exige el SAT para timbrar; el
  // superRefine de abajo los vuelve obligatorios solo en ExportacionConCce.
  cceClaveDePedimento: z.string().max(2).nullable(),
  cceCertificadoOrigen: z.boolean(),
  cceReceptorDomicilioCalle: z.string().max(200).nullable(),
  cceReceptorDomicilioEstado: z.string().max(30).nullable(),
  cceReceptorDomicilioCodigoPostal: z.string().max(12).nullable(),
  lineas: z.array(EmitirFacturaLineaSchema).min(1, 'Agrega al menos una línea'),
}).superRefine((values, ctx) => {
  // Matriz SAT método/forma de pago (CFDI40105, incidente COTT-2026-3):
  // PPD exige forma 99 "Por definir"; PUE exige la forma real del cobro.
  // El form auto-ajusta al cambiar el método; esto es la red de seguridad.
  if (values.metodoPago === 'PPD' && values.formaPago !== '99') {
    ctx.addIssue({
      code: 'custom',
      path: ['formaPago'],
      message: 'Con método PPD la forma de pago debe ser 99 — Por definir (SAT CFDI40105)',
    });
  }
  if (values.metodoPago === 'PUE' && values.formaPago === '99') {
    ctx.addIssue({
      code: 'custom',
      path: ['formaPago'],
      message: 'Con método PUE captura la forma de pago real del cobro (99 solo aplica a PPD)',
    });
  }

  // Moneda extranjera exige tipo de cambio (CFDI 4.0). Sin esto el backend
  // rechaza con 400; se valida aquí para dar error inline en el campo.
  if (
    values.moneda !== 'MXN' &&
    (values.tipoCambio == null || values.tipoCambio <= 0)
  ) {
    ctx.addIssue({
      code: 'custom',
      path: ['tipoCambio'],
      message: 'Con moneda distinta a MXN captura el tipo de cambio.',
    });
  }

  // Domicilio del receptor extranjero: requerido para timbrar CCE 2.0
  // (el backend rechaza con CCE_DOMICILIO_RECEPTOR_INCOMPLETO).
  if (values.comportamientoFiscal !== ComportamientoFiscal.ExportacionConCce) {
    return;
  }
  if ((values.cceReceptorDomicilioEstado ?? '').trim().length === 0) {
    ctx.addIssue({
      code: 'custom',
      path: ['cceReceptorDomicilioEstado'],
      message: 'Estado/provincia del receptor requerido para timbrar CCE',
    });
  }
  if ((values.cceReceptorDomicilioCodigoPostal ?? '').trim().length === 0) {
    ctx.addIssue({
      code: 'custom',
      path: ['cceReceptorDomicilioCodigoPostal'],
      message: 'Código postal del receptor requerido para timbrar CCE',
    });
  }

  // Datos de aduana por mercancía (CCE 2.0). Espejo de las reglas del SAT y
  // del backend (ComplementoCce.AgregarLinea) para no quemar folio contra el
  // rechazo del PAC (#8):
  //  - valorDolares > 0 siempre (nodo requerido; alimenta TotalUSD).
  //  - Bien tangible (unidad aduanera ≠ 99 y clave de unidad SAT ≠ E48, caso de
  //    Millet): fracción arancelaria de 8-10 dígitos + trío unidad/cantidad/
  //    valor unitario completo (SAT CCE160/CCE165).
  //  - Servicio / sin unidad (unidad aduanera 99 o clave SAT E48): la fracción
  //    NO debe registrarse (SAT CCE159).
  // La pertenencia a catálogos (c_FraccionArancelaria/c_UnidadAduana) la valida
  // el PAC.
  values.lineas.forEach((l, i) => {
    const unidadAduana = (l.unidadAduana ?? '').trim();
    const esServicioSinUnidad =
      unidadAduana === '99' || l.claveUnidadSat === 'E48';

    if (esServicioSinUnidad) {
      if ((l.fraccionArancelaria ?? '').trim().length > 0) {
        ctx.addIssue({
          code: 'custom',
          path: ['lineas', i, 'fraccionArancelaria'],
          message:
            'Sin unidad aduanera (99 / servicio) no se registra fracción arancelaria (SAT CCE159)',
        });
      }
    } else {
      const fraccion = (l.fraccionArancelaria ?? '').trim();
      if (fraccion.length === 0) {
        ctx.addIssue({
          code: 'custom',
          path: ['lineas', i, 'fraccionArancelaria'],
          message: 'Fracción arancelaria requerida en exportación',
        });
      } else if (!/^\d{8,10}$/.test(fraccion)) {
        ctx.addIssue({
          code: 'custom',
          path: ['lineas', i, 'fraccionArancelaria'],
          message: 'La fracción debe tener de 8 a 10 dígitos numéricos',
        });
      }
      if (unidadAduana.length === 0) {
        ctx.addIssue({
          code: 'custom',
          path: ['lineas', i, 'unidadAduana'],
          message: 'Unidad aduanera requerida (c_UnidadAduana)',
        });
      }
      if ((l.cantidadAduana ?? 0) <= 0) {
        ctx.addIssue({
          code: 'custom',
          path: ['lineas', i, 'cantidadAduana'],
          message: 'La cantidad aduanera debe ser mayor a 0',
        });
      }
      if ((l.valorUnitarioAduana ?? 0) <= 0) {
        ctx.addIssue({
          code: 'custom',
          path: ['lineas', i, 'valorUnitarioAduana'],
          message: 'El valor unitario aduanero debe ser mayor a 0',
        });
      }
    }

    if ((l.valorDolares ?? 0) <= 0) {
      ctx.addIssue({
        code: 'custom',
        path: ['lineas', i, 'valorDolares'],
        message: 'El valor USD debe ser mayor a 0',
      });
    }
  });
});

export type EmitirFacturaValues = z.infer<typeof EmitirFacturaSchema>;
