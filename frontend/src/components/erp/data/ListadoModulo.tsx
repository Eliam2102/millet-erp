import type { ReactNode } from 'react';

export interface ListadoModuloProps {
  tabs?: ReactNode;
  filtros?: ReactNode;
  children: ReactNode;
  pie?: ReactNode;
}
/** Tarjeta de listado: usa tabs={<TabsEstado ... />}, filtros={<BarraFiltros ... />}
 * y pie={<PaginacionTabla ... />}; la tabla va como children. */
export function ListadoModulo({ tabs, filtros, children, pie }: ListadoModuloProps) {
  return (
    <div className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-t-lg bg-surface-card text-ink shadow-card-flat">
      {tabs != null && (
        <div className="flex min-h-11 shrink-0 items-center border-b border-line-divider px-4">
          {tabs}
        </div>
      )}
      {filtros != null && (
        <div className="shrink-0 border-b border-line-divider px-4 py-2.5">{filtros}</div>
      )}
      <div className="min-h-0 flex-1 overflow-auto">{children}</div>
      {pie != null && (
        <div className="shrink-0 border-t border-line-divider text-xs text-ink-muted [&>nav]:border-t-0">
          {pie}
        </div>
      )}
    </div>
  );
}
