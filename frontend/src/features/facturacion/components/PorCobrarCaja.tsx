import { useEffect, useState } from 'react';
import { HandCoins, Search } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { TableSkeleton } from '@/components/erp';
import { useComprobantesCobrables } from '@/features/facturacion/api/useCajaSesiones';
import type { ComprobanteCobrableItem } from '@/features/facturacion/api/types';
import { RegistrarCobroCard } from '@/features/facturacion/components/RegistrarCobroCard';

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

  return (
    <section className="space-y-2" aria-labelledby="por-cobrar-titulo">
      <div className="flex flex-wrap items-end justify-between gap-2">
        <div>
          <h3 id="por-cobrar-titulo" className="font-medium">
            Por cobrar
          </h3>
          <p className="text-xs text-muted-foreground">
            Facturas, anticipos y complementos de pago timbrados que puedes cobrar en esta caja.
          </p>
        </div>
        <div className="relative w-64">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={texto}
            onChange={(e) => setTexto(e.target.value)}
            placeholder="Buscar folio o cliente…"
            aria-label="Buscar comprobantes por cobrar"
            className="pl-8"
          />
        </div>
      </div>

      {cobrables.isLoading ? (
        <TableSkeleton rows={3} />
      ) : items.length === 0 ? (
        <p className="rounded-md border bg-card px-3 py-4 text-center text-sm text-muted-foreground">
          {busqueda === ''
            ? 'No hay comprobantes por cobrar en tu caja.'
            : `Ningún comprobante por cobrar coincide con «${busqueda}».`}
        </p>
      ) : (
        <div className="overflow-x-auto rounded-md border bg-card">
          <table className="w-full text-sm">
            <thead className="bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">Comprobante</th>
                <th className="px-3 py-2 text-left">Cliente</th>
                <th className="px-3 py-2 text-right">Por cobrar</th>
                <th className="px-3 py-2 text-right">
                  <span className="sr-only">Acción</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {items.map((c) => (
                <tr key={c.comprobanteId} className="border-t">
                  <td className="px-3 py-2">
                    <span className="font-mono text-xs">{c.folio}</span>
                    <span className="ml-2 text-xs text-muted-foreground">
                      {tipoComprobante(c.tipo)}
                    </span>
                  </td>
                  <td className="px-3 py-2">{c.receptorNombre}</td>
                  <td className="px-3 py-2 text-right font-mono tabular-nums">
                    {formatearMxn(c.total)}
                    {c.montoAcreditado > 0 && (
                      <span className="block text-[11px] text-muted-foreground">
                        Neto de NC: −{formatearMxn(c.montoAcreditado)}
                      </span>
                    )}
                  </td>
                  <td className="px-3 py-2 text-right">
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => setCobrando(c)}
                      aria-label={`Cobrar ${c.folio}`}
                    >
                      <HandCoins className="mr-1 h-3.5 w-3.5" />
                      Cobrar
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {items.length >= LIMITE && (
            <p className="border-t px-3 py-2 text-xs text-muted-foreground">
              Se muestran los {LIMITE} más recientes. Usa el buscador para encontrar otro folio.
            </p>
          )}
        </div>
      )}

      <Sheet open={cobrando != null} onOpenChange={(abierto) => !abierto && setCobrando(null)}>
        <SheetContent side="right" className="w-full overflow-y-auto sm:max-w-xl">
          {cobrando && (
            <>
              <SheetHeader>
                <SheetTitle>Cobrar {cobrando.folio}</SheetTitle>
                <SheetDescription>
                  {tipoComprobante(cobrando.tipo)} · {cobrando.receptorNombre} · por cobrar{' '}
                  {formatearMxn(cobrando.total)}
                </SheetDescription>
              </SheetHeader>
              <div className="px-4 pb-4">
                <RegistrarCobroCard
                  key={cobrando.comprobanteId}
                  comprobanteId={cobrando.comprobanteId}
                  folio={cobrando.folio}
                  total={cobrando.total}
                  moneda={cobrando.moneda}
                  abiertoInicial
                  onCerrar={() => setCobrando(null)}
                  onRegistrado={() => setCobrando(null)}
                />
              </div>
            </>
          )}
        </SheetContent>
      </Sheet>
    </section>
  );
}
