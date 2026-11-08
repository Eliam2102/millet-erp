import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { periodoKeys, type EstadoPeriodo } from './periodos';
import type { Poliza } from './polizas-types';

/**
 * CON-12 (86e3jvvme) · checklist de cierre. Mirror de `ChecklistCierre.cs` y `CierreAnual.cs` (enums como texto).
 * El cierre en sí sigue siendo `POST /periodos/{id}/cerrar`: ahí el backend exige el checklist (CA10.9).
 */
export type TipoPasoCierre = 'Automatico' | 'Manual';
export type AlcancePasoCierre = 'Mensual' | 'Anual';
export type TipoMarcaPasoCierre = 'Excepcion' | 'Confirmacion';
export type EstadoPasoChecklist = 'Cumple' | 'Pendiente' | 'NoAplica' | 'Excepcion' | 'Confirmado';

export interface MarcaPaso {
  id: string;
  tipo: TipoMarcaPasoCierre;
  motivo: string;
  usuarioNombre: string;
  registradaEn: string;
}

export interface PasoChecklist {
  pasoId: string;
  clave: string;
  nombre: string;
  descripcion: string;
  tipo: TipoPasoCierre;
  orden: number;
  obligatorio: boolean;
  estado: EstadoPasoChecklist;
  resumen: string;
  detalle: string[];
  ruta: string | null;
  bloquea: boolean;
  marca: MarcaPaso | null;
}

export interface CierreRegistrado {
  id: string;
  cerradoPor: string;
  cerradoEn: string;
  excepciones: number;
}

export interface ChecklistCierre {
  periodoId: string;
  periodo: string;
  nombre: string;
  estadoPeriodo: EstadoPeriodo;
  exigeChecklist: boolean;
  puedeCerrar: boolean;
  bloqueantes: number;
  excepciones: number;
  evaluadoEn: string;
  pasos: PasoChecklist[];
  cierres: CierreRegistrado[];
}

export interface CierreAnual {
  anio: number;
  ingresos: number;
  costosYGastos: number;
  resultado: number;
  tipo: 'Utilidad' | 'Pérdida';
  cuentasConSaldo: number;
  polizas: Poliza[];
}

/** Motivo de la excepción o de la confirmación (mismo rango que el backend). */
export const MOTIVO_MARCA_MINIMO = 10;
export const MOTIVO_MARCA_MAXIMO = 500;

const BASE = '/api/v1/contabilidad/cierre';

export const cierreKeys = {
  all: ['contabilidad', 'cierre'] as const,
  checklist: (periodoId: string) => ['contabilidad', 'cierre', 'checklist', periodoId] as const,
  registro: (cierreId: string) => ['contabilidad', 'cierre', 'registro', cierreId] as const,
};

function useInvalidar() {
  const qc = useQueryClient();
  return () =>
    Promise.all([
      qc.invalidateQueries({ queryKey: cierreKeys.all }),
      qc.invalidateQueries({ queryKey: periodoKeys.all }),
    ]);
}

/**
 * Aplica al checklist en caché la marca que acaba de registrar el servidor (QA O1): el paso cambia de estado al
 * instante, sin esperar a que se vuelva a evaluar todo el checklist. La consulta se refresca después.
 */
export function aplicarMarca(checklist: ChecklistCierre, clave: string, marca: MarcaPaso): ChecklistCierre {
  const pasos = checklist.pasos.map((p) =>
    p.clave === clave
      ? { ...p, marca, bloquea: false, estado: (marca.tipo === 'Excepcion' ? 'Excepcion' : 'Confirmado') as EstadoPasoChecklist }
      : p,
  );
  const bloqueantes = pasos.filter((p) => p.bloquea).length;
  return {
    ...checklist,
    pasos,
    bloqueantes,
    puedeCerrar: bloqueantes === 0,
    excepciones: pasos.filter((p) => p.estado === 'Excepcion').length,
  };
}

export function useChecklistCierre(periodoId: string | null) {
  return useQuery({
    queryKey: cierreKeys.checklist(periodoId ?? ''),
    enabled: !!periodoId,
    queryFn: async ({ signal }) =>
      (await apiRequest<ChecklistCierre>(`${BASE}/periodos/${periodoId}/checklist`, { signal })).data,
  });
}

/** Foto del checklist guardada en un cierre (CON-12-a). */
export function useCierreRegistrado(cierreId: string | null) {
  return useQuery({
    queryKey: cierreKeys.registro(cierreId ?? ''),
    enabled: !!cierreId,
    queryFn: async ({ signal }) =>
      (await apiRequest<ChecklistCierre>(`${BASE}/registros/${cierreId}`, { signal })).data,
  });
}

/** Excepción autorizada (pendiente que no bloquea) o confirmación de un paso manual. */
export function useMarcarPaso() {
  const qc = useQueryClient();
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: {
      periodoId: string;
      clave: string;
      tipo: TipoMarcaPasoCierre;
      motivo: string;
      idempotencyKey: string;
    }) =>
      (
        await apiRequest<MarcaPaso>(
          `${BASE}/periodos/${a.periodoId}/pasos/${a.clave}/${a.tipo === 'Excepcion' ? 'excepcion' : 'confirmacion'}`,
          { method: 'POST', body: { motivo: a.motivo.trim() }, idempotencyKey: a.idempotencyKey },
        )
      ).data,
    onSuccess: (marca, a) => {
      qc.setQueryData<ChecklistCierre>(cierreKeys.checklist(a.periodoId), (actual) =>
        actual ? aplicarMarca(actual, a.clave, marca) : actual,
      );
      // Sin esperar el refresco: el diálogo se cierra ya con el paso actualizado.
      void invalidar();
    },
  });
}

export function useRetirarMarca() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (marcaId: string) =>
      (await apiRequest<void>(`${BASE}/marcas/${marcaId}`, { method: 'DELETE' })).data,
    onSuccess: invalidar,
  });
}

/** Genera en el periodo 13 las pólizas (borrador) que cancelan resultados contra resultado del ejercicio. */
export function useCierreAnual() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { anio: number; idempotencyKey: string }) =>
      (await apiRequest<CierreAnual>(`${BASE}/anual`, { method: 'POST', body: { anio: a.anio }, idempotencyKey: a.idempotencyKey }))
        .data,
    onSuccess: invalidar,
  });
}
