import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useForm, useWatch } from 'react-hook-form';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Checkbox } from '@/components/ui/checkbox';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetDescription,
  SheetFooter,
} from '@/components/ui/sheet';
import { FormaPagoSelector } from '@/components/erp/selectors/FormaPagoSelector';
import { FacturaPpdPicker } from '../components/FacturaPpdPicker';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  useReppPendientes,
  useReppPendiente,
  useAccionesReppPendiente,
} from '../api/useReppPendientes';
import type {
  EstadoPendiente,
  ReppPendiente,
  RelacionRepp,
  EmisionPendiente,
} from '../api/useReppPendientes';
import { bloqueoRelacion, formatoImporte, totalRelacion } from '../lib/repp-pendiente';

export function PendientesRepp() {
  const [estado, setEstado] = useState<EstadoPendiente | ''>('Pendiente');
  const [indicador, setIndicador] = useState('');
  const [offset, setOffset] = useState(0);
  const [seleccion, setSeleccion] = useState<string[]>([]);
  const [abierto, setAbierto] = useState<string | null>(null);
  const dirty = useRef(false);
  const onDirtyChange = useCallback((value: boolean) => {
    dirty.current = value;
  }, []);
  function cerrar(force = false) {
    if (
      force ||
      !dirty.current ||
      window.confirm('Hay cambios sin guardar. ¿Descartar los cambios y cerrar?')
    ) {
      dirty.current = false;
      setAbierto(null);
    }
  }
  const [resultados, setResultados] = useState<EmisionPendiente[]>([]);
  const puedeEscribir = useHasPermission(PermisosCanonicos.FacturacionReppEmitir);
  const query = useReppPendientes(estado, indicador, offset);
  const detalle = useReppPendiente(abierto);
  const { emitir } = useAccionesReppPendiente();
  const kpis = query.data?.kpis;
  const tarjetas = [
    ['Pendientes', kpis?.pendientes, ''],
    ['Cerca del plazo', kpis?.cercaDelPlazo, 'cerca'],
    ['Vencidos', kpis?.vencidos, 'vencidos'],
    ['Con error', kpis?.conError, 'error'],
  ] as const;
  const items = query.data?.items ?? [];
  function filtrar(nuevoEstado: EstadoPendiente | '', nuevoIndicador: string) {
    setEstado(nuevoEstado);
    setIndicador(nuevoIndicador);
    setOffset(0);
    setSeleccion([]);
  }
  async function emitirSeleccionados() {
    try {
      const respuesta = await emitir.mutateAsync({
        ids: seleccion,
        idempotencyKey: crypto.randomUUID(),
      });
      setResultados(respuesta);
      setSeleccion([]);
    } catch {
      toast.error(
        'No se pudo completar el lote. Actualiza la bandeja para consultar los resultados.',
      );
    }
  }
  return (
    <main className="flex h-full min-h-0 flex-col gap-4 px-6 pt-5 text-sm text-ink">
      <header className="flex items-center justify-between gap-4">
        <div>
          <h1 className="text-3xl font-semibold">Pendientes de REP</h1>
          <p className="text-ink-muted">
            Revisa los pagos bancarios confirmados y emite sus complementos de pago.
          </p>
        </div>
        {puedeEscribir && (
          <Button
            onClick={() => void emitirSeleccionados()}
            disabled={!seleccion.length || seleccion.length > 50 || emitir.isPending}
            title={
              !seleccion.length
                ? 'Selecciona hasta 50 pagos pendientes para emitir.'
                : emitir.isPending
                  ? 'La emisión está en curso.'
                  : 'Emitir los pagos seleccionados con su relación actual.'
            }
          >
            {emitir.isPending ? 'Emitiendo…' : `Emitir seleccionados (${seleccion.length})`}
          </Button>
        )}
      </header>
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {tarjetas.map(([label, count, filtro]) => (
          <Button
            key={label}
            variant="ghost"
            className="h-auto flex-col items-start gap-1 rounded-lg bg-surface-card px-4 py-3 shadow-card"
            aria-pressed={estado === 'Pendiente' && indicador === filtro}
            onClick={() => filtrar('Pendiente', filtro)}
          >
            <span className="text-xs text-ink-muted">{label}</span>
            <span className="text-3xl font-semibold tabular-nums">{count ?? '—'}</span>
          </Button>
        ))}
      </div>
      {resultados.length > 0 && (
        <div role="status" className="rounded-lg bg-surface-card p-3 shadow-card-flat">
          <p className="font-medium">Resultado del lote</p>
          <ul>
            {resultados.map((r) => (
              <li key={r.id}>
                <Button variant="link" onClick={() => setAbierto(r.id)}>
                  Ver pago
                </Button>
                {r.emitido ? 'REP emitido' : `${r.codigo}: ${r.mensaje}`}
              </li>
            ))}
          </ul>
        </div>
      )}
      <section
        className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-t-lg bg-surface-card shadow-card-flat"
        aria-label="Bandeja de pendientes de REP"
      >
        <Tabs
          value={estado || 'Todas'}
          onValueChange={(v) => filtrar(v === 'Todas' ? '' : (v as EstadoPendiente), '')}
        >
          <TabsList className="h-11 w-full justify-start rounded-none border-b border-line-divider bg-surface-card px-4">
            <TabsTrigger value="Pendiente">Pendientes</TabsTrigger>
            <TabsTrigger value="Emitido">Emitidos</TabsTrigger>
            <TabsTrigger value="Descartado">Descartados</TabsTrigger>
            <TabsTrigger value="Todas">Todas</TabsTrigger>
          </TabsList>
        </Tabs>
        {indicador && (
          <div className="flex items-center gap-2 border-b border-line-divider px-4 py-2">
            <span>Filtro: {tarjetas.find((t) => t[2] === indicador)?.[0]}</span>
            <Button size="sm" variant="ghost" onClick={() => filtrar(estado, '')}>
              Quitar filtro
            </Button>
          </div>
        )}
        <div className="min-h-0 flex-1 overflow-auto">
          {query.isError ? (
            <div role="alert" className="p-4">
              No se pudo cargar la bandeja.{' '}
              <Button variant="secondary" onClick={() => void query.refetch()}>
                Reintentar
              </Button>
            </div>
          ) : query.isLoading ? (
            <p role="status" className="p-4">
              Cargando pagos…
            </p>
          ) : !items.length ? (
            <p className="p-4 text-ink-muted">No hay pagos en esta vista.</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  {puedeEscribir && <TableHead>Seleccionar</TableHead>}
                  <TableHead>Cliente / referencia</TableHead>
                  <TableHead>Facturas</TableHead>
                  <TableHead>Fecha del pago</TableHead>
                  <TableHead className="text-right">Importe</TableHead>
                  <TableHead>Plazo</TableHead>
                  <TableHead>Estado</TableHead>
                  <TableHead>Acción</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((p) => (
                  <TableRow key={p.id} className="h-12">
                    {puedeEscribir && (
                      <TableCell>
                        <Checkbox
                          aria-label={`Seleccionar pago de ${p.clienteNombre}`}
                          disabled={
                            p.estado !== 'Pendiente' ||
                            emitir.isPending ||
                            (!seleccion.includes(p.id) && seleccion.length >= 50)
                          }
                          checked={seleccion.includes(p.id)}
                          onCheckedChange={(checked) =>
                            setSeleccion((s) =>
                              checked ? [...s, p.id] : s.filter((id) => id !== p.id),
                            )
                          }
                        />
                      </TableCell>
                    )}
                    <TableCell>
                      <p className="font-medium">{p.clienteNombre}</p>
                      <p className="font-mono text-xs text-ink-muted">
                        {p.referencia ?? 'Sin referencia'}
                      </p>
                    </TableCell>
                    <TableCell className="font-mono text-xs">
                      {p.facturas.map((f) => f.folio ?? '[FACTURA]').join(', ') || 'Por relacionar'}
                    </TableCell>
                    <TableCell>{p.fechaValor}</TableCell>
                    <TableCell className="text-right tabular-nums">
                      {formatoImporte(p.monto)} {p.moneda}
                    </TableCell>
                    <TableCell>
                      {p.fechaLimite}
                      <p className="text-xs">{p.estado === 'Pendiente' ? p.alerta : 'Resuelto'}</p>
                    </TableCell>
                    <TableCell>
                      <Badge
                        variant={
                          p.estado === 'Emitido'
                            ? 'success'
                            : p.estado === 'Descartado'
                              ? 'neutral'
                              : 'warning'
                        }
                      >
                        {p.estado}
                      </Badge>
                      {p.tcPorRegistrar && p.estado === 'Pendiente' && (
                        <p className="text-xs text-warning-fg">TC por registrar</p>
                      )}
                      {p.ultimoErrorMensaje && (
                        <p className="text-xs text-danger-fg" title={p.ultimoErrorMensaje}>
                          {p.ultimoErrorCodigo}
                        </p>
                      )}
                    </TableCell>
                    <TableCell>
                      <Button variant="link" onClick={() => setAbierto(p.id)}>
                        {puedeEscribir && p.estado === 'Pendiente' ? 'Revisar' : 'Ver'}
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </div>
        <footer className="flex h-11 items-center justify-between border-t border-line-divider px-4 text-xs text-ink-muted">
          <span>
            {query.data?.total
              ? `${offset + 1}–${Math.min(offset + 10, query.data.total)} de ${query.data.total}`
              : '0 pagos'}
          </span>
          <div className="flex gap-2">
            <Button
              size="sm"
              variant="secondary"
              disabled={offset === 0}
              title="Página anterior"
              onClick={() => setOffset((o) => Math.max(0, o - 10))}
            >
              Anterior
            </Button>
            <Button
              size="sm"
              variant="secondary"
              disabled={offset + 10 >= (query.data?.total ?? 0)}
              title="Página siguiente"
              onClick={() => setOffset((o) => o + 10)}
            >
              Siguiente
            </Button>
          </div>
        </footer>
      </section>
      <Sheet
        open={abierto !== null}
        onOpenChange={(open) => {
          if (!open) cerrar();
        }}
      >
        <SheetContent className="flex flex-col overflow-y-auto">
          <SheetHeader>
            <SheetTitle>Revisar pago confirmado</SheetTitle>
            <SheetDescription>
              Valida la relación antes de emitir el REP. El monto y la moneda confirmados son
              inmutables.
            </SheetDescription>
          </SheetHeader>
          {detalle.isLoading ? (
            <p className="px-4">Cargando revisión…</p>
          ) : detalle.isError ? (
            <p role="alert" className="px-4">
              No se pudo cargar el pago.
            </p>
          ) : (
            detalle.data && (
              <RevisionPago
                key={`${detalle.data.id}:${JSON.stringify(detalle.data)}`}
                pago={detalle.data}
                puedeEscribir={puedeEscribir}
                cerrar={cerrar}
                onDirtyChange={onDirtyChange}
              />
            )
          )}
        </SheetContent>
      </Sheet>
    </main>
  );
}

function RevisionPago({
  pago,
  puedeEscribir,
  cerrar,
  onDirtyChange,
}: {
  pago: ReppPendiente;
  puedeEscribir: boolean;
  cerrar: (force?: boolean) => void;
  onDirtyChange: (value: boolean) => void;
}) {
  const acciones = useAccionesReppPendiente();
  const [motivo, setMotivo] = useState('');
  const form = useForm<{ formaPago: string; facturas: RelacionRepp[] }>({
    defaultValues: { formaPago: pago.formaPago, facturas: pago.facturas },
  });
  useEffect(() => onDirtyChange(form.formState.isDirty), [form.formState.isDirty, onDirtyChange]);
  const formaPago = useWatch({ control: form.control, name: 'formaPago' });
  const facturas = useWatch({ control: form.control, name: 'facturas' });
  const ocupado =
    acciones.revisar.isPending || acciones.emitirUno.isPending || acciones.descartar.isPending;
  const editable = puedeEscribir && pago.estado === 'Pendiente' && !pago.intentoReciboPagoId;
  const bloqueo = bloqueoRelacion(pago.monto, formaPago, facturas);
  const bloqueoEmision =
    bloqueo ??
    (form.formState.isDirty ? 'Guarda la revisión antes de emitir.' : pago.bloqueos[0]) ??
    null;
  function cambiar(filas: RelacionRepp[]) {
    form.setValue('facturas', filas, { shouldDirty: true });
  }
  async function guardar() {
    if (bloqueo) return;
    try {
      await acciones.revisar.mutateAsync({ id: pago.id, formaPago, facturas });
      toast.success('Revisión guardada.');
    } catch {
      toast.error('No se pudo guardar. Verifica cliente, facturas PPD, saldo y monto confirmado.');
    }
  }
  async function emitir() {
    try {
      await acciones.emitirUno.mutateAsync({ id: pago.id, idempotencyKey: crypto.randomUUID() });
      toast.success('REP emitido.');
      cerrar(true);
    } catch {
      toast.error('No se emitió el REP. Consulta el último error del pago.');
    }
  }
  async function descartar() {
    try {
      await acciones.descartar.mutateAsync({ id: pago.id, motivo });
      toast.success('Pago descartado con motivo.');
      cerrar(true);
    } catch {
      toast.error('No se pudo descartar el pendiente.');
    }
  }
  return (
    <>
      <div className="space-y-5 px-4 text-sm">
        <fieldset className="space-y-2">
          <legend className="font-semibold">1. Datos del pago</legend>
          <dl className="grid grid-cols-2 gap-3 rounded-lg bg-surface-subtle p-3">
            <div>
              <dt className="text-xs text-ink-muted">Cliente</dt>
              <dd>{pago.clienteNombre}</dd>
            </div>
            <div>
              <dt className="text-xs text-ink-muted">Monto confirmado</dt>
              <dd className="tabular-nums">
                {formatoImporte(pago.monto)} {pago.moneda}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-ink-muted">Fecha valor</dt>
              <dd>{pago.fechaValor}</dd>
            </div>
            <div>
              <dt className="text-xs text-ink-muted">Fecha límite</dt>
              <dd>
                {pago.fechaLimite} · {pago.alerta}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-ink-muted">Referencia bancaria</dt>
              <dd className="font-mono text-xs">{pago.referencia ?? 'Sin referencia'}</dd>
            </div>
            <div>
              <dt className="text-xs text-ink-muted">Tipo de cambio del pago</dt>
              <dd>
                {pago.moneda === 'MXN'
                  ? 'No aplica (MXN)'
                  : (pago.tipoCambio ?? 'TC por registrar')}
              </dd>
            </div>
          </dl>
          <Label>Forma de pago</Label>
          {editable ? (
            <FormaPagoSelector
              value={formaPago}
              disabled={ocupado}
              onChange={(v) => form.setValue('formaPago', v ?? '', { shouldDirty: true })}
            />
          ) : (
            <p>{formaPago}</p>
          )}
          {!pago.revisado && <p className="text-xs text-warning-fg">Propuesta: 03 · confirmar</p>}
        </fieldset>
        <fieldset className="space-y-3">
          <legend className="font-semibold">2. Relación de facturas</legend>
          {facturas.map((f, index) => (
            <div key={f.facturaVentaId} className="space-y-2 rounded-lg bg-surface-subtle p-3">
              <p className="font-mono text-xs">{f.folio ?? '[FACTURA POR IDENTIFICAR]'}</p>
              <Label htmlFor={`importe-${index}`}>Importe aplicado</Label>
              <div className="flex gap-2">
                <Input
                  id={`importe-${index}`}
                  type="number"
                  min="0"
                  step="0.01"
                  value={f.importe}
                  disabled={!editable || ocupado}
                  onChange={(e) =>
                    cambiar(
                      facturas.map((fila, i) =>
                        i === index ? { ...fila, importe: Number(e.target.value) } : fila,
                      ),
                    )
                  }
                />
                {editable && (
                  <Button
                    variant="secondary-danger"
                    disabled={ocupado}
                    title={ocupado ? 'Operación en curso.' : 'Quitar esta factura de la relación.'}
                    onClick={() => cambiar(facturas.filter((_, i) => i !== index))}
                  >
                    Quitar
                  </Button>
                )}
              </div>
            </div>
          ))}
          {editable && (
            <FacturaPpdPicker
              value={null}
              receptorRfc={pago.clienteRfc}
              excluirIds={facturas.map((f) => f.facturaVentaId)}
              disabled={ocupado}
              onChange={(f) => {
                if (f)
                  cambiar([
                    ...facturas,
                    {
                      facturaVentaId: f.facturaVentaId,
                      folio: f.folio,
                      importe: Math.min(f.saldo, Math.max(0, pago.monto - totalRelacion(facturas))),
                    },
                  ]);
              }}
            />
          )}
          <p className="text-right tabular-nums">
            Total relacionado: {formatoImporte(totalRelacion(facturas))} {pago.moneda}
          </p>
        </fieldset>
        {pago.ultimoErrorMensaje && (
          <p role="alert" className="rounded-md bg-danger-bg p-3 text-danger-fg">
            {pago.ultimoErrorCodigo}: {pago.ultimoErrorMensaje}
          </p>
        )}
        {pago.estado === 'Pendiente' && (bloqueoEmision || pago.bloqueos.length > 0) && (
          <div role="note" className="rounded-md bg-warning-note-bg p-3 text-warning-note-fg">
            {bloqueoEmision}
            <ul>
              {pago.bloqueos
                .filter((b) => b !== bloqueoEmision)
                .map((b) => (
                  <li key={b}>{b}</li>
                ))}
            </ul>
          </div>
        )}
        {(pago.reciboPagoId || pago.intentoReciboPagoId) && (
          <Link
            className="text-brand underline"
            to="/facturacion/repp/$id"
            params={{ id: (pago.reciboPagoId ?? pago.intentoReciboPagoId)! }}
          >
            Ver comprobante e intento de timbrado
          </Link>
        )}
        {pago.motivoDescarte && <p>Motivo de descarte: {pago.motivoDescarte}</p>}
        {editable && (
          <fieldset className="space-y-2">
            <legend className="font-semibold">Descartar si no requiere REP</legend>
            <Label htmlFor="motivo-descarte">Motivo obligatorio</Label>
            <Input
              id="motivo-descarte"
              value={motivo}
              maxLength={1000}
              onChange={(e) => setMotivo(e.target.value)}
              disabled={ocupado}
            />
            <Button
              variant="secondary-danger"
              onClick={() => void descartar()}
              disabled={!motivo.trim() || ocupado}
              title={
                !motivo.trim()
                  ? 'Escribe el motivo del descarte.'
                  : 'Descartar el pendiente conservando el pago confirmado.'
              }
            >
              Descartar pendiente
            </Button>
          </fieldset>
        )}
      </div>
      <SheetFooter className="mt-auto flex-row justify-end border-t border-line-divider">
        <Button variant="ghost" onClick={() => cerrar()}>
          Cerrar
        </Button>
        {editable && (
          <>
            <Button
              variant="secondary"
              onClick={() => void guardar()}
              disabled={!!bloqueo || ocupado}
              title={
                bloqueo ??
                (ocupado ? 'Operación en curso.' : 'Confirmar la forma de pago y la relación.')
              }
            >
              Guardar revisión
            </Button>
            <Button
              onClick={() => void emitir()}
              disabled={!!bloqueoEmision || ocupado}
              title={
                bloqueoEmision ?? (ocupado ? 'Operación en curso.' : 'Emitir el REP de este pago.')
              }
            >
              Emitir REP
            </Button>
          </>
        )}
      </SheetFooter>
    </>
  );
}
