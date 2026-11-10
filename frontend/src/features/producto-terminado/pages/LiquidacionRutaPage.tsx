import { useState } from 'react';
import { Breadcrumbs, EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { mensajeErrorAccion } from '@/lib/api/error';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import {
  useCierreDiaContraEntrega, useLiquidacion, useLiquidaciones, useLiquidarRuta,
  type EstadoLiquidacion, type LiquidacionDto, type TipoTarjeta,
} from '../api/liquidacion';

// Texto además del color: el estado no depende solo de la vista.
// Regla de salida 5 (Millet, 9-nov): sucursal hasta el siguiente día hábil 13:00, Conkal 2 días; alerta a las 12 horas hábiles.
const plazoUi: Record<'EnPlazo' | 'Alerta' | 'Vencida', { texto: string; tono: 'info' | 'warning' | 'danger' }> = {
  EnPlazo: { texto: 'En plazo', tono: 'info' },
  Alerta: { texto: '12 h hábiles sin liquidar', tono: 'warning' },
  Vencida: { texto: 'Vencida', tono: 'danger' },
};

const estadoUi: Record<EstadoLiquidacion, { texto: string; tono: 'info' | 'warning' | 'success' }> = {
  Pendiente: { texto: 'Sin liquidar', tono: 'info' },
  ConDiferencia: { texto: 'Con diferencia', tono: 'warning' },
  Cerrada: { texto: 'Cerrada', tono: 'success' },
};

interface LineaCaptura {
  firmado: boolean;
  efectivo: string;
  voucher: string;
  referencia: string;
  tipoTarjeta: TipoTarjeta | null;
  ultimos4: string;
}

const numero = (s: string) => (s.trim() === '' ? 0 : Number(s));
// M-177: la caja registra el voucher con su tipo de tarjeta (forma 04 o 28) y los últimos 4 dígitos.
const voucherCompleto = (l: LineaCaptura) => numero(l.voucher) === 0
  || (l.referencia.trim() !== '' && l.tipoTarjeta !== null && /^\d{4}$/.test(l.ultimos4.trim()));
const importeValido = (s: string) => s.trim() === '' || (Number.isFinite(Number(s)) && Number(s) >= 0);

function PanelLiquidacion({ dto, operar }: { dto: LiquidacionDto; operar: boolean }) {
  const liquidar = useLiquidarRuta();
  const [lineas, setLineas] = useState<Record<string, LineaCaptura>>(() =>
    Object.fromEntries(
      dto.albaranes.map((a) => [
        a.albaranId,
        {
          firmado: a.firmado, efectivo: a.efectivo ? String(a.efectivo) : '', voucher: a.voucher ? String(a.voucher) : '', referencia: a.voucherReferencia ?? '',
          tipoTarjeta: a.tipoTarjeta ?? null, ultimos4: a.tarjetaUltimos4 ?? '',
        },
      ]),
    ),
  );
  const [imprevistos, setImprevistos] = useState<{ descripcion: string; importe: string }[]>(() =>
    dto.imprevistos.map((i) => ({ descripcion: i.descripcion, importe: String(i.importe) })),
  );
  const [error, setError] = useState<string | null>(null);

  const cerrada = dto.estado === 'Cerrada';
  const editable = operar && !cerrada;
  const cambiar = (id: string, parche: Partial<LineaCaptura>) => setLineas((l) => ({ ...l, [id]: { ...l[id], ...parche } }));
  const diferenciaDe = (esperado: number, l: LineaCaptura) => esperado - numero(l.efectivo) - numero(l.voucher);
  const total = dto.albaranes.reduce((s, a) => s + diferenciaDe(a.esperado, lineas[a.albaranId]), 0);
  const valido = dto.albaranes.every((a) => importeValido(lineas[a.albaranId].efectivo) && importeValido(lineas[a.albaranId].voucher)
    && voucherCompleto(lineas[a.albaranId]))
    && imprevistos.every((i) => i.descripcion.trim() !== '' && importeValido(i.importe));

  const enviar = () => {
    setError(null);
    liquidar
      .mutateAsync({
        salidaId: dto.salidaId,
        body: {
          albaranes: dto.albaranes.map((a) => {
            const l = lineas[a.albaranId];
            const conVoucher = numero(l.voucher) > 0;
            return {
              albaranId: a.albaranId, firmado: l.firmado, efectivo: numero(l.efectivo), voucher: numero(l.voucher),
              voucherReferencia: conVoucher ? l.referencia.trim() : null,
              tipoTarjeta: conVoucher ? l.tipoTarjeta : null,
              tarjetaUltimos4: conVoucher ? l.ultimos4.trim() : null,
            };
          }),
          imprevistos: imprevistos.map((i) => ({ descripcion: i.descripcion.trim(), importe: numero(i.importe) })),
        },
      })
      .catch((e: unknown) => setError(mensajeErrorAccion(e)));
  };

  return (
    <section aria-label={`Liquidación de ${dto.folio}`} className="flex flex-col gap-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <h2 className="text-base font-semibold">{dto.folio} · {dto.destino}</h2>
      {error && <AvisoEnLinea tono="error" titulo="No se pudo liquidar">{error}</AvisoEnLinea>}
      {cerrada && (
        <p role="status" className="rounded-md bg-success-bg p-3 text-success-fg">
          Liquidación cerrada: el cobro coincide con las facturas, los albaranes volvieron firmados y lo cobrado entró a tu sesión de caja.
        </p>
      )}
      {editable && (
        <p className="text-xs text-muted-foreground">
          Al cuadrar, lo cobrado entra como cobro a tu sesión de caja abierta en la sucursal de la hoja, sobre la factura de cada albarán.
        </p>
      )}
      {dto.estado === 'ConDiferencia' && (
        <AvisoEnLinea tono="advertencia" titulo="Liquidación con diferencia: no se cierra">
          Corrige el cobro o recibe el albarán firmado y vuelve a liquidar. Mientras tanto la contra entrega cuenta como no liquidada.
        </AvisoEnLinea>
      )}
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Albarán</TableHead>
            <TableHead>Pedido</TableHead>
            <TableHead>Debe cobrar</TableHead>
            <TableHead>Firmado</TableHead>
            <TableHead>Efectivo</TableHead>
            <TableHead>Voucher</TableHead>
            <TableHead>Datos del voucher</TableHead>
            <TableHead>Diferencia</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {dto.albaranes.map((a) => {
            const l = lineas[a.albaranId];
            const dif = diferenciaDe(a.esperado, l);
            return (
              <TableRow key={a.albaranId}>
                <TableCell className="font-mono text-xs">{a.albaranFolio}</TableCell>
                <TableCell>
                  {a.numeroPedido}
                  <div className="text-xs text-muted-foreground">{a.cliente}</div>
                  <div className="flex flex-wrap gap-1 pt-1">
                    {a.contraEntrega ? <Badge variant="info">Contra entrega</Badge> : <Badge variant="neutral">No se cobra en ruta</Badge>}
                    {a.avisado && <Badge variant="warning">Avisado a CxC</Badge>}
                    {a.cobradoEnCaja && <Badge variant="success">Cobrado en caja</Badge>}
                  </div>
                  {a.facturaFolio && <div className="pt-1 text-xs text-muted-foreground">Factura <span className="font-mono">{a.facturaFolio}</span></div>}
                </TableCell>
                <TableCell><MoneyDisplay amount={a.esperado} currency="MXN" /></TableCell>
                <TableCell>
                  <Checkbox aria-label={`Albarán ${a.albaranFolio} firmado`} checked={l.firmado} disabled={!editable} onCheckedChange={(v) => cambiar(a.albaranId, { firmado: v === true })} />
                </TableCell>
                <TableCell>
                  <Input aria-label={`Efectivo ${a.albaranFolio}`} inputMode="decimal" className="w-28" value={l.efectivo} disabled={!editable} onChange={(e) => cambiar(a.albaranId, { efectivo: e.target.value })} />
                </TableCell>
                <TableCell>
                  <Input aria-label={`Voucher ${a.albaranFolio}`} inputMode="decimal" className="w-28" value={l.voucher} disabled={!editable} onChange={(e) => cambiar(a.albaranId, { voucher: e.target.value })} />
                </TableCell>
                <TableCell>
                  <div className="flex flex-col gap-1.5">
                    <Input aria-label={`Folio del voucher ${a.albaranFolio}`} className="w-36" placeholder="Autorización" value={l.referencia} disabled={!editable || numero(l.voucher) === 0} onChange={(e) => cambiar(a.albaranId, { referencia: e.target.value })} />
                    <div role="group" aria-label={`Tipo de tarjeta ${a.albaranFolio}`} className="flex gap-1">
                      {(['Credito', 'Debito'] as const).map((t) => (
                        <Button key={t} type="button" size="sm" variant={l.tipoTarjeta === t ? 'default' : 'outline'} aria-pressed={l.tipoTarjeta === t}
                          disabled={!editable || numero(l.voucher) === 0} onClick={() => cambiar(a.albaranId, { tipoTarjeta: t })}>
                          {t === 'Credito' ? 'Crédito' : 'Débito'}
                        </Button>
                      ))}
                    </div>
                    <Input aria-label={`Últimos 4 de la tarjeta ${a.albaranFolio}`} inputMode="numeric" maxLength={4} className="w-24" placeholder="Últimos 4" value={l.ultimos4}
                      disabled={!editable || numero(l.voucher) === 0} onChange={(e) => cambiar(a.albaranId, { ultimos4: e.target.value.replace(/\D/g, '') })} />
                  </div>
                </TableCell>
                <TableCell>
                  {dif === 0 ? <Badge variant="success">Cuadra</Badge> : <Badge variant="warning" className="gap-1">{dif > 0 ? 'Falta' : 'Sobra'} <MoneyDisplay amount={Math.abs(dif)} currency="MXN" /></Badge>}
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
      <div className="flex flex-col gap-2">
        <h3 className="text-sm font-semibold">Imprevistos de la ruta</h3>
        {imprevistos.map((i, n) => (
          <div key={n} className="flex gap-2">
            <Input aria-label={`Imprevisto ${n + 1}`} className="max-w-md" placeholder="Qué pasó en la ruta" value={i.descripcion} disabled={!editable}
              onChange={(e) => setImprevistos((x) => x.map((y, k) => (k === n ? { ...y, descripcion: e.target.value } : y)))} />
            <Input aria-label={`Importe del imprevisto ${n + 1}`} inputMode="decimal" className="w-28" placeholder="Importe" value={i.importe} disabled={!editable}
              onChange={(e) => setImprevistos((x) => x.map((y, k) => (k === n ? { ...y, importe: e.target.value } : y)))} />
            {editable && <Button variant="outline" size="sm" onClick={() => setImprevistos((x) => x.filter((_, k) => k !== n))}>Quitar</Button>}
          </div>
        ))}
        {editable && <div><Button variant="outline" size="sm" onClick={() => setImprevistos((x) => [...x, { descripcion: '', importe: '' }])}>Agregar imprevisto</Button></div>}
      </div>
      <div className="flex flex-wrap items-center gap-4">
        <span>Diferencia total: <strong><MoneyDisplay amount={total} currency="MXN" /></strong></span>
        {editable && <Button disabled={!valido || liquidar.isPending} onClick={enviar}>Liquidar ruta</Button>}
        {cerrada && <Badge variant="success">Cerrada</Badge>}
      </div>
    </section>
  );
}

export function LiquidacionRutaPage() {
  const operar = useAuthStore((s) => s.permisos.includes(P.ProductoTerminadoExcepcionesGestionar));
  const lista = useLiquidaciones();
  const cierre = useCierreDiaContraEntrega();
  const [abierta, setAbierta] = useState<string | null>(null);
  const detalle = useLiquidacion(abierta);
  const [aviso, setAviso] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const cerrarDia = () => {
    setError(null);
    setAviso(null);
    cierre
      .mutateAsync(new Date().toISOString())
      .then(({ data }) => setAviso(data.avisos === 0 ? 'No hay contra entrega pendiente de avisar.' : `Se avisó a Crédito y Cobranza de ${data.avisos} contra entrega sin liquidar.`))
      .catch((e: unknown) => setError(mensajeErrorAccion(e)));
  };

  return (
    <main className="flex h-full min-h-0 flex-col gap-5 overflow-auto text-sm">
      <Breadcrumbs items={[{ label: 'Producto Terminado' }, { label: 'Liquidación de ruta' }]} />
      <PageHeader
        titulo="Liquidación de ruta"
        descripcion="Cuando el chofer regresa, Caja recibe los albaranes firmados, el efectivo y los vouchers, y registra los imprevistos. La contra entrega (Crédito 24 horas) que no se liquida el mismo día se avisa a Crédito y Cobranza."
      />
      {operar && (
        <div className="flex items-center gap-3">
          <Button variant="outline" disabled={cierre.isPending} onClick={cerrarDia}>Cerrar el día de caja</Button>
          <span className="text-muted-foreground">También corre solo después de las 20:00.</span>
        </div>
      )}
      {error && <AvisoEnLinea tono="error" titulo="No se pudo cerrar el día">{error}</AvisoEnLinea>}
      {aviso && <p role="status" className="rounded-md bg-success-bg p-3 text-success-fg">{aviso}</p>}
      {lista.isError ? (
        <ErrorState title="No se pudieron cargar las hojas" onRetry={() => void lista.refetch()} />
      ) : lista.isLoading ? (
        <TableSkeleton />
      ) : (lista.data?.items.length ?? 0) === 0 ? (
        <EmptyState title="No hay hojas de salida emitidas." />
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Hoja</TableHead>
              <TableHead>Destino</TableHead>
              <TableHead>Salida</TableHead>
              <TableHead>Pedidos</TableHead>
              <TableHead>Contra entrega</TableHead>
              <TableHead>Liquidación</TableHead>
              <TableHead>Plazo para liquidar</TableHead>
              <TableHead>Diferencia</TableHead>
              <TableHead>Acciones</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {lista.data!.items.map((h) => (
              <TableRow key={h.salidaId}>
                <TableCell className="font-mono text-xs">{h.folio}</TableCell>
                <TableCell>{h.destino}</TableCell>
                <TableCell><DateTimeDisplay value={h.emitidaAt} variant="datetime" /></TableCell>
                <TableCell className="tabular-nums">{h.pedidos}</TableCell>
                <TableCell className="tabular-nums">{h.contraEntrega}</TableCell>
                <TableCell><Badge variant={estadoUi[h.estado].tono}>{estadoUi[h.estado].texto}</Badge></TableCell>
                <TableCell>
                  {h.plazo && h.limiteAt ? (
                    <span className="flex flex-col gap-1">
                      <Badge variant={plazoUi[h.plazo].tono}>{plazoUi[h.plazo].texto}</Badge>
                      <span className="text-xs text-ink-muted">Hasta <DateTimeDisplay value={h.limiteAt} variant="datetime" /></span>
                    </span>
                  ) : '—'}
                </TableCell>
                <TableCell>{h.estado === 'Pendiente' ? '—' : <MoneyDisplay amount={h.diferencia} currency="MXN" />}</TableCell>
                <TableCell>
                  <Button variant="outline" size="sm" onClick={() => setAbierta(abierta === h.salidaId ? null : h.salidaId)}>
                    {abierta === h.salidaId ? 'Ocultar' : h.estado === 'Cerrada' ? 'Ver liquidación' : 'Liquidar'}
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {abierta && detalle.isError && <ErrorState title="No se pudo cargar la liquidación" onRetry={() => void detalle.refetch()} />}
      {abierta && detalle.data && (
        <PanelLiquidacion key={`${detalle.data.salidaId}-${detalle.data.liquidadaAt ?? 'nueva'}`} dto={detalle.data} operar={operar} />
      )}
    </main>
  );
}
