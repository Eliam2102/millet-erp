import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, esConflictoConcurrencia, useBodyScopedIdempotencyKey } from '@/lib/api';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import type { AutorizarHojaBody, ConfirmarEntregaBody, CrearHojaSalidaBody, EntregaEvidenciaDto, EscaneoDto, EscaneosPagina, HojaSalidaDto, HojasSalidaPagina, PiezasPagina, ResultadoEscaneo, SalidasPendientesPagina, TraspasoDto, TraspasosPagina } from './types';

const BASE = '/api/v1/producto-terminado';

export const ptKeys = {
  all: ['producto-terminado'] as const,
};

function qs(params: Record<string, string | number | undefined | null>): string {
  const sp = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== null && v !== '') sp.set(k, String(v));
  }
  const s = sp.toString();
  return s ? `?${s}` : '';
}

export function usePiezas(f: { pedido?: string; sucursalId?: string; page?: number; pageSize?: number }) {
  return useQuery({
    queryKey: [...ptKeys.all, 'piezas', f],
    queryFn: async ({ signal }) =>
      (await apiRequest<PiezasPagina>(`${BASE}/piezas${qs(f)}`, { signal })).data,
  });
}

export function useEscaneos(f: { resultado?: ResultadoEscaneo; page?: number; pageSize?: number }) {
  return useQuery({
    queryKey: [...ptKeys.all, 'escaneos', f],
    queryFn: async ({ signal }) =>
      (await apiRequest<EscaneosPagina>(`${BASE}/escaneos${qs(f)}`, { signal })).data,
  });
}

export type EscaneoAccionRuta = 'reintentar' | 'descartar' | 'revisar';

export function useEscaneoAccion() {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  const conflict = useConflictDialog();
  return useMutation({
    mutationFn: ({ escaneo, ruta, body }: { escaneo: Pick<EscaneoDto, 'id' | 'version'>; ruta: EscaneoAccionRuta; body?: { motivo: string } }) =>
      apiRequest<EscaneoDto>(`${BASE}/escaneos/${escaneo.id}/${ruta}`, {
        method: 'POST',
        body,
        // apiRequest ya envuelve el valor entre comillas (If-Match: "<version>").
        ifMatch: String(escaneo.version),
        idempotencyKey: keyFor({ id: escaneo.id, version: escaneo.version, ruta, body }),
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ptKeys.all }),
    onError: (error) => {
      if (esConflictoConcurrencia(error))
        conflict.openSimple({ onRefrescar: () => qc.invalidateQueries({ queryKey: ptKeys.all }) });
    },
  });
}

/** Pedidos en 70 que aún no salen; se refresca solo cada minuto. */
export function useSalidasPendientes(f: {
  sucursalId?: string;
  alerta?: '24h' | '48h';
  buscar?: string;
  page?: number;
  pageSize?: number;
}) {
  return useQuery({
    queryKey: [...ptKeys.all, 'salidas-pendientes', f],
    queryFn: async ({ signal }) =>
      (await apiRequest<SalidasPendientesPagina>(`${BASE}/salidas-pendientes${qs(f)}`, { signal })).data,
    refetchInterval: 60_000,
  });
}

export function useTraspasos(f: { estado?: string; page?: number; pageSize?: number }) {
  return useQuery({
    queryKey: [...ptKeys.all, 'traspasos', f],
    queryFn: async ({ signal }) =>
      (await apiRequest<TraspasosPagina>(`${BASE}/traspasos${qs(f)}`, { signal })).data,
  });
}

export function useTraspaso(id: string | null) {
  return useQuery({
    queryKey: [...ptKeys.all, 'traspaso', id],
    enabled: id !== null,
    queryFn: async ({ signal }) => (await apiRequest<TraspasoDto>(`${BASE}/traspasos/${id}`, { signal })).data,
  });
}

export function useCrearTraspaso() {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  return useMutation({
    mutationFn: (body: { sucursalOrigenId: string; sucursalDestinoId: string; etiquetas: string[] }) =>
      apiRequest<TraspasoDto>(`${BASE}/traspasos`, { method: 'POST', body, idempotencyKey: keyFor(body) }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ptKeys.all }),
  });
}

/** `piezaIds` ausente = recepción total; una lista parcial deja la diferencia pendiente. */
export function useRecibirTraspaso() {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  return useMutation({
    mutationFn: ({ id, piezaIds }: { id: string; piezaIds?: string[] }) =>
      apiRequest<TraspasoDto>(`${BASE}/traspasos/${id}/recibir`, {
        method: 'POST',
        body: piezaIds ? { piezaIds } : undefined,
        idempotencyKey: keyFor({ id, piezaIds }),
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ptKeys.all }),
  });
}

export function useHojasSalida(f: { estado?: string; page?: number; pageSize?: number }) {
  return useQuery({
    queryKey: [...ptKeys.all, 'hojas-salida', f],
    queryFn: async ({ signal }) =>
      (await apiRequest<HojasSalidaPagina>(`${BASE}/hojas-salida${qs(f)}`, { signal })).data,
  });
}

export function useHojaSalida(id: string | null) {
  return useQuery({
    queryKey: [...ptKeys.all, 'hoja-salida', id],
    enabled: id !== null,
    queryFn: async ({ signal }) => (await apiRequest<HojaSalidaDto>(`${BASE}/hojas-salida/${id}`, { signal })).data,
  });
}

export function useCrearHojaSalida() {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  return useMutation({
    mutationFn: async (body: CrearHojaSalidaBody) =>
      (await apiRequest<HojaSalidaDto>(`${BASE}/hojas-salida`, { method: 'POST', body, idempotencyKey: keyFor(body) })).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ptKeys.all }),
  });
}

/** Emitir una hoja ya emitida no cambia nada: la respuesta trae `yaEmitida`. El estado va en la llave para que ese reintento llegue al servidor y no repita la respuesta guardada. */
export function useAccionHojaSalida() {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  return useMutation({
    mutationFn: async ({ id, accion, estado }: { id: string; accion: 'emitir' | 'cancelar'; estado: string }) =>
      (await apiRequest<HojaSalidaDto>(`${BASE}/hojas-salida/${id}/${accion}`, { method: 'POST', idempotencyKey: keyFor({ id, accion, estado }) })).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ptKeys.all }),
  });
}

/** Reglas de salida 2 y 4: autoriza la hoja sin factura o la salida parcial. La llave lleva el cuerpo: reintentar lo mismo no registra otra. */
export function useAutorizarHojaSalida() {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: AutorizarHojaBody }) =>
      (await apiRequest<HojaSalidaDto>(`${BASE}/hojas-salida/${id}/autorizar`, { method: 'POST', body, idempotencyKey: keyFor({ id, body }) })).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ptKeys.all }),
  });
}

/** Carga física pieza por pieza (PT5.4). El intento va en la llave: tras un rechazo, escanear la misma etiqueta otra vez llega al servidor y no repite la respuesta guardada. */
export function useCargaHoja() {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  const [intento, setIntento] = useState(0);
  return useMutation({
    mutationFn: async ({ id, accion, etiqueta }: { id: string; accion: 'cargar' | 'descargar' | 'rota'; etiqueta: string }) =>
      (await apiRequest<HojaSalidaDto>(`${BASE}/hojas-salida/${id}/${accion}`, { method: 'POST', body: { etiqueta }, idempotencyKey: keyFor({ id, accion, etiqueta, intento }) })).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ptKeys.all }),
    onError: () => setIntento((n) => n + 1),
  });
}

/** Confirma la entrega de un albarán (PT5.6). La llave lleva el cuerpo: reenviar lo mismo (red inestable en el celular) no repite la confirmación. */
export function useConfirmarEntrega() {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  return useMutation({
    mutationFn: async ({ hojaId, albaranId, body }: { hojaId: string; albaranId: string; body: ConfirmarEntregaBody }) =>
      (await apiRequest<HojaSalidaDto>(`${BASE}/hojas-salida/${hojaId}/albaranes/${albaranId}/entrega`, { method: 'POST', body, idempotencyKey: keyFor({ hojaId, albaranId, body }) })).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ptKeys.all }),
  });
}

/** Firma y foto de una entrega: solo se piden al abrir la evidencia. */
export function useEvidenciaEntrega(hojaId: string, albaranId: string, activa: boolean) {
  return useQuery({
    queryKey: [...ptKeys.all, 'entrega-evidencia', hojaId, albaranId],
    enabled: activa,
    queryFn: async ({ signal }) => (await apiRequest<EntregaEvidenciaDto>(`${BASE}/hojas-salida/${hojaId}/albaranes/${albaranId}/entrega/evidencia`, { signal })).data,
  });
}
