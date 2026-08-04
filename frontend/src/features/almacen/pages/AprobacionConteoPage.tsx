import { useMemo, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Link, useParams } from '@tanstack/react-router';
import {
  AlertTriangle,
  ArrowLeft,
  Calculator,
  Check,
  Lock,
  RotateCcw,
  Wallet,
  X,
} from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
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
  EstadoConteoLabels,
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
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { VariacionBadge } from '@/features/almacen/components/VariacionBadge';
import { Field } from '@/features/almacen/components/internal/Field';
import { cn } from '@/lib/utils';

const FROM = '/_app/almacen/inventarios/$id/aprobacion' as const;
const UMBRAL_MUY_GRANDE_MXN = 10_000;

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

  const totalVariacionAbs = useMemo(() => {
    const items = comparacionQuery.data ?? [];
    return items.reduce(
      (acc, l) => acc + Math.abs(l.variacionValorMxn ?? 0),
      0,
    );
  }, [comparacionQuery.data]);

  const tieneVariacionMuyGrande = totalVariacionAbs >= UMBRAL_MUY_GRANDE_MXN;

  function ejecutarEvaluar() {
    if (!id) return;
    evaluar.mutate(
      { conteoId: id },
      {
        onSuccess: (resp) => {
          toast.success(
            `${resp.lineasMarcadasParaRecuento} líneas marcadas para recuento (A7)`,
          );
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
            `${resp.movimientosGenerados} movimientos aplicados (monto neto ${formatearMonto(resp.montoNetoMxn)})`,
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
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-2">
        <Button asChild variant="ghost" size="sm">
          <Link to="/almacen/inventarios/$id" params={{ id: id ?? '' }}>
            <ArrowLeft className="mr-2 h-4 w-4" />
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
          problem={
            esApiError(conteoQuery.error)
              ? conteoQuery.error.problem
              : undefined
          }
          onRetry={() => conteoQuery.refetch()}
        />
      ) : comparacionQuery.isError ? (
        <ErrorState
          title="No se pudo cargar la comparación"
          problem={
            esApiError(comparacionQuery.error)
              ? comparacionQuery.error.problem
              : undefined
          }
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
          <header className="space-y-2">
            <div className="flex flex-wrap items-center gap-3">
              <h1 className="text-2xl font-semibold tracking-tight">
                Aprobación de conteo
              </h1>
              {conteo && (
                <span className="inline-flex items-center rounded-full bg-slate-200 px-2 py-0.5 text-xs font-medium text-slate-700">
                  {EstadoConteoLabels[conteo.estado]}
                </span>
              )}
            </div>
            <p className="text-sm text-muted-foreground">
              Variación total absoluta:{' '}
              <span className="font-mono font-semibold">
                {formatearMonto(totalVariacionAbs)}
              </span>
              {tieneVariacionMuyGrande && (
                <span className="ml-2 inline-flex items-center gap-1 text-rose-700">
                  <AlertTriangle className="h-3 w-3" />
                  Excede umbral $10K — requiere aprobación elevada (A8)
                </span>
              )}
            </p>

            <div className="flex flex-wrap gap-2">
              {enConciliacion && (
                <>
                  <Button variant="outline" onClick={ejecutarEvaluar}>
                    <Calculator className="mr-2 h-4 w-4" />
                    Evaluar variaciones (A7)
                  </Button>
                  <Button onClick={() => setAprobarConteoOpen(true)}>
                    <Check className="mr-2 h-4 w-4" />
                    Aprobar conteo
                  </Button>
                  <Button
                    variant="destructive"
                    onClick={() => setRechazarOpen(true)}
                  >
                    <X className="mr-2 h-4 w-4" />
                    Rechazar
                  </Button>
                </>
              )}
              {aprobado && (
                <Button onClick={() => setAplicarOpen(true)}>
                  <Wallet className="mr-2 h-4 w-4" />
                  Aplicar (generar movimientos)
                </Button>
              )}
              {aplicado && (
                <p className="text-sm text-emerald-700 flex items-center gap-2">
                  <Lock className="h-4 w-4" />
                  Conteo aplicado. Ajustes generados en el inventario.
                </p>
              )}
            </div>
          </header>

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
      <AlertDialog
        open={aprobarConteoOpen}
        onOpenChange={setAprobarConteoOpen}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>¿Aprobar conteo?</AlertDialogTitle>
            <AlertDialogDescription>
              Confirmas tu aprobación. Todas las líneas deben estar resueltas
              (recuento o aprobadas individualmente). El conteo pasará a
              estado <b>Aprobado</b> y podrás aplicarlo después.
              {tieneVariacionMuyGrande && (
                <span className="mt-2 block text-rose-700">
                  <b>Variación total ≥ $10K</b>: este conteo cae en el
                  umbral A8 de "aprobación elevada". Confirma que tienes el
                  rol/permiso adecuado antes de continuar.
                </span>
              )}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={aprobar.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              disabled={aprobar.isPending}
              onClick={ejecutarAprobar}
            >
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
              Se generarán los movimientos AjustePositivo/AjusteNegativo en
              batch y los saldos se actualizarán. Esta acción es
              irreversible. Asegúrate de haber confirmado el resultado del
              conteo y la justificación de cada variación.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={aplicar.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              disabled={aplicar.isPending}
              onClick={ejecutarAplicar}
            >
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
    <div className="flex items-start gap-3 rounded-md border border-rose-200 bg-rose-50 px-4 py-3 text-sm">
      <Lock className="mt-0.5 h-5 w-5 shrink-0 text-rose-700" />
      <div>
        <p className="font-medium text-rose-900">
          Inventario anual — salidas bloqueadas (A18)
        </p>
        <p className="text-rose-800">
          Mientras este conteo esté activo, los almacenes no pueden surtir
          salidas. Coordina con producción y termina el ciclo lo antes
          posible.
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
      <div className="rounded-md border bg-muted/30 px-4 py-6 text-center text-sm text-muted-foreground">
        Sin líneas para comparar.
      </div>
    );
  }

  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-sm">
        <thead className="bg-muted/50">
          <tr>
            <th className="px-3 py-2 text-left">Artículo</th>
            <th className="px-3 py-2 text-left">Sub-almacén</th>
            <th className="px-3 py-2 text-left">Rack</th>
            <th className="px-3 py-2 text-right">Teórica</th>
            <th className="px-3 py-2 text-right">Real</th>
            <th className="px-3 py-2 text-left">Variación</th>
            <th className="px-3 py-2 text-left">Estado</th>
            {puedeAccionar && (
              <th className="px-3 py-2 text-right">Acciones</th>
            )}
          </tr>
        </thead>
        <tbody>
          {items.map((l) => (
            <tr key={l.id} className="border-t">
              <td className="px-3 py-2">
                <EtiquetaArticulo
                  articuloId={l.articuloId}
                  clave={l.articuloClave}
                  descripcion={l.articuloDescripcion}
                />
              </td>
              <td className="px-3 py-2">
                <EtiquetaSubAlmacen
                  subAlmacenId={l.subAlmacenId}
                  clave={l.subAlmacenClave}
                />
              </td>
              <td className="px-3 py-2 font-mono text-xs">{l.ubicacionClave}</td>
              <td className="px-3 py-2 text-right font-mono">
                {l.cantidadTeorica.toLocaleString('es-MX', {
                  minimumFractionDigits: 2,
                  maximumFractionDigits: 4,
                })}
              </td>
              <td className="px-3 py-2 text-right font-mono">
                {l.cantidadRealCapturada != null
                  ? l.cantidadRealCapturada.toLocaleString('es-MX', {
                      minimumFractionDigits: 2,
                      maximumFractionDigits: 4,
                    })
                  : '—'}
              </td>
              <td className="px-3 py-2">
                <VariacionBadge
                  variacionAbsoluta={l.variacionAbsoluta}
                  variacionPorcentaje={l.variacionPorcentaje}
                  variacionValorMxn={l.variacionValorMxn}
                  requiereRecuento={l.requiereRecuento}
                />
              </td>
              <td className="px-3 py-2">
                {l.aprobadoIndividualmente ? (
                  <span
                    className={cn(
                      'inline-flex items-center gap-1 rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-800',
                    )}
                  >
                    <Check className="h-3 w-3" />
                    Aprobada
                  </span>
                ) : l.requiereRecuento ? (
                  <span className="inline-flex items-center rounded-full bg-rose-100 px-2 py-0.5 text-xs font-medium text-rose-800">
                    Pendiente
                  </span>
                ) : (
                  <span className="text-xs text-muted-foreground">OK</span>
                )}
              </td>
              {puedeAccionar && (
                <td className="px-3 py-2 text-right">
                  <div className="flex items-center justify-end gap-1">
                    <Button
                      type="button"
                      size="sm"
                      variant="ghost"
                      onClick={() => onRecuento(l.id)}
                      aria-label={`Recuento línea ${l.id}`}
                    >
                      <RotateCcw className="h-4 w-4" />
                    </Button>
                    {!l.aprobadoIndividualmente && (
                      <Button
                        type="button"
                        size="sm"
                        variant="ghost"
                        onClick={() => onAprobarLinea(l.id)}
                        aria-label={`Aprobar línea ${l.id}`}
                      >
                        <Check className="h-4 w-4" />
                      </Button>
                    )}
                  </div>
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ─── Dialog: Recuento ───────────────────────────────────────────────────────

function RecuentoDialog({
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
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Agregar recuento</DialogTitle>
          <DialogDescription>
            Captura la cantidad recontada por el contador en una segunda
            pasada. El backend re-evalúa la variación (A7).
          </DialogDescription>
        </DialogHeader>
        <form
          id="recuento-form"
          onSubmit={form.handleSubmit(onSubmit)}
          noValidate
          className="space-y-3"
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
                    field.onChange(
                      e.target.value === '' ? '' : Number(e.target.value),
                    )
                  }
                  autoFocus
                />
              )}
            />
          </Field>
        </form>
        <DialogFooter>
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpenChange(false)}
            disabled={agregar.isPending}
          >
            Cancelar
          </Button>
          <Button
            type="submit"
            form="recuento-form"
            disabled={agregar.isPending}
          >
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
          toast.success('Línea aprobada');
          onOpenChange(false);
          form.reset({ justificacion: '' });
        },
        onError: manejarError,
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Aprobar línea individualmente</DialogTitle>
          <DialogDescription>
            Captura la justificación. Aplica para líneas dentro de umbral
            A7 o ya conciliadas con recuento.
          </DialogDescription>
        </DialogHeader>
        <form
          id="aprobar-linea-form"
          onSubmit={form.handleSubmit(onSubmit)}
          noValidate
          className="space-y-3"
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
        <DialogFooter>
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpenChange(false)}
            disabled={aprobar.isPending}
          >
            Cancelar
          </Button>
          <Button
            type="submit"
            form="aprobar-linea-form"
            disabled={aprobar.isPending}
          >
            {aprobar.isPending ? 'Aprobando…' : 'Aprobar línea'}
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
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Rechazar conteo</DialogTitle>
          <DialogDescription>
            Captura el motivo. El conteo se cierra como Rechazado y no
            generará ajustes.
          </DialogDescription>
        </DialogHeader>
        <form
          id="rechazar-conteo-form"
          onSubmit={form.handleSubmit(onSubmit)}
          noValidate
          className="space-y-3"
        >
          <Field
            label="Motivo"
            required
            error={form.formState.errors.motivo?.message}
          >
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

function manejarError(error: unknown) {
  if (esApiError(error)) {
    toast.error(error.problem.title, {
      description: error.traceId ? `Código: ${error.traceId}` : undefined,
    });
    return;
  }
  toast.error('Error inesperado.');
}

function formatearMonto(v: number): string {
  return new Intl.NumberFormat('es-MX', {
    style: 'currency',
    currency: 'MXN',
    minimumFractionDigits: 2,
  }).format(v);
}

