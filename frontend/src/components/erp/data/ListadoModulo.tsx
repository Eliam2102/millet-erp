import { isValidElement, type ReactNode } from 'react';
import { useAltoListado } from './useAltoListado';

export interface ListadoModuloProps {
  tabs?: ReactNode;
  filtros?: ReactNode;
  children: ReactNode;
  pie?: ReactNode;
}
/** Tarjeta de listado: usa tabs={<TabsEstado ... />}, filtros={<BarraFiltros ... />}
 * y pie={<PaginacionTabla ... />}; la tabla va como children. */
export function ListadoModulo({ tabs, filtros, children, pie }: ListadoModuloProps) {
  const tarjeta = useAltoListado();
  return (
    <div
      ref={tarjeta}
      data-listado-modulo
      className="erp-listado flex h-[var(--listado-alto,auto)] min-h-0 min-w-0 flex-1 flex-col overflow-hidden rounded-t-lg bg-surface-card text-ink shadow-card-flat"
    >
      {tabs != null && (
        <div className="flex min-h-11 min-w-0 shrink-0 items-center overflow-x-auto border-b border-line-divider px-4">
          {tabs}
        </div>
      )}
      {filtros != null && (
        <div className="shrink-0 border-b border-line-divider px-4 py-2.5">{filtros}</div>
      )}
      <div
        id={isValidElement<{ regionId?: string }>(pie) ? pie.props.regionId : undefined}
        data-table-viewport
        tabIndex={0}
        aria-label="Registros del listado"
        className="min-h-0 min-w-0 flex-1 overflow-auto focus-visible:outline-2 focus-visible:outline-primary"
      >{children}</div>
      {pie != null && (
        <div className="shrink-0 border-t border-line-divider text-xs text-ink-muted [&>nav]:border-t-0">
          {pie}
        </div>
      )}
    </div>
  );
}
