import { type ReactNode, useState } from 'react';
import { FileSpreadsheet, FileText, Loader2, Printer } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { ErrorState } from '@/components/erp/feedback/ErrorState';
import type { ApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;ReporteShell/&gt;</c> — wrapper compartido cross-módulo para
 * reportes operativos según ADR-0036. Renderiza header (título +
 * filtros aplicados) + tabla derivada del shape canónico
 * <c>ReporteResponse&lt;TFila&gt;</c> + botones de descarga PDF /
 * Excel + impresión.
 *
 * <para><b>Exports lazy-loaded</b>: los renderers de PDF
 * (<c>@react-pdf/renderer</c>, ~500KB) y Excel (<c>exceljs</c>,
 * ~300KB) se importan via <c>import()</c> dinámico solo cuando el
 * usuario hace click — el bundle inicial queda chico aunque el
 * componente esté en todas las pantallas de reportes del ERP.</para>
 *
 * <para>API agnóstica del módulo: el caller pasa <c>filtros</c> como
 * children (puede inyectar selectores/inputs específicos del reporte).
 * El <c>nombreArchivo</c> es opcional — default deriva del título
 * sin caracteres especiales.</para>
 */
export interface ReporteShellProps<TFila> {
  /** Reporte cargado del backend. */
  reporte:
    | {
        titulo: string;
        generadoEn: string;
        filtrosAplicados: Record<string, string | null>;
        columnas: readonly {
          clave: string;
          etiqueta: string;
          tipo: 'texto' | 'numero' | 'moneda' | 'fecha';
          alineacion?: string | null;
          anchoPx?: number | null;
        }[];
        filas: readonly TFila[];
        totales?: Record<string, number> | null;
      }
    | undefined;
  isLoading: boolean;
  error: ApiError | undefined;
  onRetry?: () => void;
  /** Controles de filtros — el caller los inyecta arriba de la tabla. */
  filtrosUi?: ReactNode;
  /**
   * Hook para acceso a un valor de fila desconocido por TFila.
   * Default: <c>row[clave]</c> con cast. Override para casos donde
   * el shape no sea exactamente el del clave de columna.
   */
  obtenerCelda?: (row: TFila, clave: string) => unknown;
  /**
   * Nombre del archivo (sin extensión) para downloads PDF/Excel.
   * Default: deriva del título limpiando caracteres no-ASCII.
   */
  nombreArchivo?: string;
}

export function ReporteShell<TFila>({
  reporte,
  isLoading,
  error,
  onRetry,
  filtrosUi,
  obtenerCelda,
  nombreArchivo,
}: ReporteShellProps<TFila>) {
  const getCelda =
    obtenerCelda ??
    ((row: TFila, clave: string) =>
      (row as unknown as Record<string, unknown>)[clave]);

  const [descargandoPdf, setDescargandoPdf] = useState(false);
  const [descargandoExcel, setDescargandoExcel] = useState(false);

  async function descargarPdf() {
    if (reporte == null) return;
    setDescargandoPdf(true);
    try {
      const { descargarReportePdf } = await import(
        '@/components/erp/reportes/exporters/reporte-pdf-exporter'
      );
      await descargarReportePdf(
        reporte,
        nombreArchivo ?? slugify(reporte.titulo),
      );
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
      const { descargarReporteExcel } = await import(
        '@/components/erp/reportes/exporters/reporte-excel-exporter'
      );
      await descargarReporteExcel(
        reporte,
        nombreArchivo ?? slugify(reporte.titulo),
      );
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
        <div data-print="hidden" className="rounded-md border bg-muted/30 p-3">
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
        <div className="flex items-center gap-2 rounded-md border bg-muted/30 px-4 py-6 text-sm text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" />
          Cargando reporte…
        </div>
      ) : reporte == null ? (
        <div className="rounded-md border bg-muted/30 px-4 py-6 text-center text-sm text-muted-foreground">
          Captura los filtros y ejecuta el reporte.
        </div>
      ) : (
        <>
          <header className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h1 className="text-2xl font-semibold tracking-tight">
                {reporte.titulo}
              </h1>
              <p className="text-xs text-muted-foreground">
                Generado: {new Date(reporte.generadoEn).toLocaleString('es-MX')}
              </p>
              {Object.entries(reporte.filtrosAplicados).length > 0 && (
                <p className="mt-1 text-xs text-muted-foreground">
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
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => window.print()}
              >
                <Printer className="mr-2 h-4 w-4" />
                Imprimir
              </Button>
            </div>
          </header>

          <div className="overflow-x-auto rounded-md border">
            <table className="w-full text-sm">
              <thead className="bg-muted/50">
                <tr>
                  {reporte.columnas.map((c) => (
                    <th
                      key={c.clave}
                      style={c.anchoPx ? { width: `${c.anchoPx}px` } : undefined}
                      className={cn(
                        'px-3 py-2 font-medium',
                        c.tipo === 'numero' || c.tipo === 'moneda'
                          ? 'text-right'
                          : 'text-left',
                      )}
                    >
                      {c.etiqueta}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {reporte.filas.length === 0 ? (
                  <tr>
                    <td
                      colSpan={reporte.columnas.length}
                      className="px-3 py-6 text-center text-sm text-muted-foreground"
                    >
                      Sin datos para los filtros aplicados.
                    </td>
                  </tr>
                ) : (
                  reporte.filas.map((fila, i) => (
                    <tr key={i} className="border-t">
                      {reporte.columnas.map((c) => (
                        <td
                          key={c.clave}
                          className={cn(
                            'px-3 py-2',
                            (c.tipo === 'numero' || c.tipo === 'moneda') &&
                              'text-right font-mono',
                            c.tipo === 'texto' && 'truncate',
                          )}
                        >
                          {renderCelda(getCelda(fila, c.clave), c.tipo)}
                        </td>
                      ))}
                    </tr>
                  ))
                )}
              </tbody>
              {reporte.totales && Object.keys(reporte.totales).length > 0 && (
                <tfoot className="border-t bg-muted/30">
                  <tr>
                    {reporte.columnas.map((c, idx) => {
                      const total = reporte.totales?.[c.clave];
                      const esPrimera = idx === 0;
                      return (
                        <td
                          key={c.clave}
                          className={cn(
                            'px-3 py-2 font-semibold',
                            c.tipo === 'numero' || c.tipo === 'moneda'
                              ? 'text-right font-mono'
                              : '',
                          )}
                        >
                          {esPrimera && total == null ? 'Totales' : ''}
                          {total != null && renderCelda(total, c.tipo)}
                        </td>
                      );
                    })}
                  </tr>
                </tfoot>
              )}
            </table>
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
  return s
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '') // tildes
    .replace(/[^a-zA-Z0-9-_]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .toLowerCase()
    .slice(0, 80) || 'reporte';
}
