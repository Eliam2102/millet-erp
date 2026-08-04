import {
  ComportamientoFiscal,
  type EmisorDefaultsResponse,
} from '@/features/facturacion/api/types';
import type { EmitirFacturaValues } from '@/features/facturacion/schemas/emitir-factura';
import type { EmitirFacturaPrefill } from './prefill';

/**
 * Línea vacía para "Agregar concepto" y el arranque del form. La tasa
 * arranca con el IVA default de la empresa (FAC-DET-PR3); sin default
 * configurado cae al 0.16 histórico.
 */
export function defaultLinea(
  tasaIvaDefault?: number | null,
): EmitirFacturaValues['lineas'][number] {
  return {
    productoId: null,
    claveProdServSat: '',
    descripcion: '',
    claveUnidadSat: 'H87',
    cantidad: 1,
    valorUnitario: 0,
    descuento: 0,
    objetoImp: '02',
    tasaIvaTraslado: tasaIvaDefault ?? 0.16,
    tasaRetencionIva: null,
    tasaRetencionIsr: null,
    requierePedimento: false,
    fraccionArancelaria: null,
    unidadAduana: null,
    cantidadAduana: null,
    valorUnitarioAduana: null,
    valorDolares: null,
    aplicaIva0: false,
    pesoUnitarioKg: null,
  };
}

export const VALORES_INICIALES: EmitirFacturaValues = {
  sucursalId: '',
  rfcEmisor: '',
  regimenFiscalEmisor: '',
  receptorRfc: '',
  receptorNombre: '',
  receptorRegimenFiscal: '',
  receptorCodigoPostal: '',
  receptorUsoCfdi: 'G03',
  receptorPais: 'MEX',
  metodoPago: 'PUE',
  formaPago: '01',
  moneda: 'MXN',
  tipoCambio: null,
  // Canal id 1 (seed histórico "Tienda Cancún"); si ya no existe en el
  // catálogo, <CanalVentaSelector/> lo normaliza al primer canal activo.
  canalVenta: 1,
  comportamientoFiscal: ComportamientoFiscal.MostradorInmediato,
  obraNombre: null,
  cceTipoOperacion: '2',
  cceIncoterm: null,
  cceTcDof: null,
  cceReceptorNumRegIdTrib: null,
  cceReceptorPaisResidencia: null,
  cceClaveDePedimento: null,
  cceCertificadoOrigen: false,
  cceReceptorDomicilioCalle: null,
  cceReceptorDomicilioEstado: null,
  cceReceptorDomicilioCodigoPostal: null,
  lineas: [defaultLinea()],
};

export function nullIfEmpty(v: string | null | undefined): string | null {
  const s = (v ?? '').trim();
  return s.length > 0 ? s : null;
}

export const numeroONull = (v: unknown): number | null => {
  if (v === '' || v == null) return null;
  const n = Number(v);
  return Number.isNaN(n) ? null : n;
};

/**
 * Defaults del form combinando el prefill del pedido (receptor/líneas,
 * FAC-UX-PR3) con los defaults del emisor (empresa/sucursal/IVA). La
 * precedencia efectiva de la tasa por línea (FAC-DET-PR3):
 * tasa de la línea del pedido > master del artículo (ya resuelta en el
 * detalle) > IVA default de la empresa > 0.16.
 */
export function valoresIniciales(
  prefill: EmitirFacturaPrefill | undefined,
  emisor: EmisorDefaultsResponse,
): EmitirFacturaValues {
  const base: EmitirFacturaValues = {
    ...VALORES_INICIALES,
    rfcEmisor: emisor.rfcEmisor,
    regimenFiscalEmisor: emisor.regimenFiscalEmisor,
    sucursalId: emisor.sucursalIdDefault ?? '',
    lineas: [defaultLinea(emisor.tasaIvaDefault)],
  };
  if (prefill == null) return base;
  const lineas =
    prefill.lineas && prefill.lineas.length > 0
      ? prefill.lineas.map((l) => ({
          productoId: l.productoId,
          claveProdServSat: l.claveProdServSat,
          descripcion: l.descripcion,
          claveUnidadSat: l.claveUnidadSat || 'H87',
          cantidad: l.cantidad,
          valorUnitario: l.valorUnitario,
          descuento: l.descuento,
          objetoImp: l.objetoImp ?? '02',
          tasaIvaTraslado: l.tasaIvaTraslado ?? emisor.tasaIvaDefault ?? 0.16,
          tasaRetencionIva: l.tasaRetencionIva ?? null,
          tasaRetencionIsr: l.tasaRetencionIsr ?? null,
          requierePedimento: l.requierePedimento,
          fraccionArancelaria: null,
          unidadAduana: null,
          cantidadAduana: null,
          valorUnitarioAduana: null,
          valorDolares: null,
          aplicaIva0: false,
          pesoUnitarioKg: null,
        }))
      : base.lineas;
  return {
    ...base,
    sucursalId: prefill.sucursalId ?? base.sucursalId,
    receptorNombre: prefill.receptorNombre ?? '',
    receptorRfc: prefill.receptorRfc ?? '',
    receptorRegimenFiscal: prefill.receptorRegimenFiscal ?? '',
    receptorCodigoPostal: prefill.receptorCodigoPostal ?? '',
    receptorUsoCfdi: prefill.receptorUsoCfdi ?? base.receptorUsoCfdi,
    metodoPago: prefill.metodoPago ?? base.metodoPago,
    formaPago: prefill.formaPago ?? base.formaPago,
    canalVenta: prefill.canalVenta ?? base.canalVenta,
    comportamientoFiscal:
      prefill.comportamientoFiscal ?? base.comportamientoFiscal,
    moneda: prefill.moneda ?? base.moneda,
    obraNombre: prefill.obraNombre ?? null,
    lineas,
  };
}
