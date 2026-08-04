import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, ifMatch } from '@/lib/api';
import {
  facturacionKeys,
  type ListarPedidosFiltros,
} from '@/features/facturacion/api/keys';
import type {
  BandejaScoped,
  ExcepcionImportacionItem,
  PedidoBandejaItem,
  PedidoComprobanteItem,
  PedidoFacturableDetalleResponse,
  PedidoFacturableLineaInput,
  PedidoFacturableResponse,
} from '@/features/facturacion/api/types';

const BASE = '/api/v1/facturacion/pedidos-facturables';

/**
 * <c>useListarPedidos(filtros)</c> — bandeja de pedidos facturables.
 * El backend devuelve la lista directa (no paginada); el filtro
 * <c>q</c> es client-side (folio/cliente) y no viaja al server.
 */
export function useListarPedidos(filtros: ListarPedidosFiltros) {
  return useQuery<BandejaScoped<PedidoBandejaItem>>({
    queryKey: facturacionKeys.pedidosList(filtros),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.estado != null) params.set('estado', String(filtros.estado));
      if (filtros.origen != null) params.set('origen', String(filtros.origen));
      if (filtros.offset != null) params.set('offset', String(filtros.offset));
      if (filtros.limit != null) params.set('limit', String(filtros.limit));
      if (filtros.alcance != null) params.set('alcance', filtros.alcance);
      const qs = params.toString();
      // CAJAS-PR6: expone el envelope {items, sinAsignarCount} completo
      // (badge "Sin asignar" [Decisión 12-B], solo caja.leer-todas).
      const { data } = await apiRequest<BandejaScoped<PedidoBandejaItem>>(
        qs ? `${BASE}?${qs}` : `${BASE}/`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>usePedido(id)</c> — detalle de un pedido facturable (B1). Captura el
 * ETag (mirror del `Version`) para el `If-Match` al editar; el consumidor
 * ve solo el `data` vía `select`.
 */
interface CachedPedido {
  data: PedidoFacturableDetalleResponse;
  etag: string | undefined;
}

export function usePedido(id: string | null | undefined) {
  return useQuery<CachedPedido, Error, PedidoFacturableDetalleResponse>({
    queryKey:
      id != null ? facturacionKeys.pedidoById(id) : ['facturacion', 'noop'],
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('usePedido invocado sin id');
      const { data, etag } = await apiRequest<PedidoFacturableDetalleResponse>(
        `${BASE}/${id}`,
        { signal },
      );
      return { data, etag };
    },
    select: (cached) => cached.data,
    enabled: id != null,
    staleTime: 0,
  });
}

/** <c>usePedidoComprobantes(id)</c> — historial de comprobantes del pedido (B5). */
export function usePedidoComprobantes(id: string | null | undefined) {
  return useQuery<PedidoComprobanteItem[]>({
    queryKey:
      id != null
        ? facturacionKeys.pedidoComprobantes(id)
        : ['facturacion', 'noop-comprobantes'],
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('usePedidoComprobantes invocado sin id');
      const { data } = await apiRequest<PedidoComprobanteItem[]>(
        `${BASE}/${id}/comprobantes`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
    staleTime: 0,
  });
}

/** Command del POST de pedido manual (mirror del backend). */
export interface CrearPedidoManualCommand {
  numeroPedido: string | null;
  sucursalId: string;
  clienteId: string;
  clienteNombre: string;
  canalVenta: number;
  comportamientoFiscal: number;
  moneda: string;
  obraId: number | null;
  obraNombre: string | null;
  comentarios: string | null;
  lineas: PedidoFacturableLineaInput[];
}

/**
 * <c>useCrearPedidoManual()</c> — POST de captura manual con
 * Idempotency-Key (ADR-0020). Invalida la familia entera del módulo al
 * éxito.
 */
export function useCrearPedidoManual() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: CrearPedidoManualCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<PedidoFacturableResponse>(`${BASE}/`, {
        method: 'POST',
        body: args.command,
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: facturacionKeys.pedidos() });
    },
  });
}

/**
 * <c>useExcepciones(soloPendientes)</c> — bandeja de excepciones de
 * importación A+W / Planta Pintura (B/F3).
 */
export function useExcepciones(soloPendientes: boolean) {
  return useQuery<ExcepcionImportacionItem[]>({
    queryKey: facturacionKeys.pedidosExcepcionesList(soloPendientes),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      params.set('soloPendientes', String(soloPendientes));
      const { data } = await apiRequest<ExcepcionImportacionItem[]>(
        `${BASE}/excepciones?${params.toString()}`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>useResolverExcepcion()</c> — marca una excepción como resuelta
 * (POST idempotente). Invalida la familia de excepciones.
 */
export function useResolverExcepcion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { id: string; idempotencyKey: string }) => {
      const { data } = await apiRequest<{ id: string; resuelto: boolean }>(
        `${BASE}/excepciones/${args.id}/resolver`,
        { method: 'POST', idempotencyKey: args.idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: facturacionKeys.pedidosExcepciones(),
      });
    },
  });
}

/** Body del PUT de edición (mirror de EditarPedidoFacturableRequest). */
export interface EditarPedidoCommand {
  clienteId: string;
  clienteNombre: string;
  canalVenta: number;
  comportamientoFiscal: number;
  moneda: string;
  obraId: number | null;
  obraNombre: string | null;
  comentarios: string | null;
  lineas: PedidoFacturableLineaInput[];
}

/**
 * <c>useEditarPedido()</c> — PUT de edición de un pedido manual con
 * concurrencia optimista (If-Match = versión actual, ADR-0012). El
 * backend devuelve 409 al choque y 428 si falta If-Match. No usa
 * Idempotency-Key (la operación lleva su propio candado por versión).
 */
export function useEditarPedido() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      version: number;
      command: EditarPedidoCommand;
    }) => {
      const { data } = await apiRequest<PedidoFacturableResponse>(
        `${BASE}/${args.id}`,
        {
          method: 'PUT',
          body: args.command,
          headers: ifMatch(String(args.version)),
        },
      );
      return data;
    },
    onSuccess: (_data, args) => {
      queryClient.invalidateQueries({ queryKey: facturacionKeys.pedidos() });
      queryClient.invalidateQueries({
        queryKey: facturacionKeys.pedidoById(args.id),
      });
    },
  });
}
