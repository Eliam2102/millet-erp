import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest, esApiError } from '@/lib/api';
import { z } from 'zod';
import { zId } from '@/lib/z-id';

export interface ClasificacionCliente { id: string; nombre: string; descripcion: string; activa: boolean; version: number }
export interface ClienteExistente { id: string; clave: string; razonSocial: string }
export interface LigadoCliente { id: string; referenciaExterna: string; rfc: string; razonSocialAw: string; version: number; candidatos: ClienteExistente[] }
export interface FichaFiscal { claveProdServSat: string; claveUnidadSat: string; objetoImp: string; tasaIvaTraslado: number; tasaRetencionIva: number | null; tasaRetencionIsr: number | null }
export interface ReglaFiscal { id: string; campo: 'Tipo' | 'Grupo' | 'Wgr'; valor: string; descripcion: string; ficha: FichaFiscal; activa: boolean; version: number }
export interface CambioFiscal { id: string; accion: string; createdAt: string; usuarioId: string | null; antesJson: string; despuesJson: string }
export interface ImportacionFiscal { aplicadas: number; sinCambios: number; errores: { fila: number; mensaje: string }[] }

export const clasificacionRequerida = zId('Elige una clasificación').min(1, 'Elige una clasificación');
export const fichaFiscalSchema = z.object({
  claveProdServSat: z.string().regex(/^\d{8}$/, 'Clave SAT de ocho dígitos'),
  claveUnidadSat: z.string().regex(/^[A-Z0-9]{1,5}$/, 'Unidad SAT requerida'),
  objetoImp: z.string().regex(/^0[1-8]$/, 'Objeto de impuesto 01–08'),
  tasaIvaTraslado: z.number({ error: "Indica una tasa numérica" }).min(0, "La tasa no puede ser negativa").max(1, "La tasa no puede ser mayor que 1"),
  tasaRetencionIva: z.number({ error: "Indica una tasa numérica" }).min(0, "La tasa no puede ser negativa").max(1, "La tasa no puede ser mayor que 1").nullable(),
  tasaRetencionIsr: z.number({ error: "Indica una tasa numérica" }).min(0, "La tasa no puede ser negativa").max(1, "La tasa no puede ser mayor que 1").nullable(),
});
/** Prefijo de las consultas F1A; invalidarlo refresca todos los paneles F1A. */
export const F1A_QUERY_KEY = ['datos-maestros', 'F1A'] as const;
export function useF1AQuery<T>(path: string, enabled = true) {
  return useQuery({ queryKey: [...F1A_QUERY_KEY, path], enabled,
    queryFn: async ({ signal }) => (await apiRequest<T>(path, { signal })).data });
}
export function useF1AMutation<T = void>() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ path, body, method = 'POST' }: { path: string; body: unknown; method?: 'POST' | 'PUT' }) =>
      (await apiRequest<T>(path, { method, body, idempotencyKey: crypto.randomUUID() })).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ['datos-maestros'] }),
  });
}
export const CLASIFICACIONES = '/api/v1/datos-maestros/clasificaciones-cliente';
export const LIGADOS = '/api/v1/datos-maestros/clientes/ligados-aw';
export const FISCAL = '/api/v1/datos-maestros/productos-aw/fiscal';
export function mensajeF1A(error: unknown) { return esApiError(error) ? error.problem.detail ?? error.problem.title : 'No se pudo guardar. Reintenta.'; }
