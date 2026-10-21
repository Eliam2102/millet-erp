import { ErrorState } from '@/components/erp/feedback/ErrorState';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { cn } from '@/lib/utils';
import { FileSpreadsheet, FileText, Loader2, Printer } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';

import type { ReporteShellProps } from '@/components/erp/reportes/ReporteShell';

// Adaptación visual local: conserva exportadores y datos completos sin cambiar el reporte compartido.
export function ReporteFacturacion<TFila>({
  reporte,
  isLoading,
  error,
  onRetry,
  filtrosUi,
  obtenerCelda,
  nombreArchivo,
  accionFila,
  etiquetaAccion = 'Acciones',
  filasVisibles,
}: ReporteShellProps<TFila>) {
  const getCelda =
    obtenerCelda ??
    ((row: TFila, clave: string) => (row as unknown as Record<string, unknown>)[clave]);

  const [descargandoPdf, setDescargandoPdf] = useState(false);
  const [descargandoExcel, setDescargandoExcel] = useState(false);

  async function descargarPdf() {
    if (reporte == null) return;
    setDescargandoPdf(true);
    try {
      const { descargarReportePdf } =
        await import('@/components/erp/reportes/exporters/reporte-pdf-exporter');
      await descargarReportePdf(reporte, nombreArchivo ?? slugify(reporte.titulo));
    } catch (err) {
      toast.error('No se pudo generar el PDF', {
        description: err instanceof Error ? err.message : String(err),
      });
    } finally {
      setDescargandoPdf(false);
    }
  }

  async function descargarExcel() {
    if (reporte == null) return;
    setDescargandoExcel(true);
    try {
      const { descargarReporteExcel } =
        await import('@/components/erp/reportes/exporters/reporte-excel-exporter');
      await descargarReporteExcel(reporte, nombreArchivo ?? slugify(reporte.titulo));
    } catch (err) {
      toast.error('No se pudo generar el Excel', {
        description: err instanceof Error ? err.message : String(err),
      });
    } finally {
      setDescargandoExcel(false);
    }
  }

  return (
    <div className="space-y-4">
      {filtrosUi && (
        <div data-print="hidden" className="rounded-md border border-line bg-surface-muted/30 p-3">
          {filtrosUi}
        </div>
      )}

      {error ? (
        <ErrorState
          title="No se pudo cargar el reporte"
          problem={error.problem}
          onRetry={onRetry}
        />
      ) : isLoading ? (
        <div className="flex items-center gap-2 rounded-md border border-line bg-surface-muted/30 px-4 py-6 text-sm text-ink-muted">
          <Loader2 className="h-4 w-4 animate-spin" />
          Cargando reporte…
        </div>
      ) : reporte == null ? (
        <div className="rounded-md border border-line bg-surface-muted/30 px-4 py-6 text-center text-sm text-ink-muted">
          Captura los filtros y ejecuta el reporte.
        </div>
      ) : (
        <>
          <header className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h2 className="text-lg font-semibold text-ink">{reporte.titulo}</h2>
              <p className="text-xs text-ink-muted">
                Generado: {new Date(reporte.generadoEn).toLocaleString('es-MX')}
              </p>
              {Object.entries(reporte.filtrosAplicados).length > 0 && (
                <p className="mt-1 text-xs text-ink-muted">
                  Filtros:{' '}
                  {Object.entries(reporte.filtrosAplicados)
                    .filter(([, v]) => v != null && v !== '')
                    .map(([k, v]) => `${k}: ${v}`)
                    .join(' · ') || '(sin filtros)'}
                </p>
              )}
            </div>
            <div data-print="hidden" className="flex gap-2">
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={descargarExcel}
                disabled={descargandoExcel}
              >
                {descargandoExcel ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : (
                  <FileSpreadsheet className="mr-2 h-4 w-4" />
                )}
                Excel
              </Button>
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={descargarPdf}
                disabled={descargandoPdf}
              >
                {descargandoPdf ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : (
                  <FileText className="mr-2 h-4 w-4" />
                )}
                PDF
              </Button>
              <Button type="button" variant="outline" size="sm" onClick={() => window.print()}>
                <Printer className="mr-2 h-4 w-4" />
                Imprimir
              </Button>
            </div>
          </header>

          <div className="overflow-x-auto rounded-md border border-line">
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50">
                <TableRow>
                  {reporte.columnas.map((c) => (
                    <TableHead
                      key={c.clave}
                      style={c.anchoPx ? { width: `${c.anchoPx}px` } : undefined}
                      className={cn(
                        'px-3 py-2 font-medium',
                        c.tipo === 'numero' || c.tipo === 'moneda'
                          ? 'text-right tabular-nums'
                          : 'text-left',
                      )}
                    >
                      {c.etiqueta}
                    </TableHead>
                  ))}
                  {accionFila && (
                    <TableHead
                      data-print="hidden"
                      className="px-3 py-2 text-right tabular-nums font-medium"
                    >
                      <span className="sr-only">{etiquetaAccion}</span>
                    </TableHead>
                  )}
                </TableRow>
              </TableHeader>
              <TableBody>
                {reporte.filas.length === 0 ? (
                  <TableRow>
                    <TableCell
                      colSpan={reporte.columnas.length + (accionFila ? 1 : 0)}
                      className="px-3 py-6 text-center text-sm text-ink-muted"
                    >
                      Sin datos para los filtros aplicados.
                    </TableCell>
                  </TableRow>
                ) : (
                  (filasVisibles ?? reporte.filas).map((fila, i) => (
                    <TableRow key={i} className="border-t">
                      {reporte.columnas.map((c) => (
                        <TableCell
                          key={c.clave}
                          className={cn(
                            'px-3 py-2',
                            (c.tipo === 'numero' || c.tipo === 'moneda') &&
                              'text-right tabular-nums font-mono',
                            c.tipo === 'texto' && 'truncate',
                          )}
                        >
                          {renderCelda(getCelda(fila, c.clave), c.tipo)}
                        </TableCell>
                      ))}
                      {accionFila && (
                        <TableCell
                          data-print="hidden"
                          className="px-3 py-2 text-right tabular-nums"
                        >
                          {accionFila(fila)}
                        </TableCell>
                      )}
                    </TableRow>
                  ))
                )}
              </TableBody>
              {reporte.totales && Object.keys(reporte.totales).length > 0 && (
                <TableBody className="border-t bg-surface-muted/30">
                  <TableRow>
                    {reporte.columnas.map((c, idx) => {
                      const total = reporte.totales?.[c.clave];
                      const esPrimera = idx === 0;
                      return (
                        <TableCell
                          key={c.clave}
                          className={cn(
                            'px-3 py-2 font-semibold',
                            c.tipo === 'numero' || c.tipo === 'moneda'
                              ? 'text-right tabular-nums font-mono'
                              : '',
                          )}
                        >
                          {esPrimera && total == null ? 'Totales' : ''}
                          {total != null && renderCelda(total, c.tipo)}
                        </TableCell>
                      );
                    })}
                    {accionFila && <TableCell data-print="hidden" />}
                  </TableRow>
                </TableBody>
              )}
            </Table>
          </div>
        </>
      )}
    </div>
  );
}

function renderCelda(valor: unknown, tipo: string): string {
  if (valor == null) return '—';
  switch (tipo) {
    case 'moneda':
      return typeof valor === 'number'
        ? new Intl.NumberFormat('es-MX', {
            style: 'currency',
            currency: 'MXN',
            minimumFractionDigits: 2,
          }).format(valor)
        : String(valor);
    case 'numero':
      return typeof valor === 'number'
        ? valor.toLocaleString('es-MX', {
            minimumFractionDigits: 0,
            maximumFractionDigits: 4,
          })
        : String(valor);
    case 'fecha':
      return typeof valor === 'string'
        ? new Date(valor).toLocaleDateString('es-MX')
        : String(valor);
    default:
      return String(valor);
  }
}

function slugify(s: string): string {
  return (
    s
      .normalize('NFKD')
      .replace(/[̀-ͯ]/g, '') // tildes
      .replace(/[^a-zA-Z0-9-_]+/g, '-')
      .replace(/^-+|-+$/g, '')
      .toLowerCase()
      .slice(0, 80) || 'reporte'
  );
}
