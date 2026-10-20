import { IndicadorAlmacenBadge } from '../components/IndicadorAlmacenBadge';
import { MontoMovimiento } from '@/components/erp/display/MontoMovimiento';
import { DialogBody } from '@/components/ui/dialog';
import { PageHeader } from '@/components/erp/PageHeader';
import { useMemo, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Link, useParams } from '@tanstack/react-router';
import { ArrowLeft, Calculator, Check, Lock, RotateCcw, Wallet, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { Input } from '@/components/ui/input';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { TextAreaField } from '@/components/erp';
import { ErrorState, TableSkeleton } from '@/components/erp';
import {
  EtiquetaArticulo,
  EtiquetaSubAlmacen,
} from '@/features/almacen/components/EtiquetasConteo';
import { useConteo } from '@/features/almacen/api/useConteos';
import {
  useAgregarRecuento,
  useAplicarConteo,
  useAprobarConteo,
  useAprobarLineaIndividualmente,
  useEvaluarVariaciones,
  useLineasComparacion,
  useRechazarConteo,
} from '@/features/almacen/api/useAprobacionConteo';
import {
  EstadoConteo,
  TipoConteo,
  type LineaConteoComparacionDto,
} from '@/features/almacen/api/types';
import {
  AgregarRecuentoSchema,
  AprobarLineaIndividualmenteSchema,
  RechazarConteoSchema,
  type AgregarRecuentoValues,
  type AprobarLineaIndividualmenteValues,
  type RechazarConteoValues,
} from '@/features/almacen/schemas/aprobacion-conteo';
import { esApiError, useFormIdempotencyKey, codigoSoporte } from '@/lib/api';
import { VariacionBadge } from '@/features/almacen/components/VariacionBadge';
import { Field } from '@/features/almacen/components/internal/Field';
import { montoNetoConteo } from '@/features/almacen/lib/monto-neto-conteo';
import { EstadoAlmacenBadge } from '@/features/almacen/components/EstadoAlmacenBadge';
import { formatMoney, mxn } from '@/lib/money';
import { formatCantidad } from '@/lib/format-cantidad';

const FROM = '/_app/almacen/inventarios/$id/aprobacion' as const;

/**
 * <c>P10 — Aprobación de conteo</c> (doc 07 §FE-F5-PR2, doc 00 §A7-A8).
 * Pantalla del aprobador con cantidad teórica + variaciones + valor.
 * Permiso <c>almacen.inventarios.aprobar-nivel1</c>.
 *
 * <para>Acciones por línea:</para>
 * <list>
 *   <item>Agregar recuento (vuelve la línea al contador).</item>
 *   <item>Aprobar individualmente con justificación (líneas dentro de
 *     umbral o ya conciliadas).</item>
 * </list>
 *
 * <para>Acciones globales:</para>
 * <list>
 *   <item>Evaluar variaciones (aplica reglas A7 — marca para recuento).</item>
 *   <item>Aprobar conteo (firma; requiere todas las líneas resueltas).</item>
 *   <item>Rechazar conteo con motivo.</item>
 *   <item>Aplicar (genera movimientos Ajuste +/- y publica evento).</item>
 * </list>
 */
export function AprobacionConteoPage() {
  const { id } = useParams({ from: FROM });
  const conteoQuery = useConteo(id);
  const comparacionQuery = useLineasComparacion(id);

  const evaluar = useEvaluarVariaciones();
  const aprobar = useAprobarConteo();
  const rechazar = useRechazarConteo();
  const aplicar = useAplicarConteo();

  const [recuentoLineaId, setRecuentoLineaId] = useState<string | null>(null);
  const [aprobarLineaId, setAprobarLineaId] = useState<string | null>(null);
  const [rechazarOpen, setRechazarOpen] = useState(false);
  const [aprobarConteoOpen, setAprobarConteoOpen] = useState(false);
  const [aplicarOpen, setAplicarOpen] = useState(false);

  const aplicarIdempotencyKey = useFormIdempotencyKey();

  // Mismo criterio que el backend al aprobar: requiere recuento, sin recuento
  // ni aprobación individual.
  const pendientesRecuento = useMemo(
    () =>
      (comparacionQuery.data ?? []).filter(
        (l) => l.requiereRecuento && !l.aprobadoIndividualmente && !l.recuentoRealizado,
      ).length,
    [comparacionQuery.data],
  );
  const motivoNoAprobar =
    pendientesRecuento > 0
      ? `${pendientesRecuento} ${pendientesRecuento === 1 ? 'partida pendiente' : 'partidas pendientes'} de recuento o aprobación individual`
      : null;

  const montoNeto = useMemo(
    () => montoNetoConteo(comparacionQuery.data ?? []),
    [comparacionQuery.data],
  );

  function ejecutarEvaluar() {
    if (!id) return;
    evaluar.mutate(
      { conteoId: id },
      {
        onSuccess: (resp) => {
          toast.success(`${resp.lineasMarcadasParaRecuento} partidas marcadas para recuento (A7)`);
        },
        onError: manejarError,
      },
    );
  }

  function ejecutarAprobar() {
    if (!id) return;
    aprobar.mutate(
      { conteoId: id },
      {
        onSuccess: () => {
          toast.success('Conteo aprobado');
          setAprobarConteoOpen(false);
        },
        onError: manejarError,
      },
    );
  }

  function ejecutarAplicar() {
    if (!id) return;
    aplicar.mutate(
      { conteoId: id, idempotencyKey: aplicarIdempotencyKey },
      {
        onSuccess: (resp) => {
          toast.success(
            `${resp.movimientosGenerados} movimientos aplicados (monto neto ${formatMoney(mxn(resp.montoNetoMxn))})`,
          );
          setAplicarOpen(false);
        },
        onError: manejarError,
      },
    );
  }

  const conteo = conteoQuery.data;
  const enConciliacion = conteo?.estado === EstadoConteo.EnConciliacion;
  const aprobado = conteo?.estado === EstadoConteo.Aprobado;
  const aplicado = conteo?.estado === EstadoConteo.Aplicado;

  return (
    <div className="space-y-5">
      <div className="flex items-center justify-between gap-2">
        <Button asChild variant="ghost" size="sm">
          <Link to="/almacen/inventarios/$id" params={{ id: id ?? '' }}>
            <ArrowLeft className="mr-2 size-3.5" />
            Volver al detalle
          </Link>
        </Button>
      </div>

      {conteo?.tipo === TipoConteo.Anual &&
        (enConciliacion || conteo.estado === EstadoConteo.EnCurso) && (
          <BloqueoInventarioAnualBanner />
        )}

      {conteoQuery.isError ? (
        <ErrorState
          title="No se pudo cargar el conteo"
          problem={esApiError(conteoQuery.error) ? conteoQuery.error.problem : undefined}
          onRetry={() => conteoQuery.refetch()}
        />
      ) : comparacionQuery.isError ? (
        <ErrorState
          title="No se pudo cargar la comparación"
          problem={esApiError(comparacionQuery.error) ? comparacionQuery.error.problem : undefined}
          onRetry={() => comparacionQuery.refetch()}
        />
      ) : conteoQuery.isLoading || comparacionQuery.isLoading ? (
        <TableSkeleton
          rows={8}
          columns={[
            { width: 'w-8' },
            { width: 'w-64' },
            { width: 'w-24' },
            { width: 'w-24' },
            { width: 'w-24' },
            { width: 'w-32' },
            { width: 'w-32' },
          ]}
        />
      ) : (
        <>
          <PageHeader
            titulo="Aprobación de conteo"
            estado={<>{conteo && <EstadoAlmacenBadge tipo="conteo" estado={conteo.estado} />}</>}
            descripcion={
              <>
                Monto neto del ajuste:{' '}
                <span className="font-semibold">
                  <MontoMovimiento
                    monto={montoNeto}
                    moneda="MXN"
                    tipo={montoNeto < 0 ? 'salida' : 'entrada'}
                  />
                </span>
              </>
            }
            acciones={
              <>
                <div className="flex flex-wrap gap-2">
                  {enConciliacion && (
                    <>
                      <Button variant="outline" onClick={ejecutarEvaluar}>
                        <Calculator className="mr-2 size-3.5" />
                        Evaluar variaciones
                      </Button>
                      <Button
                        onClick={() => setAprobarConteoOpen(true)}
                        disabled={motivoNoAprobar != null}
                        aria-describedby={motivoNoAprobar ? 'motivo-no-aprobar' : undefined}
                      >
                        <Check className="mr-2 size-3.5" />
                        Aprobar conteo
                      </Button>
                      <Button variant="secondary-danger" onClick={() => setRechazarOpen(true)}>
                        <X className="mr-2 size-3.5" />
                        Rechazar
                      </Button>
                    </>
                  )}
                  {enConciliacion && motivoNoAprobar && (
                    <p id="motivo-no-aprobar" className="basis-full text-sm text-ink-muted">
                      No puedes aprobar aún: {motivoNoAprobar}.
                    </p>
                  )}
                  {aprobado && (
                    <Button onClick={() => setAplicarOpen(true)}>
                      <Wallet className="mr-2 size-3.5" />
                      Aplicar (generar movimientos)
                    </Button>
                  )}
                  {aplicado && (
                    <p className="text-sm text-success-fg flex items-center gap-2">
                      <Lock className="h-4 w-4" />
                      Conteo aplicado. Ajustes generados en el inventario.
                    </p>
                  )}
                </div>
              </>
            }
          />

          <TablaComparacion
            items={comparacionQuery.data ?? []}
            puedeAccionar={enConciliacion}
            onRecuento={setRecuentoLineaId}
            onAprobarLinea={setAprobarLineaId}
          />
        </>
      )}

      {/* Dialogs */}
      {recuentoLineaId && id && (
        <RecuentoDialog
          conteoId={id}
          lineaId={recuentoLineaId}
          linea={comparacionQuery.data?.find((l) => l.id === recuentoLineaId)}
          open={recuentoLineaId != null}
          onOpenChange={(v) => !v && setRecuentoLineaId(null)}
        />
      )}
      {aprobarLineaId && id && (
        <AprobarLineaDialog
          conteoId={id}
          lineaId={aprobarLineaId}
          open={aprobarLineaId != null}
          onOpenChange={(v) => !v && setAprobarLineaId(null)}
        />
      )}
      {id && (
        <RechazarConteoDialog
          conteoId={id}
          open={rechazarOpen}
          onOpenChange={setRechazarOpen}
          onSuccess={() => {
            rechazar.reset();
            setRechazarOpen(false);
          }}
        />
      )}
      <AlertDialog open={aprobarConteoOpen} onOpenChange={setAprobarConteoOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>¿Aprobar conteo?</AlertDialogTitle>
            <AlertDialogDescription>
              Confirmas tu aprobación. Todas las partidas deben estar resueltas (recuento o
              aprobadas individualmente). El conteo pasará a estado <b>Aprobado</b> y podrás
              aplicarlo después.
              <span className="mt-2 block text-ink-secondary">
                El nivel requerido depende del monto neto y de los umbrales guardados al iniciar
                este conteo. El Nivel 3 genera un aviso a Finanzas.
              </span>
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={aprobar.isPending}>Cancelar</AlertDialogCancel>
            <AlertDialogAction disabled={aprobar.isPending} onClick={ejecutarAprobar}>
              {aprobar.isPending ? 'Aprobando…' : 'Aprobar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <AlertDialog open={aplicarOpen} onOpenChange={setAplicarOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>¿Aplicar conteo?</AlertDialogTitle>
            <AlertDialogDescription>
              Se generarán los movimientos AjustePositivo/AjusteNegativo en batch y los saldos se
              actualizarán. Esta acción es irreversible. Asegúrate de haber confirmado el resultado
              del conteo y la justificación de cada variación.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={aplicar.isPending}>Cancelar</AlertDialogCancel>
            <AlertDialogAction disabled={aplicar.isPending} onClick={ejecutarAplicar}>
              {aplicar.isPending ? 'Aplicando…' : 'Aplicar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

// ─── Banner bloqueo inventario anual ────────────────────────────────────────

function BloqueoInventarioAnualBanner() {
  return (
    <div className="flex items-start gap-3 rounded-md border border-danger bg-danger-bg px-4 py-3 text-sm">
      <Lock className="mt-0.5 h-5 w-5 shrink-0 text-danger-fg" />
      <div>
        <p className="font-medium text-danger-fg">Inventario anual — salidas bloqueadas</p>
        <p className="text-danger-fg">
          Mientras este conteo esté activo, los almacenes no pueden surtir salidas. Coordina con
          producción y termina el ciclo lo antes posible.
        </p>
      </div>
    </div>
  );
}

// ─── Tabla de comparación ───────────────────────────────────────────────────

function TablaComparacion({
  items,
  puedeAccionar,
  onRecuento,
  onAprobarLinea,
}: {
  items: readonly LineaConteoComparacionDto[];
  puedeAccionar: boolean;
  onRecuento: (lineaId: string) => void;
  onAprobarLinea: (lineaId: string) => void;
}) {
  if (items.length === 0) {
    return (
      <div className="rounded-md border bg-surface-muted/30 px-4 py-6 text-center text-sm text-ink-muted">
        Sin partidas para comparar.
      </div>
    );
  }

  return (
    <TableContainer>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Artículo</TableHead>
            <TableHead>Sub-almacén</TableHead>
            <TableHead>Rack</TableHead>
            <TableHead className="text-right">Teórica</TableHead>
            <TableHead className="text-right">Real</TableHead>
            <TableHead>Variación</TableHead>
            <TableHead>Estado</TableHead>
            {puedeAccionar && <TableHead className="text-right">Acciones</TableHead>}
          </TableRow>
        </TableHeader>
        <TableBody>
          {items.map((l) => (
            <TableRow key={l.id}>
              <TableCell>
                <EtiquetaArticulo
                  articuloId={l.articuloId}
                  clave={l.articuloClave}
                  descripcion={l.articuloDescripcion}
                />
              </TableCell>
              <TableCell>
                <EtiquetaSubAlmacen subAlmacenId={l.subAlmacenId} clave={l.subAlmacenClave} />
              </TableCell>
              <TableCell className="font-mono text-xs">{l.ubicacionClave}</TableCell>
              <TableCell className="text-right font-mono">
                {formatCantidad(l.cantidadTeorica)}
              </TableCell>
              <TableCell className="text-right font-mono">
                {l.cantidadVigente != null ? formatCantidad(l.cantidadVigente) : '—'}
                {l.recuentoRealizado && l.cantidadRealCapturada != null && (
                  <div className="text-xs font-normal text-ink-muted">
                    Captura original: {formatCantidad(l.cantidadRealCapturada)}
                  </div>
                )}
              </TableCell>
              <TableCell>
                <VariacionBadge
                  variacionAbsoluta={l.variacionAbsoluta}
                  variacionPorcentaje={l.variacionPorcentaje}
                  variacionValorMxn={l.variacionValorMxn}
                  requiereRecuento={l.requiereRecuento}
                />
              </TableCell>
              <TableCell>
                {l.aprobadoIndividualmente ? (
                  <IndicadorAlmacenBadge estado="aprobada" />
                ) : l.requiereRecuento ? (
                  <IndicadorAlmacenBadge estado="pendiente" />
                ) : (
                  <span className="text-xs text-ink-muted">OK</span>
                )}
              </TableCell>
              {puedeAccionar && (
                <TableCell className="text-right">
                  <div className="flex items-center justify-end gap-1">
                    <Button
                      type="button"
                      size="sm"
                      variant="ghost"
                      onClick={() => onRecuento(l.id)}
                      aria-label={`Recuento partida ${l.id}`}
                    >
                      <RotateCcw className="h-4 w-4" />
                    </Button>
                    {!l.aprobadoIndividualmente && (
                      <Button
                        type="button"
                        size="sm"
                        variant="ghost"
                        onClick={() => onAprobarLinea(l.id)}
                        aria-label={`Aprobar partida ${l.id}`}
                      >
                        <Check className="h-4 w-4" />
                      </Button>
                    )}
                  </div>
                </TableCell>
              )}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  );
}

// ─── Dialog: Recuento ───────────────────────────────────────────────────────

function RecuentoDialog({
  conteoId,
  lineaId,
  linea,
  open,
  onOpenChange,
}: {
  conteoId: string;
  lineaId: string;
  linea?: LineaConteoComparacionDto;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const idempotencyKey = useFormIdempotencyKey();
  const agregar = useAgregarRecuento();
  const form = useForm<AgregarRecuentoValues>({
    resolver: zodResolver(AgregarRecuentoSchema),
    defaultValues: { cantidadRecontada: 0 },
  });

  function onSubmit(values: AgregarRecuentoValues) {
    agregar.mutate(
      {
        command: { conteoId, lineaId, cantidadRecontada: values.cantidadRecontada },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Recuento #${resp.secuencia} agregado`);
          onOpenChange(false);
          form.reset({ cantidadRecontada: 0 });
        },
        onError: manejarError,
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent size="sm">
        <DialogHeader>
          <DialogTitle>Agregar recuento</DialogTitle>
          <DialogDescription>
            Captura la cantidad recontada por el contador en una segunda pasada. El sistema vuelve a
            calcular la variación.
            {linea && (
              <span className="mt-1 block font-medium text-ink">
                {linea.articuloClave ?? linea.articuloId.slice(0, 8)}
                {linea.articuloDescripcion ? ` · ${linea.articuloDescripcion}` : ''}
                {' · '}rack {linea.ubicacionClave}
                {linea.unidadMedida ? ` · en ${linea.unidadMedida}` : ''}
              </span>
            )}
          </DialogDescription>
        </DialogHeader>
        <DialogBody>
          <form
            id="recuento-form"
            onSubmit={form.handleSubmit(onSubmit)}
            noValidate
            className="space-y-4"
          >
            <Field
              label="Cantidad recontada"
              required
              error={form.formState.errors.cantidadRecontada?.message}
            >
              <Controller
                name="cantidadRecontada"
                control={form.control}
                render={({ field }) => (
                  <Input
                    type="number"
                    inputMode="decimal"
                    step="0.0001"
                    min="0"
                    value={field.value ?? ''}
                    onChange={(e) =>
                      field.onChange(e.target.value === '' ? '' : Number(e.target.value))
                    }
                    autoFocus
                  />
                )}
              />
            </Field>
          </form>
        </DialogBody>
        <DialogFooter>
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpenChange(false)}
            disabled={agregar.isPending}
          >
            Cancelar
          </Button>
          <Button type="submit" form="recuento-form" disabled={agregar.isPending}>
            {agregar.isPending ? 'Agregando…' : 'Agregar recuento'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ─── Dialog: Aprobar línea ──────────────────────────────────────────────────

function AprobarLineaDialog({
  conteoId,
  lineaId,
  open,
  onOpenChange,
}: {
  conteoId: string;
  lineaId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const aprobar = useAprobarLineaIndividualmente();
  const form = useForm<AprobarLineaIndividualmenteValues>({
    resolver: zodResolver(AprobarLineaIndividualmenteSchema),
    defaultValues: { justificacion: '' },
  });

  function onSubmit(values: AprobarLineaIndividualmenteValues) {
    aprobar.mutate(
      {
        command: {
          conteoId,
          lineaId,
          justificacion: values.justificacion,
        },
      },
      {
        onSuccess: () => {
          toast.success('Partida aprobada');
          onOpenChange(false);
          form.reset({ justificacion: '' });
        },
        onError: manejarError,
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent size="sm">
        <DialogHeader>
          <DialogTitle>Aprobar partida individualmente</DialogTitle>
          <DialogDescription>
            Captura la justificación. Aplica para partidas dentro de umbral A7 o ya conciliadas con
            recuento.
          </DialogDescription>
        </DialogHeader>
        <DialogBody>
          <form
            id="aprobar-linea-form"
            onSubmit={form.handleSubmit(onSubmit)}
            noValidate
            className="space-y-4"
          >
            <Field
              label="Justificación"
              required
              error={form.formState.errors.justificacion?.message}
            >
              <Controller
                name="justificacion"
                control={form.control}
                render={({ field }) => (
                  <TextAreaField
                    value={field.value ?? null}
                    onChange={(v) => field.onChange(v ?? '')}
                    maxLength={500}
                    minRows={3}
                  />
                )}
              />
            </Field>
          </form>
        </DialogBody>
        <DialogFooter>
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpenChange(false)}
            disabled={aprobar.isPending}
          >
            Cancelar
          </Button>
          <Button type="submit" form="aprobar-linea-form" disabled={aprobar.isPending}>
            {aprobar.isPending ? 'Aprobando…' : 'Aprobar partida'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ─── Dialog: Rechazar conteo ────────────────────────────────────────────────

function RechazarConteoDialog({
  conteoId,
  open,
  onOpenChange,
  onSuccess,
}: {
  conteoId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess: () => void;
}) {
  const rechazar = useRechazarConteo();
  const form = useForm<RechazarConteoValues>({
    resolver: zodResolver(RechazarConteoSchema),
    defaultValues: { motivo: '' },
  });

  function onSubmit(values: RechazarConteoValues) {
    rechazar.mutate(
      { command: { conteoId, motivo: values.motivo } },
      {
        onSuccess: () => {
          toast.success('Conteo rechazado');
          onSuccess();
          form.reset({ motivo: '' });
        },
        onError: manejarError,
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent size="sm">
        <DialogHeader>
          <DialogTitle>Rechazar conteo</DialogTitle>
          <DialogDescription>
            Captura el motivo. El conteo se cierra como Rechazado y no generará ajustes.
          </DialogDescription>
        </DialogHeader>
        <DialogBody>
          <form
            id="rechazar-conteo-form"
            onSubmit={form.handleSubmit(onSubmit)}
            noValidate
            className="space-y-4"
          >
            <Field label="Motivo" required error={form.formState.errors.motivo?.message}>
              <Controller
                name="motivo"
                control={form.control}
                render={({ field }) => (
                  <TextAreaField
                    value={field.value ?? null}
                    onChange={(v) => field.onChange(v ?? '')}
                    maxLength={500}
                    minRows={3}
                  />
                )}
              />
            </Field>
          </form>
        </DialogBody>
        <DialogFooter>
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpenChange(false)}
            disabled={rechazar.isPending}
          >
            Cancelar
          </Button>
          <Button
            type="submit"
            form="rechazar-conteo-form"
            disabled={rechazar.isPending}
            variant="destructive"
          >
            {rechazar.isPending ? 'Rechazando…' : 'Rechazar'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ─── Helpers ────────────────────────────────────────────────────────────────

const MENSAJES_APROBACION: Record<string, string> = {
  CONTEO_APROBAR_LINEAS_PENDIENTES_RECUENTO:
    'Hay partidas pendientes: haz el recuento o apruébalas una por una antes de aprobar el conteo.',
  CONTEO_NIVEL_APROBACION_INSUFICIENTE:
    'Tu permiso no alcanza el nivel que pide el monto neto del ajuste. Pide a un aprobador de nivel superior que apruebe el conteo.',
};

function manejarError(error: unknown) {
  if (esApiError(error)) {
    const mensaje = error.code ? MENSAJES_APROBACION[error.code] : undefined;
    if (mensaje) {
      toast.error(mensaje, {
        description: codigoSoporte(error),
      });
      return;
    }
    toast.error(error.problem.title, {
      description: error.problem.detail ?? codigoSoporte(error),
    });
    return;
  }
  toast.error('Error inesperado.');
}
