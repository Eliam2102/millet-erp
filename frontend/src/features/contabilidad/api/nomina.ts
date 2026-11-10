import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import type { CuerpoImportacion } from './types';
import type { Poliza } from './polizas-types';

/**
 * K10.11 · Póliza de la nómina externa por archivo.
 * Espejo de `ImportacionNomina.cs` (POST /api/v1/contabilidad/polizas/nomina[/vista-previa]).
 */
export interface ImportacionNominaRequest {
  tipoOperacionId: string;
  fecha: string;
  referencia: string;
  concepto: string;
  totalControl: number | null;
  archivo: CuerpoImportacion;
}

export interface ErrorImportacionNomina {
  /** Fila del archivo con la cabecera = 1; null = error del lote (cuadre, total, periodo, duplicado). */
  fila: number | null;
  campo: string;
  codigo: string;
  mensaje: string;
}

export interface RenglonNominaVista {
  fila: number;
  cuenta: string;
  cuentaNombre: string | null;
  cargo: number;
  abono: number;
  sucursal: string | null;
  centro: string | null;
  concepto: string | null;
}

export interface VistaPreviaNomina {
  valida: boolean;
  renglones: number;
  cargos: number;
  abonos: number;
  cuadra: boolean;
  errores: ErrorImportacionNomina[];
  partidas: RenglonNominaVista[];
  columnasNoReconocidas: string[];
}

const BASE = '/api/v1/contabilidad/polizas/nomina';

export function useVistaPreviaNomina() {
  return useMutation({
    mutationFn: async (body: ImportacionNominaRequest) =>
      (await apiRequest<VistaPreviaNomina>(`${BASE}/vista-previa`, { method: 'POST', body })).data,
  });
}

export function useImportarNomina() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (a: { body: ImportacionNominaRequest; idempotencyKey: string }) =>
      (await apiRequest<Poliza>(BASE, { method: 'POST', body: a.body, idempotencyKey: a.idempotencyKey })).data,
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['contabilidad', 'polizas'] });
    },
  });
}
