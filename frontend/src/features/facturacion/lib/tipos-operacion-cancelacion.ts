/** M-235: tipos de operación (tipo de comprobante) a los que aplica una regla de firmas de cancelación. */
export const TIPOS_OPERACION_CANCELACION: ReadonlyArray<{ value: string; label: string }> = [
  { value: '', label: 'Cualquier operación' },
  { value: 'FacturaVenta', label: 'Factura de venta' },
  { value: 'FacturaAnticipo', label: 'Factura de anticipo' },
  { value: 'NotaCredito', label: 'Nota de crédito' },
  { value: 'ReciboPago', label: 'Complemento de pago' },
  { value: 'CartaPorte', label: 'Carta Porte' },
];
