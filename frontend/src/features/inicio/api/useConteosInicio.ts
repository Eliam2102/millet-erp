import { useQueries } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { comprasKeys } from '@/features/compras/api/keys';
import type { PagedResponse, RequisicionListItemResponse } from '@/features/compras/api/types';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type { ListarOrdenesCompraResponse } from '@/features/compras/ordenes/api/types';
import type { KpisPartidasAbiertasResponse } from '@/features/compras/ordenes/api/useKpisPartidasAbiertas';
import { almacenKeys } from '@/features/almacen/api/keys';
import type {
  PagedResponse as RecepcionesResponse,
  RecepcionListItem,
} from '@/features/almacen/api/types';
import { datosMaestrosKeys } from '@/modules/datos-maestros/api/keys';
import type { ListarProveedoresResponse } from '@/modules/datos-maestros/api/types';
import { cxpKeys } from '@/features/cxp/api/keys';
import type { PagedResponse as FacturasResponse, FacturaListItem } from '@/features/cxp/api/types';
import { tesoreriaKeys } from '@/features/tesoreria/api/keys';
import type {
  PagedResponse as TesoreriaResponse,
  PagoACuentaAbiertoResponse,
  DepositoConfirmacionResponse,
} from '@/features/tesoreria/api/types';
import { cxcKeys } from '@/features/cxc/api/keys';
import type {
  PagedResponse as AlertasResponse,
  AlertaCarteraResponse,
} from '@/features/cxc/api/types';
import type { ConteoId } from '../config';

export interface ConteoInicio {
  total: number;
  atrasadas?: number;
  /** Solo cuando el endpoint garantiza orden FIFO con página de un registro. */
  fechaMasAntigua?: string;
}

function consulta<T>(
  familia: readonly unknown[],
  path: string,
  filtros: Record<string, string | number | boolean | undefined>,
  resumir: (data: T) => ConteoInicio,
) {
  return {
    queryKey: [...familia, 'conteo', filtros],
    queryFn: async ({ signal }: { signal: AbortSignal }) => {
      const params = new URLSearchParams();
      for (const [key, value] of Object.entries(filtros)) {
        if (value !== undefined) params.set(key, String(value));
      }
      const qs = params.toString();
      const { data } = await apiRequest<T>(qs ? `${path}?${qs}` : path, { signal });
      return resumir(data);
    },
    retry: 1,
    staleTime: 60_000,
  };
}

/** 1 o 2 si el usuario solo autoriza ese nivel; undefined si autoriza ambos (o ninguno). */
function nivelPorPermisos(permisos: readonly string[], n1: string, n2: string): 1 | 2 | undefined {
  if (permisos.includes(n1) === permisos.includes(n2)) return undefined;
  return permisos.includes(n1) ? 1 : 2;
}

/** Nivel de requisiciones que cuenta Inicio; la fila enlaza a la bandeja con el mismo filtro. */
export function nivelRequisiciones(permisos: readonly string[]) {
  return nivelPorPermisos(
    permisos,
    P.ComprasRequisicionesAutorizarNivel1,
    P.ComprasRequisicionesAutorizarNivel2,
  );
}

export function useConteosInicio(ids: readonly ConteoId[], permisos: readonly string[]) {
  const nivelRq = nivelRequisiciones(permisos);
  const nivelOc = nivelPorPermisos(
    permisos,
    P.ComprasOrdenesAutorizarNivel1,
    P.ComprasOrdenesAutorizarNivel2,
  );
  const nivelOrden = nivelOc === 1 ? 'Nivel1' : nivelOc === 2 ? 'Nivel2' : undefined;
  const pagina = { offset: 0, limit: 1 };
  const rq = { ...pagina, nivelPendiente: nivelRq };
  const total = (data: { total: number }) => ({ total: data.total });
  // Sin departamento seleccionado, igual que la bandeja inicial; el API aplica su alcance.
  const consultas = {
    requisiciones: consulta<PagedResponse<RequisicionListItemResponse>>(
      comprasKeys.pendientesAutorizacion(rq),
      '/api/v1/compras/pendientes-autorizacion',
      rq,
      (data) => ({ total: data.total, fechaMasAntigua: data.items[0]?.fechaSolicitud }),
    ),
    ordenes: consulta<ListarOrdenesCompraResponse>(
      ordenesKeys.pendientes(nivelOrden ?? 'todos'),
      '/api/v1/compras/ordenes/pendientes-autorizacion',
      { page: 1, pageSize: 1, nivel: nivelOrden },
      (data) => ({ total: data.totalCount, fechaMasAntigua: data.items[0]?.fechaDocumento }),
    ),
    abiertas: consulta<KpisPartidasAbiertasResponse>(
      [...ordenesKeys.all, 'partidas-abiertas', 'kpis'],
      '/api/v1/compras/ordenes/partidas-abiertas/kpis',
      {},
      (data) => ({ total: data.countPartidasAbiertas, atrasadas: data.countAtrasadas }),
    ),
    recepciones: consulta<RecepcionesResponse<RecepcionListItem>>(
      almacenKeys.recepciones(),
      '/api/v1/almacen/recepciones',
      { estado: 0, ...pagina },
      total,
    ),
    proveedores: consulta<ListarProveedoresResponse>(
      datosMaestrosKeys.proveedores(),
      '/api/v1/datos-maestros/proveedores',
      { estatus: 2, ...pagina },
      total,
    ),
    facturas: consulta<FacturasResponse<FacturaListItem>>(
      cxpKeys.facturas(),
      '/api/v1/cuentas-por-pagar/facturas',
      { estado: 2, ...pagina },
      total,
    ),
    pagosCuenta: consulta<TesoreriaResponse<PagoACuentaAbiertoResponse>>(
      tesoreriaKeys.pagosCuenta(),
      '/api/v1/tesoreria/pagos-cuenta',
      { incluirParciales: true, ...pagina },
      (data) => ({ total: data.total, fechaMasAntigua: data.items[0]?.fechaValor }),
    ),
    depositos: consulta<TesoreriaResponse<DepositoConfirmacionResponse>>(
      tesoreriaKeys.depositos(),
      '/api/v1/tesoreria/depositos',
      { estado: 1, ...pagina },
      total,
    ),
    alertas: consulta<AlertasResponse<AlertaCarteraResponse>>(
      cxcKeys.alertas(),
      '/api/v1/cuentas-por-cobrar/alertas',
      { atendida: false, ...pagina },
      total,
    ),
  };
  // Solo se crean observadores para las filas visibles; las demás no consultan el API.
  return useQueries({ queries: ids.map((id) => consultas[id]) });
}
