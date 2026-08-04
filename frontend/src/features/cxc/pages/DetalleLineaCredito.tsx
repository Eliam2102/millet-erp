import { useEffect, useState } from 'react';
import { Link, useParams, useSearch } from '@tanstack/react-router';
import { Ban, Lock, Pencil, Unlock, X } from 'lucide-react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { ErrorState } from '@/components/erp';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { useQueryClient } from '@tanstack/react-query';
import {
  useActualizarLineaCredito,
  useBloquearLineaCredito,
  useClientesLookupCxc,
  useDesbloquearLineaCredito,
  useLineaCredito,
} from '@/features/cxc/api/useLineasCredito';
import { cxcKeys } from '@/features/cxc/api/keys';
import { BloquearLineaDialog } from '@/features/cxc/components/BloquearLineaDialog';
import { ChipEstadoLinea } from '@/features/cxc/components/ChipEstadoLinea';
import { CreditoDisponibleCard } from '@/features/cxc/components/CreditoDisponibleCard';
import {
  EstadoLineaCredito,
  CLASIFICACIONES_CREDITO,
  type LineaCreditoResponse,
} from '@/features/cxc/api/types';
import {
  ETIQUETA_ORIGEN_LINEA,
  formatoMonto,
} from '@/features/cxc/lib/glosario';
import {
  EditarLineaCreditoSchema,
  SIN_CLASIFICACION,
  type EditarLineaCreditoValues,
} from '@/features/cxc/schemas/linea-credito';
import type { LineasSearch } from '@/features/cxc/lib/lineas-search-schema';
import { applyServerErrors, esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * <c>Detalle de línea de crédito</c> (CXC-FE-PR2, P3). Sub-topbar sticky
 * §6.5 (cliente + estado + acciones + cerrar), banner de bloqueo, datos
 * de la línea con edición INLINE (border ámbar §6.3 — nunca modal para
 * editar) y <c>&lt;CreditoDisponibleCard/&gt;</c> del cliente.
 */
export function DetalleLineaCredito() {
  const { id } = useParams({ from: '/_app/cxc/lineas-credito/$id' });
  const query = useLineaCredito(id);

  return (
    <div className="space-y-4">
      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la línea de crédito"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading || query.data == null ? (
        <div className="space-y-3">
          <div className="h-8 w-64 animate-pulse rounded bg-muted" />
          <div className="h-40 w-full animate-pulse rounded bg-muted" />
        </div>
      ) : (
        <Contenido linea={query.data} />
      )}
    </div>
  );
}

function Contenido({ linea }: { linea: LineaCreditoResponse }) {
  const search = useSearch({ strict: false }) as LineasSearch;
  const queryClient = useQueryClient();
  const conflictDialog = useConflictDialog();
  const puedeGestionar = useHasPermission(
    PermisosCanonicos.CuentasPorCobrarLineasCreditoGestionar,
  );

  const lookup = useClientesLookupCxc({ ids: [linea.clienteId] });
  const cliente = lookup.data?.[0] ?? null;
  const etiquetaLinea = `${cliente?.razonSocial ?? 'Cliente'} · ${linea.moneda}`;

  const [editando, setEditando] = useState(false);
  const [dialogBloquear, setDialogBloquear] = useState(false);

  const bloquear = useBloquearLineaCredito();
  const desbloquear = useDesbloquearLineaCredito();

  function onConflicto(traceId?: string) {
    conflictDialog.openSimple({
      onRefrescar: () =>
        queryClient.invalidateQueries({ queryKey: cxcKeys.lineasCredito() }),
      traceId,
    });
  }

  function manejarError(error: unknown, fallback: string) {
    if (esApiError(error)) {
      if (error.status === 409) {
        onConflicto(error.traceId);
        return;
      }
      toast.error(error.problem.title, {
        description:
          error.problem.detail ??
          (error.traceId ? `Código: ${error.traceId}` : undefined),
      });
      return;
    }
    toast.error(fallback);
  }

  return (
    <div className="space-y-6">
      {/* ── Sub-topbar §6.5 (sticky, no se imprime) ─────────────── */}
      <div
        className="sticky top-0 z-10 flex flex-wrap items-center justify-between gap-2 border-b bg-background/95 pb-2 backdrop-blur"
        data-print="hidden"
      >
        <div className="flex min-w-0 flex-wrap items-center gap-2">
          <h1 className="truncate text-lg font-semibold">
            {cliente?.razonSocial ?? '…'}
          </h1>
          <span className="font-mono text-sm text-muted-foreground">
            {linea.moneda}
          </span>
          <ChipEstadoLinea estado={linea.estado} />
        </div>
        <div className="flex items-center gap-1.5">
          {puedeGestionar && linea.estado !== EstadoLineaCredito.Suspendida && (
            <Button
              variant="outline"
              size="sm"
              onClick={() => setEditando((v) => !v)}
              aria-pressed={editando}
            >
              <Pencil className="mr-1.5 h-3.5 w-3.5" />
              Editar
            </Button>
          )}
          {puedeGestionar && linea.estado === EstadoLineaCredito.Activa && (
            <Button
              variant="outline"
              size="sm"
              className="text-destructive"
              onClick={() => setDialogBloquear(true)}
            >
              <Lock className="mr-1.5 h-3.5 w-3.5" />
              Bloquear
            </Button>
          )}
          {puedeGestionar && linea.estado === EstadoLineaCredito.Bloqueada && (
            <Button
              variant="outline"
              size="sm"
              disabled={desbloquear.isPending}
              onClick={() =>
                desbloquear.mutate(
                  {
                    id: linea.id,
                    versionEsperada: linea.version,
                    // Key fresca por submit: el backend exige UUID v4 puro.
                    idempotencyKey: crypto.randomUUID(),
                  },
                  {
                    onSuccess: () => toast.success('Línea desbloqueada.'),
                    onError: (e) =>
                      manejarError(e, 'No se pudo desbloquear la línea.'),
                  },
                )
              }
            >
              <Unlock className="mr-1.5 h-3.5 w-3.5" />
              {desbloquear.isPending ? 'Desbloqueando…' : 'Desbloquear'}
            </Button>
          )}
          <Button variant="ghost" size="icon" asChild aria-label="Cerrar detalle">
            <Link to="/cxc/lineas-credito" search={search}>
              <X className="h-4 w-4" />
            </Link>
          </Button>
        </div>
      </div>

      {linea.estado === EstadoLineaCredito.Bloqueada && linea.motivoBloqueo && (
        <div className="flex items-start gap-2 rounded-md border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950/40 dark:text-red-200">
          <Ban className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
          <div>
            <p className="font-medium">Línea bloqueada</p>
            <p>{linea.motivoBloqueo}</p>
          </div>
        </div>
      )}

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        {/* ── Datos de la línea (+ edición inline §6.3) ─────────── */}
        <section className="rounded-md border bg-card p-4" aria-label="Datos de la línea">
          <h3 className="text-sm font-medium">Datos de la línea</h3>
          {!editando ? (
            <dl className="mt-3 grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
              <Dato etiqueta="Límite">
                <span className="font-mono tabular-nums">
                  {formatoMonto(linea.limite, linea.moneda)}
                </span>
              </Dato>
              <Dato etiqueta="Plazo">{linea.plazoDias} días</Dato>
              <Dato etiqueta="Origen">{ETIQUETA_ORIGEN_LINEA[linea.origen]}</Dato>
              <Dato etiqueta="Clasificación">
                {linea.clasificacion ?? 'Sin clasificar'}
              </Dato>
              <Dato etiqueta="RFC">
                <span className="font-mono">{cliente?.rfc ?? '—'}</span>
              </Dato>
              <Dato etiqueta="Clave del cliente">
                <span className="font-mono">{cliente?.clave ?? '—'}</span>
              </Dato>
            </dl>
          ) : (
            <EditarLineaInlineForm
              linea={linea}
              onClose={() => setEditando(false)}
              onConflicto={onConflicto}
            />
          )}
          <p className="mt-4 text-xs text-muted-foreground">
            Cliente, moneda y origen son inmutables: para cambiarlos se
            bloquea esta línea y se crea otra.
          </p>
        </section>

        {/* ── Crédito disponible del cliente ────────────────────── */}
        <CreditoDisponibleCard clienteId={linea.clienteId} />
      </div>

      <BloquearLineaDialog
        open={dialogBloquear}
        onOpenChange={setDialogBloquear}
        etiquetaLinea={etiquetaLinea}
        isPending={bloquear.isPending}
        onConfirm={(motivo) =>
          bloquear.mutate(
            {
              id: linea.id,
              versionEsperada: linea.version,
              motivo,
              idempotencyKey: crypto.randomUUID(),
            },
            {
              onSuccess: () => {
                setDialogBloquear(false);
                toast.success('Línea bloqueada.');
              },
              onError: (e) => manejarError(e, 'No se pudo bloquear la línea.'),
            },
          )
        }
      />
    </div>
  );
}

function Dato({
  etiqueta,
  children,
}: {
  etiqueta: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{etiqueta}</dt>
      <dd>{children}</dd>
    </div>
  );
}

/**
 * Form de edición INLINE de límite/plazo/clasificación (§6.3: reemplaza
 * el bloque de solo lectura, border ámbar "registro existente en
 * modificación"). PUT con <c>X-Expected-Version</c>; 409 → conflict
 * dialog; 422 → <c>applyServerErrors</c>.
 */
function EditarLineaInlineForm({
  linea,
  onClose,
  onConflicto,
}: {
  linea: LineaCreditoResponse;
  onClose: () => void;
  onConflicto: (traceId?: string) => void;
}) {
  const actualizar = useActualizarLineaCredito();

  const form = useForm<EditarLineaCreditoValues>({
    resolver: zodResolver(EditarLineaCreditoSchema),
    defaultValues: {
      limite: linea.limite,
      plazoDias: linea.plazoDias,
      clasificacion:
        (linea.clasificacion as EditarLineaCreditoValues['clasificacion']) ??
        null,
    },
  });

  // Si otra sesión actualizó la línea (refetch), rebase de los defaults.
  useEffect(() => {
    form.reset({
      limite: linea.limite,
      plazoDias: linea.plazoDias,
      clasificacion:
        (linea.clasificacion as EditarLineaCreditoValues['clasificacion']) ??
        null,
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [linea.version]);

  function onSubmit(values: EditarLineaCreditoValues) {
    actualizar.mutate(
      {
        id: linea.id,
        versionEsperada: linea.version,
        body: {
          limite: values.limite,
          plazoDias: values.plazoDias,
          clasificacion: values.clasificacion,
        },
        // Key fresca por submit: el backend exige UUID v4 puro.
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success('Línea actualizada.');
          onClose();
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.status === 409) {
              onConflicto(error.traceId);
              return;
            }
            if (
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                error,
              )
            ) {
              return;
            }
            toast.error(error.problem.title, {
              description: error.problem.detail ?? undefined,
            });
            return;
          }
          toast.error('Error inesperado al actualizar la línea.');
        },
      },
    );
  }

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      className="mt-3 space-y-3 rounded-md border border-amber-400 bg-amber-50/40 p-3 dark:border-amber-700 dark:bg-amber-950/20"
    >
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <div className="space-y-1">
          <Label htmlFor="limite" className="text-xs">
            Límite ({linea.moneda}) <span className="text-destructive">*</span>
          </Label>
          <Input
            id="limite"
            type="number"
            step="0.01"
            min="0"
            {...form.register('limite', { valueAsNumber: true })}
          />
          {form.formState.errors.limite && (
            <p className="text-xs text-destructive">
              {form.formState.errors.limite.message}
            </p>
          )}
        </div>
        <div className="space-y-1">
          <Label htmlFor="plazoDias" className="text-xs">
            Plazo (días) <span className="text-destructive">*</span>
          </Label>
          <Input
            id="plazoDias"
            type="number"
            step="1"
            min="1"
            max="365"
            {...form.register('plazoDias', { valueAsNumber: true })}
          />
          {form.formState.errors.plazoDias && (
            <p className="text-xs text-destructive">
              {form.formState.errors.plazoDias.message}
            </p>
          )}
        </div>
        <div className="space-y-1">
          <Label htmlFor="clasificacion" className="text-xs">
            Clasificación
          </Label>
          <select
            id="clasificacion"
            className="h-9 w-full rounded-md border border-input bg-transparent px-3 text-sm shadow-sm"
            {...form.register('clasificacion', {
              setValueAs: (v) => (v === SIN_CLASIFICACION || v === '' ? null : v),
            })}
            defaultValue={linea.clasificacion ?? SIN_CLASIFICACION}
          >
            <option value={SIN_CLASIFICACION}>Sin clasificar</option>
            {CLASIFICACIONES_CREDITO.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </select>
        </div>
      </div>
      <div className="flex items-center justify-end gap-2">
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onClose}
          disabled={actualizar.isPending}
        >
          Cancelar
        </Button>
        <Button type="submit" size="sm" disabled={actualizar.isPending}>
          {actualizar.isPending ? 'Guardando…' : 'Guardar cambios'}
        </Button>
      </div>
    </form>
  );
}
