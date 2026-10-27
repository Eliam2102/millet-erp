import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { AF_BASE, afKeys } from './hooks';
export interface Depreciacion {
  clase: string;
  sucursal: string;
  centroCosto: string;
  id: string;
  activoId: string;
  numero: string;
  claseId: string;
  sucursalId: string;
  centroCostoId: string;
  equipoId: string | null;
  base: number;
  tasa: number | null;
  observacion?: string | null;
  importe: number;
  acumuladaAntes: number;
  acumuladaDespues: number;
}
export interface Corrida {
  id: string;
  periodo: string;
  revision: number;
  version: number;
  anulada: boolean;
  motivoAnulacion: string | null;
  polizaId: string | null;
  detalles: Depreciacion[];
}
export interface Operacion {
  id: string;
  tipo: string;
  fecha: string;
  importe: number;
  motivo: string;
  solicitadoPor: string;
  evidencia: string | null;
  version: number;
  polizaId: string | null;
  anulada: boolean;
}
export interface FilaLibro {
  activoId: string;
  numero: string;
  clase: string;
  sucursalId: string;
  centroCostoId: string;
  equipoId: string | null;
  valor: number;
  acumulada: number;
  neto: number;
}
export interface Conciliacion {
  clases: {
    clase: string;
    valorLibro: number;
    acumuladaLibro: number;
    saldoActivo: number | null;
    saldoDepreciacion: number | null;
    diferenciaValor: number | null;
    diferenciaAcumulada: number | null;
    observacion: string | null;
  }[];
  activos: FilaLibro[];
  corridas: Corrida[];
  ajustes: Operacion[];
}
export interface CargaResultado {
  valida: boolean;
  errores: { renglon: number; codigo: string; mensaje: string; columna?: string | null }[];
  avisos?: { renglon: number; codigo: string; mensaje: string; columna?: string | null }[];
  cuadre: {
    clase: string;
    valor: number;
    acumulada: number;
    neto: number;
    valorSap: number | null;
    acumuladaSap: number | null;
    diferenciaValor: number | null;
    diferenciaAcumulada: number | null;
  }[];
  cargaId: string | null;
  idempotente: boolean;
}
export interface Configuracion {
  version: number;
  revalorizacionHabilitada: boolean;
  depreciarMismoMes: boolean;
  inicioDepreciacionConfirmado: boolean;
  cuentaUtilidadId: string | null;
  cuentaPerdidaId: string | null;
  cuentaVentaPuenteId: string | null;
  cuentaRevalorizacionId: string | null;
}
export function useLibroQuery<T>(ruta: string, enabled = true) {
  return useQuery({
    queryKey: [...afKeys.all, ruta],
    enabled,
    queryFn: async ({ signal }) => (await apiRequest<T>(`${AF_BASE}/${ruta}`, { signal })).data,
  });
}
export function descargarCsv(nombre: string, texto: string) {
  const url = URL.createObjectURL(new Blob([texto], { type: 'text/csv;charset=utf-8' }));
  const a = document.createElement('a');
  a.href = url;
  a.download = nombre;
  a.click();
  URL.revokeObjectURL(url);
}
