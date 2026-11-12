import type { ModalidadTramo, TramoTraslado } from '@/features/facturacion/api/types';

export const MODALIDADES_TRAMO: { value: ModalidadTramo; label: string }[] = [
  { value: 'VehiculoPropio', label: 'Vehículo propio (Millet emite Carta Porte)' },
  { value: 'TransportistaTercero', label: 'Transportista tercero (documento del tercero)' },
];

/** Texto del estado del tramo según su modalidad. */
export function textoEstadoTramo(t: Pick<TramoTraslado, 'estado' | 'cartaPorteFolio'>): string {
  if (t.estado === 'PorEmitir') return 'Carta Porte por emitir';
  if (t.estado === 'SinDocumentoTercero') return 'Falta el documento del tercero';
  if (t.estado === 'DocumentoTercero') return 'Documento del tercero registrado';
  return `Carta Porte ${t.cartaPorteFolio ?? ''} · ${t.estado.replace('CartaPorte', '')}`.trim();
}
