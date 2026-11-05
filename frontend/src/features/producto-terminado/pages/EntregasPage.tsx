import { useState } from 'react';
import { Breadcrumbs, EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { useSucursales } from '@/features/catalogos/api';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useEvidenciaEntrega, useHojaSalida, useHojasSalida } from '../api/hooks';
import type { AlbaranDto, ResultadoEntrega } from '../api/types';
import { EntregaPanel } from './EntregaPanel';

// Texto además del color: el estado no depende solo de la vista.
const resultadoUi: Record<ResultadoEntrega, { texto: string; tono: 'success' | 'warning' | 'danger' }> = {
  Entregada: { texto: 'Entregada', tono: 'success' },
  Parcial: { texto: 'Entrega parcial', tono: 'warning' },
  Rechazada: { texto: 'Rechazada en sitio', tono: 'danger' },
};
const pedidoUi = { '71': { texto: "En ruta", tono: 'info' }, Entregado: { texto: 'Entregado · falta factura', tono: 'neutral' }, '115': { texto: "Facturado y entregado", tono: 'success' } } as const;

/** Entrega al cliente (PT5.6): el chofer confirma cada pedido de su ruta con firma o foto, parcial o rechazo en sitio. Pensada para celular y tablet. */
export function EntregasPage() {
  const operar = useAuthStore((s) => s.permisos.includes(P.ProductoTerminadoExcepcionesGestionar));
  const lista = useHojasSalida({ estado: 'Emitida', pageSize: 50 });
  const sucursales = useSucursales();
  const nombre = (id: string) => sucursales.data?.items.find((s) => s.id === id)?.nombre ?? 'Sin sucursal';
  const [abierta, setAbierta] = useState<string | null>(null);
  const detalle = useHojaSalida(abierta);
  const [confirmando, setConfirmando] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);

  return (
    <main className="flex h-full min-h-0 flex-col gap-5 overflow-auto text-sm">
      <Breadcrumbs items={[{ label: "Producto terminado" }, { label: 'Entregas' }]} />
      <PageHeader titulo="Entregas al cliente" descripcion="Confirma las entregas de cada ruta con la firma o una foto de quien recibe y registra las piezas rechazadas." />
      {aviso && <p role="status" className="rounded-md bg-success-bg p-3 text-success-fg">{aviso}</p>}
      {lista.isError ? (
        <ErrorState title="No se pudieron cargar las rutas" onRetry={() => void lista.refetch()} />
      ) : lista.isLoading ? (
        <TableSkeleton />
      ) : (lista.data?.items.length ?? 0) === 0 ? (
        <EmptyState title="No hay rutas emitidas por entregar." />
      ) : (
        <ul className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          {lista.data!.items.map((h) => (
            <li key={h.id} className="flex flex-col gap-2 rounded-lg bg-surface-card p-4 shadow-card-flat">
              <div className="flex items-start justify-between gap-2">
                <div>
                  <p className="font-mono text-xs">{h.folio}</p>
                  <p className="font-medium">{h.destino}</p>
                  <p className="text-xs text-ink-muted">{nombre(h.sucursalId)} · <DateTimeDisplay value={h.emitidaAt ?? h.creadaAt} variant="datetime" /></p>
                </div>
                <Badge variant={(h.entregasPendientes ?? 0) === 0 ? 'success' : 'info'}>{(h.entregasPendientes ?? 0) === 0 ? 'Todo entregado' : `${h.entregasPendientes} por entregar`}</Badge>
              </div>
              <Button variant="outline" size="lg" className="min-h-11" onClick={() => { setAbierta(abierta === h.id ? null : h.id); setConfirmando(null); setAviso(null); }}>
                {abierta === h.id ? 'Ocultar entregas' : 'Ver entregas'}
              </Button>
            </li>
          ))}
        </ul>
      )}
      {abierta && detalle.data && (
        <section aria-label={`Entregas de la ruta ${detalle.data.folio}`} className="flex flex-col gap-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
          <h2 className="text-md font-semibold">Ruta {detalle.data.folio} · {detalle.data.destino}</h2>
          <div className="grid items-start gap-3 xl:grid-cols-2">
            {detalle.data.albaranes.map((a) => (
              <PedidoEntrega
                key={a.id}
                hojaId={detalle.data!.id}
                albaran={a}
                operar={operar}
                confirmando={confirmando === a.id}
                onConfirmar={() => { setConfirmando(a.id); setAviso(null); }}
                onCancelar={() => setConfirmando(null)}
                onListo={() => { setConfirmando(null); setAviso(`Entrega del pedido ${a.numeroPedido} confirmada.`); }}
              />
            ))}
          </div>
        </section>
      )}
    </main>
  );
}

function PedidoEntrega({ hojaId, albaran: a, operar, confirmando, onConfirmar, onCancelar, onListo }: {
  hojaId: string; albaran: AlbaranDto; operar: boolean; confirmando: boolean; onConfirmar: () => void; onCancelar: () => void; onListo: () => void;
}) {
  const [verEvidencia, setVerEvidencia] = useState(false);
  const e = a.entrega;
  const evidencia = useEvidenciaEntrega(hojaId, a.id, verEvidencia && !!e);
  return (
    <article aria-label={`Pedido ${a.numeroPedido}`} className="flex flex-col gap-2 rounded-md border border-line-divider p-3">
      <header className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h3 className="font-medium">Entrega {a.secuencia} · Pedido {a.numeroPedido}</h3>
          <p className="text-xs text-ink-muted">{a.cliente}{a.destino ? ` · ${a.destino}` : ''}</p>
          <p className="text-xs text-ink-muted">{a.lineas.map((l) => `${l.piezas} pzas · pos. ${l.posicion}${l.descripcion ? ` ${l.descripcion}` : ''}`).join(' · ')}</p>
        </div>
        <div className="flex flex-col items-end gap-1">
          <Badge variant={e ? resultadoUi[e.resultado].tono : 'info'}>{e ? resultadoUi[e.resultado].texto : 'Por entregar'}</Badge>
          {a.estadoPedido && <Badge variant={pedidoUi[a.estadoPedido].tono}>{pedidoUi[a.estadoPedido].texto}</Badge>}
        </div>
      </header>
      {e ? (
        <div className="flex flex-col gap-1">
          <p>Recibió {e.receptor} · <DateTimeDisplay value={e.entregadaAt} variant="datetime" /> · {e.entregadas} de {e.entregadas + e.rechazadas} piezas.</p>
          {e.rechazadas > 0 && <p className="text-warning-fg">Rechazadas en sitio ({e.etiquetasRechazadas.join(', ')}): {e.motivoRechazo}. Pasan a revisión.</p>}
          {e.cobro && (
            <p>
              Cobro {e.cobro.formaPago}: <MoneyDisplay amount={e.cobro.monto} currency="MXN" strong />{e.cobro.referencia ? ` · voucher ${e.cobro.referencia}` : ''} ·{' '}
              {e.cobro.conciliadoAt ? <span className="text-success-fg">conciliado en caja</span> : <span className="text-ink-muted">pendiente de liquidar en caja</span>}
            </p>
          )}
          <div><Button variant="outline" size="sm" className="min-h-11" onClick={() => setVerEvidencia(!verEvidencia)}>{verEvidencia ? 'Ocultar evidencia' : 'Ver evidencia'}</Button></div>
          {verEvidencia && (
            <div className="flex flex-wrap gap-3">
              {evidencia.isLoading && <p className="text-ink-muted">Cargando evidencia…</p>}
              {evidencia.data?.firma && <img src={evidencia.data.firma} alt="Firma de quien recibió" className="h-28 rounded-md border border-line bg-surface-card" />}
              {evidencia.data?.foto && <img src={evidencia.data.foto} alt="Foto de la entrega" className="h-28 rounded-md border border-line" />}
            </div>
          )}
        </div>
      ) : confirmando ? (
        <EntregaPanel hojaId={hojaId} albaran={a} onListo={onListo} onCancelar={onCancelar} />
      ) : (
        <div>
          {operar ? <Button size="lg" className="min-h-11 max-sm:w-full" onClick={onConfirmar}>Confirmar entrega</Button> : <p className="text-ink-muted">Sin permiso para confirmar entregas.</p>}
        </div>
      )}
    </article>
  );
}
