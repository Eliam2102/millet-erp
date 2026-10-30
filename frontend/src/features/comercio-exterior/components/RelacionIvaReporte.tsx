import { useState, type ReactNode } from 'react';
import { toast } from 'sonner';
import { ErrorState } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { renderCelda } from '@/components/erp/reportes/render-celda';
import type { ReporteShellProps } from '@/components/erp/reportes/ReporteShell';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';

/** Vista del reporte CE; los exportadores compartidos reciben siempre el periodo completo. */
export function RelacionIvaReporte<TFila>({
  reporte,
  filasVisibles,
  isLoading,
  error,
  onRetry,
  filtrosUi,
  nombreArchivo,
  pie,
}: ReporteShellProps<TFila> & { pie: ReactNode; nombreArchivo: string }) {
  const [descargando, setDescargando] = useState({ pdf: false, excel: false });
  async function exportar(tipo: 'pdf' | 'excel') {
    if (!reporte) return;
    setDescargando((d) => ({ ...d, [tipo]: true }));
    try {
      if (tipo === 'excel') {
        const { descargarReporteExcel } =
          await import('@/components/erp/reportes/exporters/reporte-excel-exporter');
        await descargarReporteExcel(reporte, nombreArchivo);
      } else {
        const { descargarReportePdf } =
          await import('@/components/erp/reportes/exporters/reporte-pdf-exporter');
        await descargarReportePdf(reporte, nombreArchivo);
      }
    } catch (err) {
      toast.error(`No se pudo generar el ${tipo === 'excel' ? 'Excel' : 'PDF'}`, {
        description: err instanceof Error ? err.message : String(err),
      });
    } finally {
      setDescargando((d) => ({ ...d, [tipo]: false }));
    }
  }
  return (
    <ListadoModulo filtros={filtrosUi} pie={pie}>
      {error ? (
        <ErrorState
          title="No se pudo cargar el reporte"
          problem={error.problem}
          onRetry={onRetry}
        />
      ) : isLoading ? (
        <p role="status" className="p-4 text-ink-muted">
          Cargando reporte…
        </p>
      ) : !reporte ? (
        <p className="p-4 text-ink-muted">Captura los filtros y ejecuta el reporte.</p>
      ) : (
        <>
          <div className="flex flex-wrap items-center justify-between gap-3 border-b border-line-divider p-4">
            <div className="text-xs text-ink-muted">
              <p>Generado: {new Date(reporte.generadoEn).toLocaleString('es-MX')}</p>
              <p>
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
                onClick={() => void exportar('excel')}
                disabled={descargando.excel}
                title={descargando.excel ? 'Generando Excel.' : undefined}
              >
                Excel
              </Button>
              <Button
                variant="secondary"
                size="sm"
                onClick={() => void exportar('pdf')}
                disabled={descargando.pdf}
                title={descargando.pdf ? 'Generando PDF.' : undefined}
              >
                PDF
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
                    style={c.anchoPx ? { width: `${c.anchoPx}px` } : undefined}
                    className={c.tipo === 'numero' || c.tipo === 'moneda' ? 'text-right' : ''}
                  >
                    {c.etiqueta}
                  </TableHead>
                ))}
              </TableRow>
            </TableHeader>
            <TableBody>
              {reporte.filas.length === 0 ? (
                <TableRow>
                  <TableCell
                    colSpan={reporte.columnas.length}
                    className="text-center text-ink-muted"
                  >
                    Sin datos para los filtros aplicados.
                  </TableCell>
                </TableRow>
              ) : (
                (filasVisibles ?? reporte.filas).map((fila, i) => (
                  <TableRow key={i} className="h-12">
                    {reporte.columnas.map((c) => (
                      <TableCell
                        key={c.clave}
                        className={
                          c.tipo === 'numero' || c.tipo === 'moneda'
                            ? 'text-right tabular-nums'
                            : ''
                        }
                      >
                        {renderCelda((fila as Record<string, unknown>)[c.clave], c.tipo)}
                      </TableCell>
                    ))}
                  </TableRow>
                ))
              )}
            </TableBody>
            {reporte.totales && Object.keys(reporte.totales).length > 0 && (
              <tfoot className="bg-surface-subtle">
                <TableRow>
                  {reporte.columnas.map((c, i) => (
                    <TableCell
                      key={c.clave}
                      className={`font-semibold ${c.tipo === 'numero' || c.tipo === 'moneda' ? 'text-right tabular-nums' : ''}`}
                    >
                      {i === 0 && reporte.totales?.[c.clave] == null ? 'Totales' : ''}
                      {reporte.totales?.[c.clave] != null &&
                        renderCelda(reporte.totales[c.clave], c.tipo)}
                    </TableCell>
                  ))}
                </TableRow>
              </tfoot>
            )}
          </Table>
        </>
      )}
    </ListadoModulo>
  );
}
