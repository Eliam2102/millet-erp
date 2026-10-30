import { useState, type ReactNode } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { BarraFiltros, ErrorState, ListadoModulo, PaginacionTabla } from '@/components/erp';
import { renderCelda } from '@/components/erp/reportes/render-celda';
import type { ApiError } from '@/lib/api';
import type { ReporteShellData } from '../lib/reporte-cxc-adapter';
import { usePaginacionLocal } from '../lib/use-paginacion-local';

/** Vista del reporte dinámico. Comparte los exportadores y mantiene todas las filas al imprimir. */
export function ReporteCxc({
  reporte,
  isLoading,
  error,
  onRetry,
  filtrosUi,
  nombreArchivo,
}: {
  reporte: ReporteShellData | undefined;
  isLoading: boolean;
  error: ApiError | undefined;
  onRetry: () => void;
  filtrosUi: ReactNode;
  nombreArchivo: string;
}) {
  const [descargando, setDescargando] = useState<'pdf' | 'excel' | null>(null);
  const pagina = usePaginacionLocal(
    reporte?.filas ?? [],
    JSON.stringify(reporte?.filtrosAplicados),
  );
  async function descargar(tipo: 'pdf' | 'excel') {
    if (!reporte) return;
    setDescargando(tipo);
    try {
      if (tipo === 'pdf') {
        const { descargarReportePdf } =
          await import('@/components/erp/reportes/exporters/reporte-pdf-exporter');
        await descargarReportePdf(reporte, nombreArchivo);
      } else {
        const { descargarReporteExcel } =
          await import('@/components/erp/reportes/exporters/reporte-excel-exporter');
        await descargarReporteExcel(reporte, nombreArchivo);
      }
    } catch (err) {
      toast.error(`No se pudo generar el ${tipo === 'pdf' ? 'PDF' : 'Excel'}`, {
        description: err instanceof Error ? err.message : String(err),
      });
    } finally {
      setDescargando(null);
    }
  }
  return (
    <ListadoModulo
      filtros={<BarraFiltros filtros={filtrosUi} />}
      pie={
        <div data-print="hidden">
          <PaginacionTabla
            total={reporte?.filas.length ?? 0}
            offset={pagina.offset}
            limit={10}
            tamanos={[]}
            onChange={pagina.cambiar}
          />
        </div>
      }
    >
      {error ? (
        <ErrorState
          title="No se pudo cargar el reporte"
          problem={error.problem}
          onRetry={onRetry}
        />
      ) : isLoading ? (
        <p role="status" className="p-4 text-sm text-ink-muted">
          Cargando reporte…
        </p>
      ) : !reporte ? (
        <p className="p-4 text-sm text-ink-muted">Captura los filtros y ejecuta el reporte.</p>
      ) : (
        <>
          <div className="flex flex-wrap items-center justify-between gap-3 p-4">
            <div>
              <h2 className="text-sm font-semibold">{reporte.titulo}</h2>
              <p className="text-xs text-ink-muted">
                Generado: {new Date(reporte.generadoEn).toLocaleString('es-MX')}
              </p>
              <p className="text-xs text-ink-muted">
                Filtros:{' '}
                {Object.entries(reporte.filtrosAplicados)
                  .filter(([, v]) => v != null && v !== '')
                  .map(([k, v]) => `${k}: ${v}`)
                  .join(' · ') || '(sin filtros)'}
              </p>
            </div>
            <div data-print="hidden" className="flex gap-2">
              <Button
                variant="secondary"
                size="sm"
                disabled={descargando !== null}
                title={
                  descargando
                    ? 'Generación de archivo en curso.'
                    : 'Exportar todas las filas a Excel.'
                }
                onClick={() => void descargar('excel')}
              >
                {descargando === 'excel' ? 'Generando…' : 'Excel'}
              </Button>
              <Button
                variant="secondary"
                size="sm"
                disabled={descargando !== null}
                title={
                  descargando
                    ? 'Generación de archivo en curso.'
                    : 'Exportar todas las filas a PDF.'
                }
                onClick={() => void descargar('pdf')}
              >
                {descargando === 'pdf' ? 'Generando…' : 'PDF'}
              </Button>
              <Button variant="secondary" size="sm" onClick={() => window.print()}>
                Imprimir
              </Button>
            </div>
          </div>
          <Table>
            <TableHeader>
              <TableRow>
                {reporte.columnas.map((c) => (
                  <TableHead
                    key={c.clave}
                    className={c.tipo === 'numero' || c.tipo === 'moneda' ? 'text-right' : ''}
                  >
                    {c.etiqueta}
                  </TableHead>
                ))}
              </TableRow>
            </TableHeader>
            <TableBody>
              {reporte.filas.length === 0 && (
                <TableRow>
                  <TableCell
                    colSpan={reporte.columnas.length}
                    className="text-center text-ink-muted"
                  >
                    Sin datos para los filtros aplicados.
                  </TableCell>
                </TableRow>
              )}
              {reporte.filas.map((fila, index) => (
                <TableRow
                  key={index}
                  className={
                    index >= pagina.offset && index < pagina.offset + 10
                      ? 'h-12'
                      : 'hidden print:table-row'
                  }
                >
                  {reporte.columnas.map((c) => (
                    <TableCell
                      key={c.clave}
                      className={
                        c.tipo === 'numero' || c.tipo === 'moneda'
                          ? 'text-right tabular-nums'
                          : c.tipo === 'texto'
                            ? 'truncate'
                            : ''
                      }
                    >
                      {renderCelda(fila[c.clave], c.tipo)}
                    </TableCell>
                  ))}
                </TableRow>
              ))}
              {reporte.totales && Object.keys(reporte.totales).length > 0 && (
                <TableRow className="bg-surface-subtle font-semibold">
                  {reporte.columnas.map((c, index) => (
                    <TableCell
                      key={c.clave}
                      className={
                        c.tipo === 'numero' || c.tipo === 'moneda' ? 'text-right tabular-nums' : ''
                      }
                    >
                      {reporte.totales?.[c.clave] != null
                        ? renderCelda(reporte.totales[c.clave], c.tipo)
                        : index === 0
                          ? 'Totales'
                          : ''}
                    </TableCell>
                  ))}
                </TableRow>
              )}
            </TableBody>
          </Table>
        </>
      )}
    </ListadoModulo>
  );
}
