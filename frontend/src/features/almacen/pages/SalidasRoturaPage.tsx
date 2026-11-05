import { problemaUsuario } from '@/lib/error-usuario';
import { errorUsuario } from '@/lib/error-usuario';
import { useState } from 'react';
import { Plus } from 'lucide-react';
import { toast } from 'sonner';
import { PageHeader } from '@/components/erp/PageHeader';
import { EmptyState, ErrorState, TableSkeleton, TabsEstado, TextAreaField } from '@/components/erp';
import { KpiCard } from '@/components/erp/display/KpiCard';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { ConfirmarAccionDialog } from '@/components/erp/feedback/ConfirmarAccionDialog';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
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
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { CapturarRoturaSheet } from '@/features/almacen/components/CapturarRoturaSheet';
import { useDecidirRotura, useSalidasRotura, type SalidaRotura } from '@/features/almacen/api/useSalidasRotura';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

const ESTADOS = { 1: 'PENDIENTE', 2: 'AUTORIZADA', 3: 'RECHAZADA' } as const;
const ESTADO_UI: Record<SalidaRotura['estado'], { texto: string; variante: BadgeProps['variant'] }> = {
  PENDIENTE: { texto: 'Pendiente de autorizar', variante: 'warning' },
  AUTORIZADA: { texto: 'Autorizada', variante: 'success' },
  RECHAZADA: { texto: 'Rechazada', variante: 'danger' },
};
const num = (n: number) => n.toLocaleString('es-MX', { maximumFractionDigits: 4 });
const mxn = (n: number | null) => (n === null ? '—' : n.toLocaleString('es-MX', { style: 'currency', currency: 'MXN' }));

/**
 * ALM-03 — Salida por rotura: el almacenista captura y un Jefe o Gerente de almacén distinto autoriza o rechaza. Hasta
 * autorizar no se mueve el saldo; al autorizar sale a costo promedio y se avisa a Contabilidad para la póliza de merma.
 */
export function SalidasRoturaPage() {
  const [tab, setTab] = useState<number | undefined>(1);
  const [capturando, setCapturando] = useState(false);
  const [detalle, setDetalle] = useState<SalidaRotura | null>(null);
  const [autorizando, setAutorizando] = useState<SalidaRotura | null>(null);
  const [rechazando, setRechazando] = useState<SalidaRotura | null>(null);
  const [motivoRechazo, setMotivoRechazo] = useState<string | null>(null);
  const puedeCapturar = useHasPermission(PermisosCanonicos.AlmacenSalidasRoturaCapturar);
  const puedeAutorizar = useHasPermission(PermisosCanonicos.AlmacenSalidasRoturaAutorizar);
  const query = useSalidasRotura();
  const decidir = useDecidirRotura();
  const todas = query.data ?? [];
  const items = tab ? todas.filter((r) => r.estado === ESTADOS[tab as 1 | 2 | 3]) : todas;
  const autorizadas = todas.filter((r) => r.estado === 'AUTORIZADA');
  const conteos = {
    1: todas.filter((r) => r.estado === 'PENDIENTE').length,
    2: autorizadas.length,
    3: todas.filter((r) => r.estado === 'RECHAZADA').length,
  };
  const valorAutorizado = autorizadas.some((r) => r.valorMxn == null)
    ? null
    : autorizadas.reduce((s, r) => s + (r.valorMxn ?? 0), 0);

  function resolver(accion: 'autorizar' | 'rechazar', r: SalidaRotura) {
    decidir.mutate(
      { id: r.id, accion, motivo: motivoRechazo ?? undefined },
      {
        onSuccess: () => {
          toast.success(accion === 'autorizar' ? 'Rotura autorizada: el saldo ya bajó' : 'Rotura rechazada');
          setAutorizando(null);
          setRechazando(null);
          setMotivoRechazo(null);
        },
        onError: (e) => {
          toast.error(errorUsuario(e, 'No se pudo guardar la decisión'));
          setAutorizando(null);
        },
      },
    );
  }

  return (
    <div className="space-y-5">
      <PageHeader
        titulo="Salida por rotura"
        descripcion="Captura el material roto para que otro usuario autorizado apruebe su salida de existencias a costo promedio."
        acciones={
          puedeCapturar && (
            <Button onClick={() => setCapturando(true)}>
              <Plus className="mr-2 size-3.5" />
              Capturar rotura
            </Button>
          )
        }
      />

      <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
        <KpiCard
          etiqueta="Pendientes de autorizar"
          valor={query.isSuccess ? conteos[1] : <span className="text-ink-muted">—</span>}
          tono={conteos[1] > 0 ? 'warning' : 'success'}
          subdato="No bajan el saldo hasta autorizarse"
        />
        <KpiCard etiqueta="Autorizadas" valor={query.isSuccess ? conteos[2] : <span className="text-ink-muted">—</span>} tono="info" />
        <KpiCard
          etiqueta="Valor dado de baja a costo"
          valor={<MoneyDisplay amount={query.isSuccess ? valorAutorizado : null} currency="MXN" />}
          tono="neutral"
          subdato="Cantidad × costo promedio vigente"
        />
      </div>

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={[
              { valor: 1, label: 'Pendientes' },
              { valor: 2, label: 'Autorizadas' },
              { valor: 3, label: 'Rechazadas' },
            ]}
            valor={tab}
            onChange={setTab}
            conteos={query.isSuccess ? conteos : undefined}
            aria-label="Filtrar por estado"
          />
        }
      >
        {query.isError ? (
          <ErrorState
            title="No se pudieron cargar las salidas por rotura"
            problem={problemaUsuario(query.error)}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton rows={6} columns={[{ width: 'w-24' }, { width: 'w-32' }, { width: 'w-48' }, { width: 'w-20' }]} />
        ) : items.length === 0 ? (
          <EmptyState title="Sin roturas" description="No hay salidas por rotura con este filtro." />
        ) : (
          <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
            {conteos[1] > 0 && !puedeAutorizar && (
              <AvisoEnLinea tono="advertencia">
                Hay roturas pendientes. Solo un Jefe o Gerente de almacén con la facultad de autorizar puede aplicarlas o rechazarlas.
              </AvisoEnLinea>
            )}
            <TableContainer>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Fecha</TableHead>
                    <TableHead>Artículo</TableHead>
                    <TableHead>Ubicación</TableHead>
                    <TableHead className="text-right">Cantidad</TableHead>
                    <TableHead>Motivo</TableHead>
                    <TableHead>Capturó</TableHead>
                    <TableHead className="text-right">Valor</TableHead>
                    <TableHead>Estado</TableHead>
                    <TableHead>Acciones</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {items.map((r) => (
                    <TableRow key={r.id} className="hover:bg-surface-subtle">
                      <TableCell className="whitespace-nowrap">
                        <DateTimeDisplay value={r.fecha} variant="corta" />
                      </TableCell>
                      <TableCell>
                        <span className="font-mono text-xs">{r.articuloClave ?? r.articuloId}</span>
                        {r.articuloDescripcion ? ` · ${r.articuloDescripcion}` : ''}
                      </TableCell>
                      <TableCell className="font-mono text-xs">{r.ubicacionClave ?? '—'}</TableCell>
                      <TableCell className="text-right tabular-nums">
                        {num(r.cantidad)} {r.unidadMedida}
                      </TableCell>
                      <TableCell>{r.motivo}</TableCell>
                      <TableCell>{r.capturadoPor}</TableCell>
                      <TableCell className="text-right">{r.valorMxn === null ? '—' : <MoneyDisplay amount={r.valorMxn} currency="MXN" />}</TableCell>
                      <TableCell>
                        <Badge variant={ESTADO_UI[r.estado].variante}>{ESTADO_UI[r.estado].texto}</Badge>
                        {r.folioMovimiento && <div className="font-mono text-xs text-ink-muted">{r.folioMovimiento}</div>}
                      </TableCell>
                      <TableCell>
                        <div className="flex flex-wrap gap-2">
                          <Button size="sm" variant="ghost" onClick={() => setDetalle(r)}>
                            Detalle
                          </Button>
                          {r.estado === 'PENDIENTE' && puedeAutorizar && (
                            <>
                              <Button size="sm" onClick={() => setAutorizando(r)}>
                                Autorizar
                              </Button>
                              <Button size="sm" variant="outline" onClick={() => setRechazando(r)}>
                                Rechazar
                              </Button>
                            </>
                          )}
                          {r.estado === 'PENDIENTE' && !puedeAutorizar && (
                            <span className="self-center text-xs text-ink-muted">Requiere autorización</span>
                          )}
                        </div>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          </div>
        )}
      </ListadoModulo>

      <CapturarRoturaSheet open={capturando} onOpenChange={setCapturando} />

      <Sheet open={detalle !== null} onOpenChange={(abierto) => !abierto && setDetalle(null)}>
        <SheetContent side="right" size="lg">
          <SheetHeader>
            <SheetTitle>Detalle de la rotura</SheetTitle>
            <SheetDescription>
              {detalle ? `${detalle.articuloClave ?? ''} · ${num(detalle.cantidad)} ${detalle.unidadMedida} · ${detalle.motivo}` : ''}
            </SheetDescription>
          </SheetHeader>
          {detalle && (
            <SheetBody>
              <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
                <dt className="text-ink-muted">Ubicación</dt>
                <dd className="font-mono">{detalle.ubicacionClave ?? '—'}</dd>
                <dt className="text-ink-muted">Costo promedio</dt>
                <dd>{mxn(detalle.costoUnitarioMxn)}</dd>
                <dt className="text-ink-muted">Valor</dt>
                <dd>{mxn(detalle.valorMxn)}</dd>
                <dt className="text-ink-muted">Movimiento</dt>
                <dd className="font-mono">{detalle.folioMovimiento ?? 'Aún sin movimiento'}</dd>
                {detalle.comentario && (
                  <>
                    <dt className="text-ink-muted">Comentario</dt>
                    <dd>{detalle.comentario}</dd>
                  </>
                )}
              </dl>
              <h3 className="mt-6 text-sm font-semibold">Bitácora</h3>
              <ol className="mt-2 space-y-2 border-l border-line-row pl-4 text-sm">
                <li>
                  <span className="font-medium">Capturó</span> {detalle.capturadoPor}
                  <div className="text-xs text-ink-muted">
                    <DateTimeDisplay value={detalle.capturadoAt} />
                  </div>
                </li>
                {detalle.decididoPor && detalle.decididoAt && (
                  <li>
                    <span className="font-medium">{detalle.estado === 'RECHAZADA' ? 'Rechazó' : 'Autorizó'}</span> {detalle.decididoPor}
                    <div className="text-xs text-ink-muted">
                      <DateTimeDisplay value={detalle.decididoAt} />
                    </div>
                    {detalle.motivoRechazo && <div className="text-xs text-ink-muted">{detalle.motivoRechazo}</div>}
                  </li>
                )}
                {detalle.estado === 'PENDIENTE' && <li className="text-ink-muted">Pendiente de autorizar por otro usuario</li>}
              </ol>
            </SheetBody>
          )}
        </SheetContent>
      </Sheet>

      <ConfirmarAccionDialog
        open={autorizando !== null}
        onOpenChange={(abierto) => !abierto && setAutorizando(null)}
        titulo="Autorizar rotura"
        resumen={
          autorizando
            ? `${autorizando.articuloClave ?? ''} · ${num(autorizando.cantidad)} ${autorizando.unidadMedida} en ${autorizando.ubicacionClave ?? 'la ubicación'} (${autorizando.motivo}). El saldo baja a costo promedio vigente y se avisa a Contabilidad para la póliza de merma.`
            : ''
        }
        verbo="Autorizar rotura"
        tono="primario"
        pendiente={decidir.isPending}
        onConfirmar={() => autorizando && resolver('autorizar', autorizando)}
      />
      <ConfirmarAccionDialog
        open={rechazando !== null}
        onOpenChange={(abierto) => {
          if (!abierto) {
            setRechazando(null);
            setMotivoRechazo(null);
          }
        }}
        titulo="Rechazar rotura"
        resumen={
          <div className="space-y-2">
            <p>La rotura queda rechazada y el saldo no cambia. Indica el motivo.</p>
            <TextAreaField value={motivoRechazo} onChange={setMotivoRechazo} maxLength={500} minRows={2} />
          </div>
        }
        verbo="Rechazar rotura"
        tono="peligro"
        pendiente={decidir.isPending}
        onConfirmar={() => {
          if (!motivoRechazo?.trim()) {
            toast.error('Indica el motivo del rechazo.');
            return;
          }
          if (rechazando) resolver('rechazar', rechazando);
        }}
      />
    </div>
  );
}
