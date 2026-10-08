import type { EmitirFacturaVentaResponse } from '@/features/facturacion/api/types';

/** Un HTTP 201 acredita la creación del comprobante; el PAC puede rechazarlo. */
export function resultadoEmision(res: EmitirFacturaVentaResponse) {
  if (res.estado === 'Timbrado' && res.uuid?.trim()) {
    return {
      tipo: 'success' as const,
      titulo: `Factura ${res.folio} timbrada`,
      descripcion: `UUID ${res.uuid}`,
      reintentable: false,
    };
  }
  if (res.estado === 'TimbradoFallido') {
    return {
      tipo: 'error' as const,
      titulo: `No se pudo timbrar la factura ${res.folio}`,
      descripcion: `${res.timbradoErrorCodigo ?? 'Sin código del PAC'}: ${res.timbradoErrorMensaje ?? 'El PAC no proporcionó un mensaje.'}`,
      reintentable: true,
    };
  }
  return {
    tipo: 'warning' as const,
    titulo: `Factura ${res.folio} sin timbrado confirmado`,
    descripcion: res.estado === 'PendientePedimento'
      ? 'Completa el pedimento para continuar con el timbrado.'
      : 'El timbrado todavía no tiene un UUID confirmado. Consulta el comprobante.',
    reintentable: false,
  };
}
