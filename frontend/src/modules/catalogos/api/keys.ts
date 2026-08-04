/**
 * Query keys de TanStack Query para el módulo Catálogos. Mismo shape
 * que <c>datosMaestrosKeys</c>: <c>['catalogos-admin', recurso, ...]</c>.
 *
 * <para>El primer slot es <c>'catalogos-admin'</c> (no <c>'catalogos'</c>)
 * para no chocar con el namespace ya usado por
 * <c>frontend/src/features/catalogos/api/hooks.ts</c> — los hooks
 * read-only de Compras viven bajo <c>['catalogos', ...]</c> y comparten
 * cache con consumidores de selectores. Las mutaciones de admin
 * invalidan AMBOS prefijos cuando aplica (ej. crear moneda invalida
 * <c>['catalogos-admin', 'monedas']</c>; no hay key
 * <c>['catalogos', 'monedas']</c> aún, así que no se invalida).</para>
 */

export interface ListarTiposCambioFiltros {
  offset?: number;
  limit?: number;
}

export interface ListarMonedasFiltros {
  soloActivas?: boolean;
}

export const catalogosKeys = {
  all: ['catalogos-admin'] as const,

  // ─── Monedas + TiposCambio ─────────────────────────────────────
  monedas: () => [...catalogosKeys.all, 'monedas'] as const,
  monedasList: (filtros: ListarMonedasFiltros) =>
    [...catalogosKeys.monedas(), 'list', filtros] as const,
  moneda: (id: string) => [...catalogosKeys.monedas(), 'detail', id] as const,
  tiposCambio: (monedaId: string) =>
    [...catalogosKeys.monedas(), 'detail', monedaId, 'tipos-cambio'] as const,
  tiposCambioList: (monedaId: string, filtros: ListarTiposCambioFiltros) =>
    [...catalogosKeys.tiposCambio(monedaId), 'list', filtros] as const,

  // ─── Editables sin detalle ─────────────────────────────────────
  condicionesPago: () => [...catalogosKeys.all, 'condiciones-pago'] as const,
  incoterms: () => [...catalogosKeys.all, 'incoterms'] as const,
  transportistas: () => [...catalogosKeys.all, 'transportistas'] as const,
  usosPrincipales: () => [...catalogosKeys.all, 'usos-principales'] as const,
  unidadesMedida: () => [...catalogosKeys.all, 'unidades-medida'] as const,
  categoriasArticulo: () =>
    [...catalogosKeys.all, 'categorias-articulo'] as const,

  // ─── SAT read-only ─────────────────────────────────────────────
  formasPago: () => [...catalogosKeys.all, 'formas-pago'] as const,
  usosCfdi: () => [...catalogosKeys.all, 'usos-cfdi'] as const,
  regimenesFiscales: () =>
    [...catalogosKeys.all, 'regimenes-fiscales'] as const,

  // ─── SAT en vivo vía FiscalAPI (FAC-DET-PR3) ───────────────────
  satVivo: (catalogo: string, buscar: string, limit: number) =>
    [...catalogosKeys.all, 'sat-vivo', catalogo, buscar, limit] as const,
} as const;
