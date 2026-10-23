export interface BorradorCfdi {
  tipo: 'Factura' | 'Anticipo' | 'REP';
  emisor: string;
  receptor: string;
  usoCfdi: string;
  metodoPago: string;
  formaPago: string;
  moneda: string;
  partidas: { descripcion: string; cantidad: number; importe: number }[];
  importes: { etiqueta: string; monto: number }[];
}
