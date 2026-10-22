import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Loader2 } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { ErrorState } from '@/components/erp/feedback/ErrorState';
import { esApiError } from '@/lib/api';
import {
  useAnalisisOrdenes,
  type FiltrosAnalisis,
  type IndicadorDrill,
  type OrdenDrill,
  type OrigenCompromiso,
} from '@/features/compras/reportes/api/useAnalisisProveedores';

export interface DrillTarget {
  titulo: string;
  indicador: IndicadorDrill;
  proveedorId?: string;
  articuloId?: string;
  departamentoId?: string;
  moneda?: string;
  /** Indicadores que el usuario puede alternar dentro del sheet. */
  indicadores?: readonly IndicadorDrill[];
}

const ETIQUETA_INDICADOR: Record<IndicadorDrill, string> = {
  compras: 'Compras',
  saldos: 'Saldos',
  'a-tiempo': 'A tiempo',
  completo: 'Completo',
  incidencias: 'Incidencias',
  'diferencias-precio': 'Diferencias de precio',
};

const ORIGEN: Record<OrigenCompromiso, string> = {
  incidencia: 'Incidencia',
  'promesa-proveedor': 'Promesa del proveedor',
  linea: 'Fecha de la línea',
  'oc-proveedor': 'Fecha prometida en la OC',
  'oc-esperada': 'Fecha esperada en la OC',
};

const dinero = new Intl.NumberFormat('es-MX', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const cantidad = new Intl.NumberFormat('es-MX', { maximumFractionDigits: 4 });

/** `yyyy-MM-dd[...]` → `dd/MM/yyyy` sin pasar por Date (evita desfase de zona horaria). */
function fmtFecha(v: string | null | undefined): string {
  const m = v ? /^(\d{4})-(\d{2})-(\d{2})/.exec(v) : null;
  return m ? `${m[3]}/${m[2]}/${m[1]}` : '—';
}

interface Props {
  target: DrillTarget | null;
  filtros: FiltrosAnalisis;
  onClose: () => void;
}

export function DrillOrdenesSheet({ target, filtros, onClose }: Props) {
  return (
    <Sheet open={target != null} onOpenChange={(o) => !o && onClose()}>
      <SheetContent size="lg">
        {target && <Contenido key={target.titulo + target.indicador} target={target} filtros={filtros} />}
      </SheetContent>
    </Sheet>
  );
}

function Contenido({ target, filtros }: { target: DrillTarget; filtros: FiltrosAnalisis }) {
  const [indicador, setIndicador] = useState<IndicadorDrill>(target.indicador);
  const query = useAnalisisOrdenes({
    ...filtros,
    ...(target.proveedorId ? { proveedorId: target.proveedorId } : {}),
    ...(target.articuloId ? { articuloId: target.articuloId } : {}),
    ...(target.departamentoId ? { departamentoId: target.departamentoId } : {}),
    ...(target.moneda ? { moneda: target.moneda } : {}),
    indicador,
  });
  const items = query.data?.items ?? [];

  return (
    <>
      <SheetHeader>
        <SheetTitle>{target.titulo}</SheetTitle>
        <SheetDescription>
          Órdenes de compra · {ETIQUETA_INDICADOR[indicador]} · {fmtFecha(filtros.desde)} al{' '}
          {fmtFecha(filtros.hasta)}
        </SheetDescription>
      </SheetHeader>
      <SheetBody className="space-y-4">
        {target.indicadores && (
          <div role="group" aria-label="Indicador" className="flex flex-wrap gap-2">
            {target.indicadores.map((i) => (
              <Button
                key={i}
                type="button"
                size="sm"
                variant={i === indicador ? 'default' : 'outline'}
                aria-pressed={i === indicador}
                onClick={() => setIndicador(i)}
              >
                {ETIQUETA_INDICADOR[i]}
              </Button>
            ))}
          </div>
        )}
        {esApiError(query.error) ? (
          <ErrorState
            title="No se pudo cargar el detalle"
            problem={query.error.problem}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <p role="status" className="flex items-center gap-2 text-sm text-ink-muted">
            <Loader2 className="size-4 animate-spin" aria-hidden /> Cargando órdenes…
          </p>
        ) : items.length === 0 ? (
          <p className="rounded-md bg-surface-subtle px-4 py-6 text-center text-sm text-ink-muted">
            Sin órdenes para este indicador y filtros.
          </p>
        ) : (
          <>
            <p className="text-xs text-ink-muted">{items.length} órdenes</p>
            <ul className="space-y-3">
              {items.map((oc) => (
                <li key={oc.ocId}>
                  <TarjetaOrden oc={oc} />
                </li>
              ))}
            </ul>
          </>
        )}
      </SheetBody>
    </>
  );
}

function TarjetaOrden({ oc }: { oc: OrdenDrill }) {
  const montos: { etiqueta: string; valor: number; pendiente?: boolean }[] = [
    { etiqueta: 'Ordenado', valor: oc.ordenado },
    { etiqueta: 'Por recibir', valor: oc.porRecibir, pendiente: oc.pendientes.recepcion },
    { etiqueta: 'Por facturar', valor: oc.porFacturar, pendiente: oc.pendientes.facturacion },
    { etiqueta: 'Por pagar', valor: oc.porPagar, pendiente: oc.pendientes.pago },
  ];
  return (
    <article className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <Link
            to="/compras/ordenes/$id"
            params={{ id: oc.ocId }}
            className="font-mono text-sm font-medium text-brand hover:underline"
          >
            {oc.folio}
          </Link>
          <p className="truncate text-sm text-ink">{oc.proveedor}</p>
          <p className="text-xs text-ink-muted">
            {fmtFecha(oc.fechaDocumento)} · {oc.moneda}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant="neutral">{oc.estado}</Badge>
          {oc.incidencias > 0 && (
            <Badge variant="warning">
              {oc.incidencias} {oc.incidencias === 1 ? 'incidencia' : 'incidencias'}
            </Badge>
          )}
          <Link
            to="/compras/trazabilidad/oc/$id"
            params={{ id: oc.ocId }}
            className="text-xs font-medium text-brand hover:underline"
          >
            Trazabilidad
          </Link>
        </div>
      </div>

      <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        {montos.map((m) => (
          <div key={m.etiqueta} className="space-y-1">
            <dt className="text-xs text-ink-muted">{m.etiqueta}</dt>
            <dd className="flex flex-wrap items-center gap-1.5 text-sm font-medium tabular-nums text-ink">
              {dinero.format(m.valor)}
              {m.pendiente && <Badge variant="warning">Pendiente</Badge>}
            </dd>
          </div>
        ))}
      </dl>

      {oc.lineas.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full text-xs">
            <caption className="sr-only">Líneas de {oc.folio}</caption>
            <thead>
              <tr className="border-b border-line-divider text-left text-2xs uppercase tracking-[0.04em] text-ink-muted">
                <th scope="col" className="py-1.5 pr-2 font-semibold">Línea</th>
                <th scope="col" className="px-2 py-1.5 text-right font-semibold">Cantidad</th>
                <th scope="col" className="px-2 py-1.5 text-right font-semibold">Recibido neto</th>
                <th scope="col" className="px-2 py-1.5 font-semibold">Compromiso</th>
                <th scope="col" className="px-2 py-1.5 font-semibold">Completada</th>
                <th scope="col" className="px-2 py-1.5 font-semibold">A tiempo</th>
                <th scope="col" className="py-1.5 pl-2 font-semibold">Completa</th>
              </tr>
            </thead>
            <tbody>
              {oc.lineas.map((l) => (
                <tr key={l.lineaId} className="border-b border-line-row align-top">
                  <td className="py-1.5 pr-2 text-ink">
                    <span className="font-mono text-ink-muted">{l.numero}</span> {l.articulo}
                  </td>
                  <td className="px-2 py-1.5 text-right tabular-nums">{cantidad.format(l.cantidad)}</td>
                  <td className="px-2 py-1.5 text-right tabular-nums">
                    {cantidad.format(l.recibidoNeto)}
                  </td>
                  <td className="px-2 py-1.5">
                    {fmtFecha(l.compromiso)}
                    {l.origenCompromiso && (
                      <span className="block text-ink-muted">{ORIGEN[l.origenCompromiso]}</span>
                    )}
                  </td>
                  <td className="px-2 py-1.5">{fmtFecha(l.fechaCompletada)}</td>
                  <td className="px-2 py-1.5">
                    {!l.evaluable ? (
                      <Badge variant="neutral">No evaluable</Badge>
                    ) : l.aTiempo == null ? (
                      <Badge variant="neutral">En curso</Badge>
                    ) : l.aTiempo ? (
                      <Badge variant="success">A tiempo</Badge>
                    ) : (
                      <Badge variant="danger">Tarde</Badge>
                    )}
                  </td>
                  <td className="py-1.5 pl-2">
                    {l.completa == null ? (
                      <Badge variant="neutral">No aplica</Badge>
                    ) : l.completa ? (
                      <Badge variant="success">Completa</Badge>
                    ) : (
                      <Badge variant="warning">Incompleta</Badge>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {oc.facturas.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full text-xs">
            <caption className="sr-only">Facturas de {oc.folio}</caption>
            <thead>
              <tr className="border-b border-line-divider text-left text-2xs uppercase tracking-[0.04em] text-ink-muted">
                <th scope="col" className="py-1.5 pr-2 font-semibold">Factura</th>
                <th scope="col" className="px-2 py-1.5 font-semibold">Fecha</th>
                <th scope="col" className="px-2 py-1.5 font-semibold">Estado</th>
                <th scope="col" className="px-2 py-1.5 text-right font-semibold">Total</th>
                <th scope="col" className="py-1.5 pl-2 text-right font-semibold">Diferencia vs OC</th>
              </tr>
            </thead>
            <tbody>
              {oc.facturas.map((f) => (
                <tr key={f.facturaId} className="border-b border-line-row">
                  <td className="py-1.5 pr-2 font-mono text-ink">
                    {[f.serie, f.folio].filter(Boolean).join('-') || '—'}
                  </td>
                  <td className="px-2 py-1.5">{fmtFecha(f.fecha)}</td>
                  <td className="px-2 py-1.5">{f.estado}</td>
                  <td className="px-2 py-1.5 text-right tabular-nums">
                    {f.moneda} {dinero.format(f.total)}
                  </td>
                  <td className="py-1.5 pl-2 text-right tabular-nums">
                    {f.diferenciaContraOc === 0 ? (
                      <span className="text-ink-muted">Sin diferencia</span>
                    ) : (
                      <span className="inline-flex items-center gap-1.5">
                        <Badge variant="danger">Con diferencia</Badge>
                        {f.moneda} {dinero.format(f.diferenciaContraOc)}
                      </span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </article>
  );
}
