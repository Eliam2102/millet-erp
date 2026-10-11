import { TipoDocumentoTrazabilidad } from './types';

export function formatearFechaDocumento(fecha: string, tipo: TipoDocumentoTrazabilidad): string {
  // La factura de proveedor guarda una fecha de calendario: no convertirla como instante UTC.
  if (tipo === TipoDocumentoTrazabilidad.FacturaProveedor) {
    return new Date(`${fecha.slice(0, 10)}T00:00:00Z`).toLocaleDateString('es-MX', { timeZone: 'UTC' });
  }
  return new Date(fecha).toLocaleDateString('es-MX', { timeZone: 'America/Mexico_City' });
}
