import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

/** Línea de lo que recibe Almacenes en COMPRA_RECIBIDA / COMPRA_MODIFICADA (contrato v6 §9). */
export interface AlmCompraLineaDatos {
  lineaErp: string;
  articuloAw: number;
  ancho?: number;
  alto?: number;
  piezas: number;
  lote?: string;
  pedimento?: string;
}

export interface AlmCompraRecepcionDatos {
  folio?: string;
  fecha: string;
  importacion?: { pedimento: string; bl: string; contenedor: string; aduana: string; clavePedimento: string };
  lineas: { lineaErp: string; piezas: number; lote?: string; rack?: string; fraccion?: string }[];
}

export interface AlmCompraDatos {
  folio: string;
  proveedor?: { claveAw: number; nombre: string };
  lineas?: AlmCompraLineaDatos[];
  recepciones?: AlmCompraRecepcionDatos[];
  motivo?: string;
}

export interface AlmCompraEnvio {
  id: string;
  ordenCompraId: string;
  referencia: string;
  secuencia: number;
  mensajeId: string;
  tipo: 'COMPRA_RECIBIDA' | 'COMPRA_CANCELADA' | 'COMPRA_MODIFICADA';
  estado: 'PENDIENTE' | 'ENVIADO' | 'PROCESADO' | 'DUPLICADO' | 'ERROR' | 'RECHAZADO';
  intentos: number;
  proximoIntentoEn: string | null;
  ultimoError: string | null;
  compraIdAlmacenes: string | null;
  enviadoEn: string | null;
  acuseEn: string | null;
  createdAt: string;
  datos: AlmCompraDatos | null;
}

const KEY = ['compras', 'alm-compra-envios'] as const;
const BASE = '/api/v1/compras/integraciones/alm-compra/envios';

/** Tablero I13.6: mensajes ERP → Almacenes con su acuse (86e3jvu83). Se refresca solo cada 10 s. */
export function useAlmCompraEnvios(estado?: string) {
  return useQuery({
    queryKey: [...KEY, estado ?? ''] as const,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<AlmCompraEnvio[]>(estado ? `${BASE}?estado=${estado}` : BASE, { signal });
      return data;
    },
    refetchInterval: 10_000,
  });
}

export function useReintentarAlmCompraEnvio() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, { id: string }>({
    mutationFn: async ({ id }) => {
      await apiRequest<void>(`${BASE}/${id}/reintentar`, { method: 'POST', idempotencyKey: crypto.randomUUID() });
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KEY }),
  });
}
