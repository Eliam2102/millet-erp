import { useAltoListado } from '@/components/erp/data/useAltoListado';
import { useState, type ReactNode } from 'react';
import { ReporteShell, type ReporteShellProps } from '@/components/erp/reportes/ReporteShell';
import { PaginacionTabla } from '@/components/erp/data/PaginacionTabla';
import { usePaginacionLocal } from '@/components/erp/data/usePaginacionLocal';
export type ColumnaLibro = {
  clave: string;
  etiqueta: string;
  decimales?: number;
  tipo: 'texto' | 'numero' | 'moneda' | 'fecha';
};
export function TablaLibro<T>({
  titulo,
  filas,
  columnas,
  accionFila,
}: {
  titulo: string;
  filas: T[] | undefined;
  columnas: ColumnaLibro[];
  accionFila?: (fila: T) => ReactNode;
}) {
  const pagina = usePaginacionLocal(filas ?? [], titulo);
  const region = useAltoListado(64);
  const [generadoEn] = useState(() => new Date().toISOString());
  const reporte: ReporteShellProps<T>['reporte'] = filas
    ? { titulo, generadoEn, filtrosAplicados: {}, columnas, filas }
    : undefined;
  return (
    <section className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <div ref={region} id={pagina.regionId} data-table-viewport className="h-[var(--listado-alto,auto)] min-w-0 overflow-auto">
      <ReporteShell
        reporte={reporte}
        isLoading={filas === undefined}
        error={undefined}
        filasVisibles={pagina.visibles}
        accionFila={accionFila}
      />
      </div>
      <PaginacionTabla regionId={pagina.regionId}
        total={filas?.length ?? 0}
        offset={pagina.offset}
        limit={pagina.limit}
        onChange={pagina.onChange}
      />
    </section>
  );
}
