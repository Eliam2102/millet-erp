import { useState, type ReactNode } from 'react';
import { ReporteShell, type ReporteShellProps } from '@/components/erp/reportes/ReporteShell';
import { Button } from '@/components/ui/button';
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
  const [pagina, setPagina] = useState(0);
  const total = filas?.length ?? 0;
  const actual = Math.min(pagina, Math.max(0, Math.ceil(total / 10) - 1));
  const [generadoEn] = useState(() => new Date().toISOString());
  const reporte: ReporteShellProps<T>['reporte'] = filas
    ? { titulo, generadoEn, filtrosAplicados: {}, columnas, filas }
    : undefined;
  return (
    <section className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <ReporteShell
        reporte={reporte}
        isLoading={filas === undefined}
        error={undefined}
        filasVisibles={filas?.slice(actual * 10, actual * 10 + 10)}
        accionFila={accionFila}
      />
      {total > 10 && (
        <div className="flex items-center justify-end gap-3" data-print="hidden">
          <span>
            {actual * 10 + 1}–{Math.min(total, actual * 10 + 10)} de {total}
          </span>
          <Button
            variant="outline"
            disabled={actual === 0}
            title={actual === 0 ? 'Primera página' : undefined}
            onClick={() => setPagina(actual - 1)}
          >
            Anterior
          </Button>
          <Button
            variant="outline"
            disabled={(actual + 1) * 10 >= total}
            title={(actual + 1) * 10 >= total ? 'Última página' : undefined}
            onClick={() => setPagina(actual + 1)}
          >
            Siguiente
          </Button>
        </div>
      )}
    </section>
  );
}
