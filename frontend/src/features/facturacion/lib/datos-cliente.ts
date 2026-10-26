export interface DatosCliente {
  ordenCompraCliente?: string | null;
  referenciaCliente?: string | null;
  condicionesPago?: string | null;
  proforma?: string | null;
  observaciones?: string | null;
}

export const camposDatosCliente = [
  ['ordenCompraCliente', 'Orden de compra del cliente', 60],
  ['referenciaCliente', 'Referencia del cliente', 60],
  ['condicionesPago', 'Condiciones de pago', 60],
  ['proforma', 'Proforma', 40],
  ['observaciones', 'Observaciones', 1000],
] as const;

