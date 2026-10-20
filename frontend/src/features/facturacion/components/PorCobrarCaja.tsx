import { ErrorState, TableSkeleton } from '@/components/erp';
import { PaginacionTabla } from '@/components/erp/data/PaginacionTabla';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import type { ComprobanteCobrableItem } from '@/features/facturacion/api/types';
import { useComprobantesCobrables } from '@/features/facturacion/api/useCajaSesiones';
import { RegistrarCobroCard } from '@/features/facturacion/components/RegistrarCobroCard';
import { esApiError } from '@/lib/api';
import { HandCoins, Search } from 'lucide-react';
import { useEffect, useState } from 'react';

/** Tope del endpoint de cobrables (ListarComprobantesCobrablesQuery.Limite). */
const LIMITE = 50;

function formatearMxn(v: number): string {
  return new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' }).format(v);
}

function tipoComprobante(tipo: string): string {
  return tipo === 'Pago' ? 'Complemento de pago' : 'Factura';
}

/**
 * <c>&lt;PorCobrarCaja/&gt;</c> — bandeja «Por cobrar» de Mi caja (FIX-12):
 * comprobantes timbrados sin cobro dentro del alcance del cajero, con
 * «Cobrar» por fila que abre el formulario de cobro en un panel lateral, sin
 * pasar por Facturas. Usa el mismo endpoint de cobrables que la liquidación
 * de ruta y el mismo <c>RegistrarCobroCard</c> que el detalle de la factura,
 * así que las reglas (sesión abierta, alcance, voucher en tarjeta, neto de
 * NC, solo pesos, sin doble cobro) son las mismas.
 */
export function PorCobrarCaja() {
  const [destino] = useState(() => new URLSearchParams(window.location.search));
  const [cerrado, setCerrado] = useState(false);
  const directo = useComprobantesCobrables(
    destino.get('folio'),
    destino.has('cobrar') && destino.has('folio') && !cerrado,
  );
  const [offset, setOffset] = useState(0);
  const [texto, setTexto] = useState('');
  const [busqueda, setBusqueda] = useState('');
  const [cobrando, setCobrando] = useState<ComprobanteCobrableItem | null>(null);

  // Debounce de 200 ms, mismo criterio que el buscador del topbar.
  useEffect(() => {
    const t = setTimeout(() => setBusqueda(texto.trim()), 200);
    return () => clearTimeout(t);
  }, [texto]);

  const cobrables = useComprobantesCobrables(busqueda === '' ? null : busqueda);
  const items = cobrables.data ?? [];
  const seleccionado =
    cobrando ??
    (!cerrado
      ? directo.data?.find((c) => c.comprobanteId === destino.get('cobrar') && c.total > 0)
      : null);
  const cerrar = () => {
    setCobrando(null);
    setCerrado(true);
  };
  const inicio = Math.min(offset, Math.max(0, Math.ceil(items.length / 10) - 1) * 10);

  return (
    <section className="space-y-2" aria-labelledby="por-cobrar-titulo">
      <div className="flex flex-wrap items-end justify-between gap-2">
        <div>
          <h3 id="por-cobrar-titulo" className="font-medium">
            Por cobrar
          </h3>
          <p className="text-xs text-ink-muted">
            Facturas, anticipos y complementos de pago timbrados que puedes cobrar en esta caja.
          </p>
        </div>
        <div className="relative w-64">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-muted" />
          <Input
            value={texto}
            onChange={(e) => {
              setTexto(e.target.value);
              setOffset(0);
            }}
            placeholder="Buscar folio o cliente…"
            aria-label="Buscar comprobantes por cobrar"
            className="pl-8"
          />
        </div>
      </div>

      {destino.has('cobrar') && !cerrado && directo.isSuccess && !seleccionado && (
        <p role="status" className="text-sm text-warning-fg">
          El comprobante solicitado no está disponible para cobrar en esta caja. Revisa el folio, el
          saldo y el alcance de tu caja.
        </p>
      )}
      {destino.has('cobrar') && directo.isError && !cerrado && (
        <ErrorState
          title="No se pudo consultar el comprobante solicitado"
          problem={esApiError(directo.error) ? directo.error.problem : undefined}
          onRetry={() => directo.refetch()}
        />
      )}
      {cobrables.isLoading ? (
        <TableSkeleton rows={3} />
      ) : cobrables.isError ? (
        <ErrorState
          title="No se pudieron cargar los comprobantes por cobrar"
          problem={esApiError(cobrables.error) ? cobrables.error.problem : undefined}
          onRetry={() => cobrables.refetch()}
        />
      ) : items.length === 0 ? (
        <p className="rounded-md border bg-surface-card px-3 py-4 text-center text-sm text-ink-muted border-line">
          {busqueda === ''
            ? 'No hay comprobantes por cobrar en tu caja.'
            : `Ningún comprobante por cobrar coincide con «${busqueda}».`}
        </p>
      ) : (
        <div className="overflow-x-auto rounded-md border bg-surface-card border-line">
          <Table className="w-full text-sm">
            <TableHeader className="bg-surface-muted/50">
              <TableRow>
                <TableHead className="px-3 py-2 text-left">Comprobante</TableHead>
                <TableHead className="px-3 py-2 text-left">Cliente</TableHead>
                <TableHead className="px-3 py-2 text-right tabular-nums">Por cobrar</TableHead>
                <TableHead className="px-3 py-2 text-right tabular-nums">
                  <span className="sr-only">Acción</span>
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.slice(inicio, inicio + 10).map((c) => (
                <TableRow key={c.comprobanteId} className="border-t border-line">
                  <TableCell className="px-3 py-2">
                    <span className="font-mono text-xs">{c.folio}</span>
                    <span className="ml-2 text-xs text-ink-muted">{tipoComprobante(c.tipo)}</span>
                  </TableCell>
                  <TableCell className="px-3 py-2">{c.receptorNombre}</TableCell>
                  <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                    {formatearMxn(c.total)}
                    {c.montoAcreditado > 0 && (
                      <span className="block text-[11px] text-ink-muted">
                        Neto de NC: −{formatearMxn(c.montoAcreditado)}
                      </span>
                    )}
                  </TableCell>
                  <TableCell className="px-3 py-2 text-right tabular-nums">
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => setCobrando(c)}
                      aria-label={`Cobrar ${c.folio}`}
                    >
                      <HandCoins className="mr-1 h-3.5 w-3.5" />
                      Cobrar
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <PaginacionTabla
            total={items.length}
            offset={inicio}
            limit={10}
            tamanos={[]}
            onChange={setOffset}
          />
          {items.length >= LIMITE && (
            <p className="border-t px-3 py-2 text-xs text-ink-muted border-line">
              Se muestran los {LIMITE} más recientes. Usa el buscador para encontrar otro folio.
            </p>
          )}
        </div>
      )}

      <Sheet open={seleccionado != null} onOpenChange={(abierto) => !abierto && cerrar()}>
        <SheetContent side="right" size="md">
          {seleccionado && (
            <>
              <SheetHeader>
                <SheetTitle>Cobrar {seleccionado.folio}</SheetTitle>
                <SheetDescription>
                  {tipoComprobante(seleccionado.tipo)} · {seleccionado.receptorNombre} · por cobrar{' '}
                  {formatearMxn(seleccionado.total)}
                </SheetDescription>
              </SheetHeader>
              <SheetBody>
                <RegistrarCobroCard
                  key={seleccionado.comprobanteId}
                  comprobanteId={seleccionado.comprobanteId}
                  folio={seleccionado.folio}
                  total={seleccionado.total}
                  moneda={seleccionado.tipo === 'Pago' ? 'MXN' : seleccionado.moneda}
                  abiertoInicial
                  onCerrar={() => cerrar()}
                  onRegistrado={() => cerrar()}
                />
              </SheetBody>
            </>
          )}
        </SheetContent>
      </Sheet>
    </section>
  );
}
