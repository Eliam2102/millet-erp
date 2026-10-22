import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { AF_BASE, afKeys } from './hooks';
export function useConservacion<T>(ruta: string, enabled = true) {
  return useQuery({
    queryKey: [...afKeys.all, ruta],
    enabled,
    queryFn: async ({ signal }) => (await apiRequest<T>(`${AF_BASE}/${ruta}`, { signal })).data,
  });
}
export const tiposDocumento = [
  'Factura de compra',
  'Póliza de seguro',
  'Garantía',
  'Contrato de arrendamiento',
  'Pedimento',
  'Manual',
  'Fotografía',
  'Otro',
];
export interface DocumentoActivo {
  id: string;
  tipo: number;
  adjuntoId: string;
  folio: string;
  emisor: string;
  fechaEmision: string;
  vigenteDesde: string | null;
  vigenteHasta: string | null;
  notas: string | null;
  numeroVersion: number;
  reemplazado: boolean;
  reemplazaA: string | null;
}
export interface Mantenimiento {
  id: string;
  fecha: string;
  tipo: number;
  proveedorId: string;
  descripcion: string;
  costo: number;
  facturaProveedorId: string | null;
  proximo: string | null;
}
export const resultadosConteo = [
  'Pendiente',
  'Encontrado',
  'No encontrado',
  'Encontrado en otra ubicación',
  'Sobrante',
];
export interface PartidaConteo {
  id: string;
  numero: string | null;
  descripcionEsperada: string;
  ubicacionEsperada: string | null;
  resultado: number;
  ubicacionReal: string | null;
  evidencia: string | null;
  capturadoPor: string | null;
  autorizadoPor: string | null;
  evidenciaAutorizacion: string | null;
  propuesta: string | null;
}
export interface Conteo {
  id: string;
  version: number;
  sucursalId: string;
  ubicacion: string | null;
  createdAt: string;
  cerradoEn: string | null;
  partidas: PartidaConteo[];
}
export interface Pagina<T> {
  items: T[];
  total: number;
}
export interface Vencimiento {
  id: string;
  activoId: string;
  numero: string;
  descripcion: string;
  referencia: string;
  vence: string;
  esMantenimiento: boolean;
}
export interface Etiqueta {
  id: string;
  version: number;
  numero: string;
  descripcion: string;
  sucursal: string;
  empresa: string;
}
