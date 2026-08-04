import { useEffect, useState } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { ClienteSelectorCxc } from '@/features/cxc/components/ClienteSelectorCxc';
import {
  useCreditoDisponible,
} from '@/features/cxc/api/useLineasCredito';
import {
  useAutorizacionesCredito,
  useDecidirLiberacion,
} from '@/features/cxc/api/useLiberaciones';
import {
  EstadoAutorizacionCredito,
  MONEDAS_LINEA_CREDITO,
  ResultadoLiberacion,
} from '@/features/cxc/api/types';
import {
  ETIQUETA_RESULTADO_LIBERACION,
  formatoMonto,
} from '@/features/cxc/lib/glosario';
import {
  DecidirLiberacionSchema,
  type DecidirLiberacionValues,
} from '@/features/cxc/schemas/liberacion';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;DecidirLiberacionSheet/&gt;</c> — flujo de acción "Decidir
 * liberación" (05-frontend-diseno §3): NO es un form largo. Captura
 * pedido/cliente/moneda/monto, muestra el crédito disponible (snapshot
 * indicativo) y el botón cambia según el caso: Liberar / Registrar
 * retención / Liberar con override (selector de autorización VIGENTE
 * del usuario actual, §4.3 — nunca captura de contraseña ajena).
 *
 * <para>La cascada serie → crédito → override la evalúa el BACKEND al
 * registrar; el preview del FE es indicativo (las reglas por serie
 * pueden cambiar el resultado).</para>
 */
export function DecidirLiberacionSheet({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="w-full overflow-y-auto sm:max-w-2xl">
        <SheetHeader>
          <SheetTitle>Decidir liberación</SheetTitle>
          <SheetDescription>
            Evalúa la liberación de un pedido contra la cascada serie →
            crédito → override. La decisión queda registrada de forma
            inmutable (correcciones = nueva decisión).
          </SheetDescription>
        </SheetHeader>
        <div className="px-2 pb-6">
          {open && <FormDecidir onClose={() => onOpenChange(false)} />}
        </div>
      </SheetContent>
    </Sheet>
  );
}

function FormDecidir({ onClose }: { onClose: () => void }) {
  const idempotencyKey = useFormIdempotencyKey();
  const decidir = useDecidirLiberacion();
  const usuarioId = useAuthStore((s) => s.user?.id ?? null);
  const [clienteLabel, setClienteLabel] = useState<string | undefined>();

  const form = useForm<DecidirLiberacionValues>({
    resolver: zodResolver(DecidirLiberacionSchema),
    defaultValues: {
      pedidoRef: '',
      clienteId: '',
      moneda: 'MXN',
      montoPedido: 0,
      overrideId: null,
    },
  });

  const [clienteId, moneda, montoPedido, overrideId] = useWatch({
    control: form.control,
    name: ['clienteId', 'moneda', 'montoPedido', 'overrideId'],
  });

  // Snapshot indicativo del crédito de la línea (cliente, moneda).
  const credito = useCreditoDisponible(clienteId || null);
  const lineaMoneda = credito.data?.lineas.find((l) => l.moneda === moneda);
  const disponible = lineaMoneda?.disponible ?? null;
  const alcanza =
    disponible != null && montoPedido > 0 && disponible >= montoPedido;

  // Overrides vigentes del usuario actual (solo el beneficiario consume).
  const autorizaciones = useAutorizacionesCredito(
    {
      estado: EstadoAutorizacionCredito.Autorizada,
      beneficiarioUsuarioId: usuarioId ?? undefined,
    },
    { enabled: usuarioId != null },
  );
  const vigentes = autorizaciones.data?.items ?? [];

  // Si el crédito alcanza, el override no aplica — se limpia solo.
  useEffect(() => {
    if (alcanza && overrideId != null) {
      form.setValue('overrideId', null, { shouldDirty: false });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [alcanza]);

  const etiquetaBoton = alcanza
    ? 'Liberar pedido'
    : overrideId
      ? 'Liberar con override'
      : 'Registrar retención';

  function onSubmit(values: DecidirLiberacionValues) {
    decidir.mutate(
      {
        command: {
          pedidoRef: values.pedidoRef.trim(),
          clienteId: values.clienteId,
          moneda: values.moneda,
          montoPedido: values.montoPedido,
          overrideId: values.overrideId,
        },
        idempotencyKey,
      },
      {
        onSuccess: (res) => {
          const etiqueta = ETIQUETA_RESULTADO_LIBERACION[res.resultado];
          if (res.resultado === ResultadoLiberacion.Retenido) {
            toast.warning(`Pedido ${res.pedidoRef}: ${etiqueta}.`, {
              description: `Crédito disponible al decidir: ${formatoMonto(res.creditoDisponibleSnapshot, res.moneda)}.`,
            });
          } else {
            toast.success(`Pedido ${res.pedidoRef}: ${etiqueta}.`);
          }
          onClose();
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                error,
              )
            ) {
              return;
            }
            toast.error(error.problem.title, {
              description:
                error.problem.detail ??
                (error.traceId ? `Código: ${error.traceId}` : undefined),
            });
            return;
          }
          toast.error('Error inesperado al registrar la decisión.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} noValidate className="space-y-5">
      <section className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <div className="space-y-1">
          <Label htmlFor="pedidoRef" className="text-xs">
            Folio del pedido (A+W) <span className="text-destructive">*</span>
          </Label>
          <Input
            id="pedidoRef"
            placeholder="p. ej. 3000123456"
            maxLength={40}
            {...form.register('pedidoRef')}
          />
          {form.formState.errors.pedidoRef && (
            <p className="text-xs text-destructive">
              {form.formState.errors.pedidoRef.message}
            </p>
          )}
        </div>
        <div className="space-y-1">
          <Label htmlFor="montoPedido" className="text-xs">
            Monto del pedido <span className="text-destructive">*</span>
          </Label>
          <Input
            id="montoPedido"
            type="number"
            step="0.01"
            min="0"
            {...form.register('montoPedido', { valueAsNumber: true })}
          />
          {form.formState.errors.montoPedido && (
            <p className="text-xs text-destructive">
              {form.formState.errors.montoPedido.message}
            </p>
          )}
        </div>
        <div className="space-y-1">
          <Label className="text-xs">
            Cliente <span className="text-destructive">*</span>
          </Label>
          <Controller
            name="clienteId"
            control={form.control}
            render={({ field }) => (
              <ClienteSelectorCxc
                value={field.value || null}
                onChange={(item) => {
                  field.onChange(item?.id ?? '');
                  setClienteLabel(item?.razonSocial);
                }}
              />
            )}
          />
          {form.formState.errors.clienteId && (
            <p className="text-xs text-destructive">
              {form.formState.errors.clienteId.message}
            </p>
          )}
        </div>
        <div className="space-y-1">
          <Label htmlFor="moneda" className="text-xs">
            Moneda <span className="text-destructive">*</span>
          </Label>
          <select
            id="moneda"
            className="h-9 w-full rounded-md border border-input bg-transparent px-3 text-sm shadow-sm"
            {...form.register('moneda')}
          >
            {MONEDAS_LINEA_CREDITO.map((m) => (
              <option key={m} value={m}>
                {m}
              </option>
            ))}
          </select>
        </div>
      </section>

      {/* ── Snapshot indicativo de crédito ─────────────────────────── */}
      {clienteId && (
        <section
          className={cn(
            'rounded-md border px-4 py-3 text-sm',
            disponible == null
              ? 'bg-muted/40'
              : alcanza
                ? 'border-emerald-300 bg-emerald-50/60 dark:border-emerald-900 dark:bg-emerald-950/30'
                : 'border-amber-300 bg-amber-50/60 dark:border-amber-800 dark:bg-amber-950/30',
          )}
          aria-live="polite"
        >
          {credito.isLoading ? (
            <p className="text-muted-foreground">Evaluando crédito…</p>
          ) : lineaMoneda == null ? (
            <p>
              {clienteLabel ?? 'El cliente'} no tiene línea de crédito en{' '}
              {moneda} — sin override, la decisión quedará retenida.
            </p>
          ) : (
            <div className="space-y-1">
              <p>
                Crédito disponible:{' '}
                <span className="font-mono font-semibold tabular-nums">
                  {formatoMonto(lineaMoneda.disponible, lineaMoneda.moneda)}
                </span>
                {credito.data?.datoIncompleto && (
                  <span className="ml-2 text-xs text-amber-700 dark:text-amber-300">
                    (sin material liberado A+W — indicativo)
                  </span>
                )}
              </p>
              {montoPedido > 0 && (
                <p className="text-xs text-muted-foreground">
                  {alcanza
                    ? 'El crédito alcanza para el monto del pedido.'
                    : 'El crédito NO alcanza: se requiere override vigente o quedará retenido.'}{' '}
                  Las reglas por serie (p. ej. 5000/7000 siempre liberan) las
                  evalúa el sistema al registrar.
                </p>
              )}
            </div>
          )}
        </section>
      )}

      {/* ── Override consumible (solo si el crédito no alcanza) ────── */}
      {clienteId && montoPedido > 0 && !alcanza && (
        <section className="space-y-1">
          <Label htmlFor="overrideId" className="text-xs">
            Autorización vigente (override)
          </Label>
          {vigentes.length === 0 ? (
            <p className="rounded-md border border-dashed px-3 py-2 text-xs text-muted-foreground">
              No tienes autorizaciones vigentes a tu nombre. Pide a tu
              gerente que cree una desde la pestaña Autorizaciones.
            </p>
          ) : (
            <select
              id="overrideId"
              className="h-9 w-full rounded-md border border-input bg-transparent px-3 text-sm shadow-sm"
              {...form.register('overrideId', {
                setValueAs: (v) => (v === '' ? null : v),
              })}
            >
              <option value="">Sin override (retener)</option>
              {vigentes.map((a) => (
                <option key={a.id} value={a.id}>
                  {a.clienteOPedidoRef} · {a.motivo} · vence{' '}
                  {new Date(a.vigenteHasta).toLocaleString('es-MX')}
                </option>
              ))}
            </select>
          )}
        </section>
      )}

      <div className="flex items-center justify-end gap-2 border-t pt-4">
        <Button
          type="button"
          variant="ghost"
          onClick={onClose}
          disabled={decidir.isPending}
        >
          Cancelar
        </Button>
        <Button
          type="submit"
          variant={etiquetaBoton === 'Registrar retención' ? 'secondary' : 'default'}
          disabled={decidir.isPending}
        >
          {decidir.isPending ? 'Registrando…' : etiquetaBoton}
        </Button>
      </div>
    </form>
  );
}
