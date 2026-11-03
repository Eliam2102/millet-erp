import type { DatosSustitucionResponse } from '@/features/facturacion/api/types';
import type { EmitirFacturaPrefill } from '@/features/facturacion/components/emitir-factura/prefill';

/** Borrador de la sustituta (FAC-08b): los datos de la original en el formulario de emisión. */
export function prefillSustitucion(d: DatosSustitucionResponse): EmitirFacturaPrefill {
  return {
    sustituye: { id: d.sustituyeComprobanteId, folio: d.sustituyeFolio, uuid: d.sustituyeUuid },
    sucursalId: d.sucursalId,
    clienteId: d.clienteId ?? undefined,
    receptorNombre: d.receptorNombre,
    receptorRfc: d.receptorRfc,
    receptorRegimenFiscal: d.receptorRegimenFiscal,
    receptorCodigoPostal: d.receptorCodigoPostal,
    receptorUsoCfdi: d.receptorUsoCfdi,
    metodoPago: d.metodoPago,
    formaPago: d.formaPago,
    canalVenta: d.canalVenta ?? undefined,
    comportamientoFiscal: d.comportamientoFiscal,
    moneda: d.moneda,
    tipoCambio: d.tipoCambio,
    condicionesPago: d.condicionesPago,
    referenciaCliente: d.referenciaCliente,
    ordenCompraCliente: d.ordenCompraCliente,
    proforma: d.proforma,
    observaciones: d.observaciones,
    obraNombre: d.obraNombre,
    lineas: d.lineas,
  };
}
