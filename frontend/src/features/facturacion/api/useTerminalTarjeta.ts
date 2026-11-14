import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

/** 86e3vr1tf: estado de una venta de la terminal frente a los cobros de caja. */
export const EstadoTransaccionTerminal = { Emparejada: 1, SinEmparejar: 2, Rechazada: 3 } as const;
export type EstadoTransaccionTerminal =
  (typeof EstadoTransaccionTerminal)[keyof typeof EstadoTransaccionTerminal];

export interface CargaTerminal {
  id: string;
  nombreArchivo: string;
  fechaArchivo: string;
  cargadoEn: string;
  renglones: number;
  nuevos: number;
  duplicados: number;
  emparejados: number;
  sinEmparejar: number;
  rechazados: number;
  liquidaciones: number;
}

export interface TransaccionTerminal {
  id: string;
  noOperacion: string;
  fechaHora: string;
  importe: number;
  moneda: string;
  ultimos4: string | null;
  autorizacion: string | null;
  estatus: string;
  estado: EstadoTransaccionTerminal;
  comision: number | null;
  ivaComision: number | null;
  neto: number | null;
  folioLiquidacion: string | null;
  cobroMostradorId: string | null;
  folioComprobante: string | null;
  liquidacionId: string | null;
}

interface Pagina<T> {
  items: T[];
  offset: number;
  limit: number;
  total: number;
}

const BASE = '/api/v1/facturacion/terminal-tarjetas';
const keys = {
  all: ['facturacion', 'terminal-tarjetas'] as const,
  cargas: () => [...keys.all, 'cargas'] as const,
  transacciones: (estado: number | undefined) => [...keys.all, 'transacciones', { estado }] as const,
};

export function useCargasTerminal(opts?: { enabled?: boolean }) {
  return useQuery<CargaTerminal[]>({
    queryKey: keys.cargas(),
    enabled: opts?.enabled ?? true,
    queryFn: async ({ signal }) => (await apiRequest<CargaTerminal[]>(`${BASE}/cargas?limit=5`, { signal })).data,
  });
}

export function useTransaccionesTerminal(estado: number | undefined, opts?: { enabled?: boolean }) {
  return useQuery<Pagina<TransaccionTerminal>>({
    queryKey: keys.transacciones(estado),
    enabled: opts?.enabled ?? true,
    queryFn: async ({ signal }) =>
      (
        await apiRequest<Pagina<TransaccionTerminal>>(
          `${BASE}/transacciones?limit=50${estado != null ? `&estado=${estado}` : ''}`,
          { signal },
        )
      ).data,
  });
}

/** Sube el archivo diario de la terminal; recargarlo no duplica renglones. */
export function useCargarArchivoTerminal() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { archivo: File; fechaArchivo: string; idempotencyKey: string }) => {
      const formData = new FormData();
      formData.append('archivo', args.archivo);
      formData.append('fechaArchivo', args.fechaArchivo);
      return (
        await apiRequest<CargaTerminal>(`${BASE}/cargas`, {
          method: 'POST',
          body: formData,
          idempotencyKey: args.idempotencyKey,
        })
      ).data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.all }),
  });
}

/** Reintenta el emparejamiento de las ventas sin cobro (después de registrarlo). */
export function useReemparejarTerminal() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { idempotencyKey: string }) =>
      (await apiRequest<CargaTerminal>(`${BASE}/reemparejar`, { method: 'POST', idempotencyKey: args.idempotencyKey }))
        .data,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.all }),
  });
}

// ---- 86e3xt2q8 · Parte B: factura mensual del banco ligada a los cargos ----

/** CFDI recibido (buzón de CxP) candidato a factura del banco. */
export interface CfdiBancoCandidato {
  id: string;
  uuid: string;
  rfcEmisor: string;
  serie: string | null;
  folio: string | null;
  fechaCfdi: string;
  subtotal: number;
  iva: number;
  total: number;
  moneda: string;
  tipo: string;
  estado: string;
}

export interface CuadreFacturaBanco {
  anio: number;
  mes: number;
  cargos: number;
  comisionCargos: number;
  ivaCargos: number;
  totalCargos: number;
  cfdiRecibidoId: string | null;
  uuidCfdi: string | null;
  rfcEmisor: string | null;
  subtotalFactura: number | null;
  ivaFactura: number | null;
  totalFactura: number | null;
  diferenciaComision: number | null;
  diferenciaIva: number | null;
  diferenciaTotal: number | null;
  cuadra: boolean;
}

export interface FacturaBancoTerminal {
  id: string;
  cfdiRecibidoId: string;
  uuidCfdi: string;
  rfcEmisor: string;
  anio: number;
  mes: number;
  comision: number;
  ivaComision: number;
  total: number;
  cargos: number;
  ligadaEn: string;
  /** Cargos − (comisión + IVA del CFDI), a lo más $0.01; va a redondeo en la póliza. */
  redondeo?: number;
}

const facturaKeys = {
  all: [...keys.all, 'factura-banco'] as const,
  candidatos: (anio: number, mes: number) => [...facturaKeys.all, 'candidatos', { anio, mes }] as const,
  cuadre: (anio: number, mes: number, cfdi: string | null) => [...facturaKeys.all, 'cuadre', { anio, mes, cfdi }] as const,
  ligadas: () => [...facturaKeys.all, 'ligadas'] as const,
};

export function useCfdisBancoCandidatos(anio: number, mes: number) {
  return useQuery<CfdiBancoCandidato[]>({
    queryKey: facturaKeys.candidatos(anio, mes),
    queryFn: async ({ signal }) =>
      (await apiRequest<CfdiBancoCandidato[]>(`${BASE}/factura-banco/candidatos?anio=${anio}&mes=${mes}`, { signal })).data,
  });
}

/** Suma de los cargos del mes sin factura y, con un CFDI elegido, su comparación. */
export function useCuadreFacturaBanco(anio: number, mes: number, cfdiRecibidoId: string | null) {
  return useQuery<CuadreFacturaBanco>({
    queryKey: facturaKeys.cuadre(anio, mes, cfdiRecibidoId),
    queryFn: async ({ signal }) =>
      (
        await apiRequest<CuadreFacturaBanco>(
          `${BASE}/factura-banco/cuadre?anio=${anio}&mes=${mes}${cfdiRecibidoId ? `&cfdiRecibidoId=${cfdiRecibidoId}` : ''}`,
          { signal },
        )
      ).data,
  });
}

export function useFacturasBancoTerminal() {
  return useQuery<FacturaBancoTerminal[]>({
    queryKey: facturaKeys.ligadas(),
    queryFn: async ({ signal }) =>
      (await apiRequest<FacturaBancoTerminal[]>(`${BASE}/factura-banco?limit=6`, { signal })).data,
  });
}

/** Liga la factura del banco a los cargos del mes; el backend la rechaza si la diferencia pasa de $0.01. */
export function useLigarFacturaBanco() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { anio: number; mes: number; cfdiRecibidoId: string; idempotencyKey: string }) =>
      (
        await apiRequest<FacturaBancoTerminal>(`${BASE}/factura-banco`, {
          method: 'POST',
          body: { anio: args.anio, mes: args.mes, cfdiRecibidoId: args.cfdiRecibidoId },
          idempotencyKey: args.idempotencyKey,
        })
      ).data,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.all }),
  });
}

/** M-111: espejo de <c>TerminalAfiliacionResponse</c> (catálogo afiliación + terminal → sucursal). */
export interface TerminalAfiliacion {
  id: string;
  afiliacion: string;
  codTerminal: string;
  sucursalId: string;
  descripcion: string | null;
  activa: boolean;
}

/** M-111: terminal que traen los archivos cargados y aún no tiene sucursal. */
export interface TerminalSinCatalogar {
  afiliacion: string;
  codTerminal: string;
  ventas: number;
}

const clavesCatalogo = {
  lista: () => [...keys.all, 'catalogo'] as const,
  sinCatalogar: () => [...keys.all, 'sin-catalogar'] as const,
};

export function useTerminalesAfiliacion() {
  return useQuery<TerminalAfiliacion[]>({
    queryKey: clavesCatalogo.lista(),
    queryFn: async ({ signal }) => (await apiRequest<TerminalAfiliacion[]>(`${BASE}/terminales`, { signal })).data,
  });
}

export function useTerminalesSinCatalogar() {
  return useQuery<TerminalSinCatalogar[]>({
    queryKey: clavesCatalogo.sinCatalogar(),
    queryFn: async ({ signal }) =>
      (await apiRequest<TerminalSinCatalogar[]>(`${BASE}/terminales/sin-catalogar`, { signal })).data,
  });
}

/** Alta o reasignación de la terminal de una afiliación en una sucursal. */
export function useGuardarTerminalAfiliacion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      afiliacion: string;
      codTerminal: string;
      sucursalId: string;
      descripcion: string | null;
    }) =>
      (
        await apiRequest<TerminalAfiliacion>(`${BASE}/terminales`, {
          method: 'PUT',
          body: JSON.stringify(args),
          idempotencyKey: crypto.randomUUID(),
        })
      ).data,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.all }),
  });
}

export function useDesactivarTerminalAfiliacion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      (
        await apiRequest<TerminalAfiliacion>(`${BASE}/terminales/${id}/desactivar`, {
          method: 'POST',
          idempotencyKey: crypto.randomUUID(),
        })
      ).data,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.all }),
  });
}
