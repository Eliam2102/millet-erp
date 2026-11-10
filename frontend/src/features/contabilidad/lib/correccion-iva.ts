import type { DocumentoLiquidacionIva } from '../api/liquidacion-iva';

/** M-209 (CA4): solo las facturas de proveedor con CFDI se recalculan contra su XML (una por saldo). */
export function documentosCorregibles(documentos: DocumentoLiquidacionIva[]): DocumentoLiquidacionIva[] {
  const vistos = new Set<string>();
  return documentos.filter((d) => {
    if (d.lado !== 'Acreditable' || d.origenTipo !== 'factura_proveedor' || !d.uuid || vistos.has(d.saldoId)) return false;
    vistos.add(d.saldoId);
    return true;
  });
}

/** El motivo de la corrección lleva de 10 a 500 caracteres (igual que la anulación). */
export const motivoCorreccionValido = (motivo: string) => motivo.trim().length >= 10 && motivo.trim().length <= 500;
