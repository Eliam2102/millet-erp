/**
 * Query keys de TanStack Query para el módulo Compras (doc 05 §7.2).
 * Estructura consistente para todos los hooks: <c>['compras', recurso,
 * acción, ...filtros]</c>. El namespace por módulo en el primer slot
 * facilita la invalidación masiva
 * (<c>queryClient.invalidateQueries({ queryKey: comprasKeys.all })</c>)
 * cuando se cambia de empresa o de usuario.
 *
 * <para><b>Decisión cross-module</b>: este shape de keys es la
 * convención que CxC, OC, CxP, etc., replicarán cuando lleguen.
 * Mantener el primer slot como módulo + segundo slot como recurso.</para>
 */

/** Filtros aceptados por <c>GET /api/v1/compras/requisiciones</c>. */
export interface ListarRequisicionesFiltros {
  estado?: number;
  departamentoId?: string;
  requisitanteId?: string;
  q?: string;
  offset?: number;
  limit?: number;
}

/** Filtros de <c>GET /api/v1/compras/pendientes-autorizacion</c>. */
export interface ListarPendientesFiltros {
  departamentoId?: string;
  /** Nivel de autorización pendiente (PR-A): 1 = falta N1, 2 = falta N2. */
  nivelPendiente?: number;
  offset?: number;
  limit?: number;
}

/** Filtros de <c>GET /api/v1/compras/aprobadores</c> (vigentes). */
export interface ListarAprobadoresVigentesFiltros {
  departamentoId?: string;
  rol?: number;
  usuarioId?: string;
}

/** Filtros de <c>GET /api/v1/compras/aprobadores/historico</c>. Al
 * menos UNO debe estar presente — backend devuelve 422
 * <c>FILTRO_OBLIGATORIO</c> si todos vienen vacíos. */
export interface ListarAprobadoresHistoricoFiltros {
  departamentoId?: string;
  rol?: number;
  usuarioId?: string;
}

export const comprasKeys = {
  all: ['compras'] as const,

  requisiciones: () => [...comprasKeys.all, 'requisiciones'] as const,
  requisicionesList: (filtros: ListarRequisicionesFiltros) =>
    [...comprasKeys.requisiciones(), 'list', filtros] as const,
  requisicion: (id: string) =>
    [...comprasKeys.requisiciones(), 'detail', id] as const,

  /**
   * Bandeja P2 (pendientes de autorización). Aunque internamente el
   * endpoint es atajo a <c>requisicionesList({ estado: EnAutorizacion })</c>,
   * lo guardamos como key separada para que invalidaciones específicas
   * (tras autorizar/rechazar) lo refresquen sin tocar la bandeja
   * general — un autorizador puede tener ambas tabs abiertas.
   */
  pendientesAutorizacion: (filtros: ListarPendientesFiltros) =>
    [...comprasKeys.all, 'pendientes-autorizacion', filtros] as const,

  motivosRechazo: () => [...comprasKeys.all, 'motivos-rechazo'] as const,

  /** Familia de aprobadores (P9). */
  aprobadores: () => [...comprasKeys.all, 'aprobadores'] as const,
  aprobadoresVigentes: (filtros: ListarAprobadoresVigentesFiltros) =>
    [...comprasKeys.aprobadores(), 'vigentes', filtros] as const,
  aprobadoresHistorico: (filtros: ListarAprobadoresHistoricoFiltros) =>
    [...comprasKeys.aprobadores(), 'historico', filtros] as const,

  /** Histórico de transiciones de una RQ (UF7-PR4 — endpoint
   * <c>GET /requisiciones/{id}/historico</c>). Anidado bajo
   * <c>requisiciones</c> para que un <c>invalidateQueries</c> sobre
   * la familia entera tras una mutation también refresque el timeline. */
  historicoRequisicion: (id: string) =>
    [...comprasKeys.requisiciones(), 'historico', id] as const,

  /** Preview read-only del cubrimiento estimado de una RQ (PR-C). Anidado
   * bajo `requisiciones` para que invalidar la familia tras autorizar también
   * lo refresque (aunque post-auth el preview ya no aplica). */
  cubrimientoEstimado: (id: string) =>
    [...comprasKeys.requisiciones(), 'cubrimiento-estimado', id] as const,
} as const;
