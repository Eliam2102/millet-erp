import { ErrorState } from '@/components/erp';
import { ResumenImportes } from '@/components/erp/display/ResumenImportes';
import { PageHeader } from '@/components/erp/PageHeader';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useRepp } from '@/features/facturacion/api/useRepp';
import { IntentosTimbradoPanel } from '@/features/facturacion/components/IntentosTimbradoPanel';
import { TimbradoFallidoBanner } from '@/features/facturacion/components/TimbradoFallidoBanner';
import { TrazabilidadFacturacion } from '@/features/facturacion/components/TrazabilidadFacturacion';
import { descargarArchivo } from '@/features/facturacion/lib/descargas';
import type { ReppSearch } from '@/features/facturacion/lib/repp-search-schema';
import { esApiError } from '@/lib/api';
import { Link, useParams, useSearch } from '@tanstack/react-router';
import { Download, FileCode, X } from 'lucide-react';
import { toast } from 'sonner';
import { EstadoFacturacionBadge } from '../components/EstadoFacturacionBadge';
import { enmascararCuenta } from '../lib/cuentas-pago-sat';

/**
 * <c>Detalle de un REPP</c> (FE-F6). Cabecera del pago + bandeja de
 * facturas cubiertas (parcialidad, importe, saldo insoluto, ganancia/
 * pérdida cambiaria), con enlace a cada factura. Descarga el XML timbrado y
 * el PDF con el formato de Millet (86e3tfdx0).
 */
export function DetalleRepp() {
  const { id } = useParams({ from: '/_app/facturacion/repp/$id' });
  const search = useSearch({ strict: false }) as ReppSearch;
  const query = useRepp(id);

  return (
    <div className="space-y-5">
      {/* Sub-topbar del detalle (§6.5): cerrar vuelve a la bandeja
          preservando los filtros activos. */}
      <div
        className="sticky top-0 z-10 flex items-center justify-end gap-2 border-b bg-surface-page/95 pb-2 backdrop-blur border-line"
        data-print="hidden"
      >
        <Button variant="ghost" size="sm" asChild aria-label="Cerrar detalle">
          <Link to="/facturacion/repp" search={search}>
            <X className="h-4 w-4" />
          </Link>
        </Button>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudo cargar el complemento de pago"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading || query.data == null ? (
        <div className="space-y-3">
          <div className="h-8 w-64 animate-pulse rounded bg-surface-muted" />
          <div className="h-40 w-full animate-pulse rounded bg-surface-muted" />
        </div>
      ) : (
        <Contenido r={query.data} />
      )}
    </div>
  );
}

const BASE = '/api/v1/facturacion/repp';

function Contenido({ r }: { r: NonNullable<ReturnType<typeof useRepp>['data']> }) {
  const timbrado = r.estado === 'Timbrado' || r.estado === 'Cancelado';

  async function descargar(tipo: 'xml' | 'pdf') {
    try {
      await descargarArchivo(`${BASE}/${r.id}/${tipo}`, `${r.folio}.${tipo}`);
    } catch (error) {
      toast.error(esApiError(error) ? error.problem.title : 'No se pudo descargar el archivo.');
    }
  }

  return (
    <div className="space-y-5">
      <TimbradoFallidoBanner
        comprobanteId={r.id}
        estado={r.estado}
        errorCodigo={r.timbradoErrorCodigo}
        errorMensaje={r.timbradoErrorMensaje}
      />

      <div className="space-y-1">
        <div className="flex flex-wrap items-center gap-3">
          <PageHeader
            mono
            titulo={<>{r.folio}</>}
            descripcion={<>{r.receptorNombre}</>}
            estado={<EstadoFacturacionBadge entidad="rep" estado={r.estado} />}
            acciones={
              <div className="flex flex-wrap items-center gap-2" data-print="hidden">
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => descargar('xml')}
                  disabled={!timbrado}
                >
                  <FileCode className="mr-1 h-4 w-4" />
                  XML
                </Button>
                <Button variant="outline" size="sm" onClick={() => descargar('pdf')}>
                  <Download className="mr-1 h-4 w-4" />
                  PDF
                </Button>
              </div>
            }
          />
        </div>
        {r.uuid && <p className="font-mono text-xs text-ink-muted">UUID {r.uuid}</p>}
      </div>

      <dl className="grid grid-cols-1 gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat sm:grid-cols-4">
        <Dato label="Receptor">{r.receptorNombre}</Dato>
        <Dato label="Moneda del pago">{r.monedaPago}</Dato>
        <Dato label="Fecha de pago">{new Date(r.fechaPago).toLocaleString('es-MX')}</Dato>
        <Dato label="Forma de pago">
          {r.formaPago ?? <span className="text-ink-muted">Sin forma de pago</span>}
        </Dato>
        <Dato label="Cuenta ordenante">
          <CuentaEnmascarada valor={r.cuentaOrdenante} />
        </Dato>
        <Dato label="Cuenta beneficiaria">
          <CuentaEnmascarada valor={r.cuentaBeneficiaria} />
        </Dato>
      </dl>

      <section className="space-y-2">
        <h2 className="text-sm font-medium">Facturas cubiertas ({r.facturasCubiertas.length})</h2>
        <div className="overflow-x-auto rounded-md border border-line">
          <Table className="w-full text-sm">
            <TableHeader className="bg-surface-muted/50">
              <TableRow>
                <TableHead className="px-3 py-2 text-left">Factura</TableHead>
                <TableHead className="px-3 py-2 text-right tabular-nums">Parcialidad</TableHead>
                <TableHead className="px-3 py-2 text-right tabular-nums">Importe pagado</TableHead>
                <TableHead className="px-3 py-2 text-right tabular-nums">Saldo insoluto</TableHead>
                <TableHead className="px-3 py-2 text-right tabular-nums">
                  Gan./pérd. cambiaria
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {r.facturasCubiertas.map((f) => (
                <TableRow key={f.facturaVentaId} className="border-t border-line">
                  <TableCell className="px-3 py-2">
                    <Link
                      to="/facturacion/facturas/$id"
                      params={{ id: f.facturaVentaId }}
                      className="font-mono text-xs text-brand hover:underline"
                    >
                      {f.folio ?? 'Ver'}
                    </Link>
                  </TableCell>
                  <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                    {f.numParcialidad}
                  </TableCell>
                  <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                    {f.importePagado.toFixed(2)}
                  </TableCell>
                  <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                    {f.saldoInsoluto.toFixed(2)}
                  </TableCell>
                  <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                    {f.gananciaPerdidaCambiaria.toFixed(2)}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <ResumenImportes moneda={r.monedaPago} total={r.importeTotalPago} />
        </div>
      </section>

      {/* ── Trazabilidad documento-céntrica (ANT-PR3, doc 13) ────── */}
      <TrazabilidadFacturacion raiz="comprobante" id={r.id} />
      <IntentosTimbradoPanel comprobanteId={r.id} />
    </div>
  );
}

function CuentaEnmascarada({ valor }: { valor?: string | null }) {
  const cuenta = enmascararCuenta(valor);
  return cuenta ? (
    <span className="font-mono text-xs">{cuenta}</span>
  ) : (
    <span className="text-ink-muted">Sin cuenta</span>
  );
}

function Dato({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-0.5">
      <dt className="text-xs text-ink-muted">{label}</dt>
      <dd className="text-sm">{children}</dd>
    </div>
  );
}
