import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, esConflictoConcurrencia, useBodyScopedIdempotencyKey } from '@/lib/api';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { CE_BASE, ceKeys } from './hooks';
export interface Concepto {
  id: string;
  nombre: string;
  codigo: string;
  requiereOrdenServicio: boolean;
  baseImportacion: number;
  baseExportacion: number;
  /** M-169: grupo contable del gasto de importación (1 honorarios, 2 fletes y maniobras, 3 almacenajes, 4 otros). */
  grupoContable?: number;
}
export interface Gasto {
  id: string;
  anticipoAgenteId?: string | null;
  conceptoId: string;
  concepto: string;
  proveedorId: string;
  factura: string;
  facturaProveedorId: string | null;
  impuestoPeca?: boolean;
  moneda: string;
  importe: number;
  importeMxn: number;
  tipoCambio: number;
  fuenteTipoCambio: string;
  tipoCambioSugerido: number | null;
  diferenciaTipoCambioPct: number | null;
  notaTipoCambio: string | null;
  tipoCambioModificadoPor: string | null;
  fechaFactura: string;
  pedimento: string | null;
  ordenServicioId: string | null;
  ordenServicioFolio: string | null;
  ordenServicioTotal: number | null;
  ordenServicioMoneda: string | null;
  ordenServicioAsignado: number | null;
  cotizado: number | null;
  diferencia: number | null;
  contenedorId: string | null;
  importesDirectos: Record<string, number>;
}
export interface PartidaCosto {
  partidaId: string;
  contenedorId: string | null;
  descripcion: string;
  valorAduanaMxn: number;
  pesoKg: number;
  cantidad: number;
  clienteId: string | null;
}
export interface Distribucion {
  numeroVersion: number;
  fecha: string;
  usuarioId: string;
  renglones: {
    gastoId: string;
    conceptoId: string;
    base: number;
    partidaId: string;
    baseUsada: number;
    importeMxn: number;
  }[];
  partidas: {
    partidaId: string;
    descripcion: string;
    clienteId: string | null;
    importeMxn: number;
    diferenciaMxn: number;
    inventarioMxn: number;
    gastoImportacionMxn: number;
    tieneSalidas: boolean;
    costoUnitarioAntes: number;
    costoUnitarioDespues: number;
  }[];
}
export interface Anticipo {
  fechaPago?: string | null;
  soporteCalculoId?: string | null;
  referenciaPago?: string | null;
  devolucionPendienteId?: string | null;
  historialComprobaciones?: {
    impuestos: { iva: number; igi: number; prv: number };
    gastosMxn: number;
    fecha: string;
    motivo: string;
  }[];
  cuentaPeca?: boolean;
  /** M-327: reversa de la comprobación pedida por Comercio exterior; espera la firma del Contador General. */
  reversaPendiente?: {
    id: string;
    motivo: string;
    solicitadoPor: string;
    solicitadoEn: string;
  } | null;
  /** M-327: historial de reversas (quién pidió, cuándo, motivo y firma). */
  reversasComprobacion?: {
    solicitudId: string;
    motivo: string;
    solicitadoPor: string;
    solicitadoEn: string;
    autorizada: boolean;
    firmadoPor: string;
    firmadoEn: string;
    motivoRechazo: string | null;
    saldoAFavorMxn: number;
    gastosLiberadosMxn: number;
  }[];
  comprobacionRevertida?: boolean;
  saldoAFavorAgenteMxn?: number;
  id: string;
  agenteId: string;
  pedimento: string;
  anticipado: number;
  aplicado: number | null;
  diferencia: number | null;
  saldo: number | null;
  estado: string;
  compensacionRecibida: number;
  resoluciones: {
    tipo: string;
    importe: number;
    referencia: string;
    anticipoDestinoId: string | null;
  }[];
}
export interface OrdenServicio {
  id: string;
  folio: string;
  proveedor: string;
  moneda: string;
  total: number;
  tieneCotizacion: boolean;
  cotizacionAdjuntoId: string | null;
  contenedor: string | null;
}
export interface OrdenServicioResumen {
  id: string;
  folio: string;
  moneda: string;
  total: number;
  asignado: number;
  facturado: number;
  diferencia: number;
}
export interface Finanzas {
  autorizaciones?: {
    anticipoId: string;
    estado: string;
    nivel1UsuarioId: string | null;
    nivel1En: string | null;
    nivel2UsuarioId: string | null;
    nivel2En: string | null;
  }[];
  ordenesServicio: OrdenServicioResumen[];
  nombres: Record<string, string>;
  version: number;
  estadoFinanciero: string;
  pendientes: string[];
  conceptos: Concepto[];
  gastos: Gasto[];
  partidas: PartidaCosto[];
  bases: { id: string; pedimento: string }[];
  distribuciones: Distribucion[];
  anticipos: Anticipo[];
  /** M-169: false (por omisión) = el gasto va a gasto de importación y no cambia el costo del inventario. */
  incrementablesAlInventario?: boolean;
}
const key = (id: string) => [...ceKeys.all, 'finanzas', id];
export function useFinanzas(id: string) {
  return useQuery({
    queryKey: key(id),
    enabled: Boolean(id),
    queryFn: ({ signal }) => apiRequest<Finanzas>(`${CE_BASE}/${id}/finanzas`, { signal }),
  });
}
/** H1 paso 4: qué exige la cuenta del gasto de importación (regla C-03) en la sucursal del expediente. */
export interface DimensionesDistribucion {
  sucursalId: string;
  exigeArea: boolean;
  exigeEquipo: boolean;
  cuentaCodigo: string | null;
}

export function useDimensionesDistribucion(id: string, enabled: boolean) {
  return useQuery({
    queryKey: [...key(id), 'dimensiones-distribucion'] as const,
    enabled,
    queryFn: async ({ signal }) =>
      (await apiRequest<DimensionesDistribucion>(`${CE_BASE}/${id}/gastos/distribuir/dimensiones`, { signal })).data,
  });
}

/** Selector de áreas y equipos de la sucursal del expediente (mismo contrato que el de Contabilidad). */
export const endpointCentrosDistribucion = (id: string) => `${CE_BASE}/${id}/gastos/distribuir/centros`;

export function useMutarFinanzas(id: string) {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  const conflict = useConflictDialog();
  return useMutation({
    mutationFn: ({
      ruta,
      body,
      method = 'POST',
      etag,
    }: {
      ruta: string;
      body?: unknown;
      method?: string;
      etag?: string;
    }) => {
      const version = etag ?? qc.getQueryData<{ etag?: string }>(key(id))?.etag;
      return apiRequest<Finanzas>(`${CE_BASE}/${id}/${ruta}`, {
        method,
        body,
        ifMatch: version,
        idempotencyKey: keyFor({ ruta, body, version }),
      });
    },
    onSuccess: async (result) => {
      qc.setQueryData(key(id), result);
      await qc.invalidateQueries({ queryKey: ceKeys.all });
    },
    onError: (error) => {
      if (esConflictoConcurrencia(error))
        conflict.openSimple({ onRefrescar: () => qc.invalidateQueries({ queryKey: ceKeys.all }) });
    },
  });
}

export function useOrdenesServicio(id: string, proveedorId: string | null, q: string) {
  return useQuery({
    queryKey: [...ceKeys.all, 'ordenes-servicio', id, proveedorId, q],
    enabled: Boolean(proveedorId),
    queryFn: ({ signal }) =>
      apiRequest<OrdenServicio[]>(
        `${CE_BASE}/${id}/gastos/ordenes-servicio?${new URLSearchParams({ proveedorId: proveedorId!, q })}`,
        { signal },
      ),
  });
}
