import { SheetBody, SheetFooter } from '@/components/ui/sheet';
import { FormGrid, FormCampo } from '@/components/erp';
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
import { useCreditoDisponible } from '@/features/cxc/api/useLineasCredito';
import {
  useAutorizacionesCredito,
  useDecidirLiberacion,
  useReglasLiberacionSerie,
} from '@/features/cxc/api/useLiberaciones';
import {
  ComportamientoSerie,
  EstadoAutorizacionCredito,
  EstadoLineaCredito,
  MONEDAS_LINEA_CREDITO,
  ResultadoLiberacion,
} from '@/features/cxc/api/types';
import {
  ETIQUETA_RESULTADO_LIBERACION,
  formatoCobertura,
  formatoMonto,
} from '@/features/cxc/lib/glosario';
import {
  UMBRAL_COBERTURA_AUTORIZACION,
  autorizacionesConsumibles,
  calcularCobertura,
  comportamientoSeriePedido,
  mensajeErrorAutorizar,
  mostrarSelectorOverride,
  textoCoberturaPreview,
  textoSinLineaActiva,
} from '@/features/cxc/lib/autorizar-pendiente';
import {
  DecidirLiberacionSchema,
  type DecidirLiberacionValues,
} from '@/features/cxc/schemas/liberacion';
import { applyServerErrors, esApiError, useFormIdempotencyKey, codigoSoporte } from '@/lib/api';
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
      <SheetContent side="right" size="lg">
        <SheetHeader>
          <SheetTitle>Decidir liberación</SheetTitle>
          <SheetDescription>
            Evalúa la liberación de un pedido contra la cascada serie → crédito → override. La
            decisión queda registrada de forma inmutable (correcciones = nueva decisión).
          </SheetDescription>
        </SheetHeader>
        <div className="flex min-h-0 flex-1 flex-col">
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

  const [pedidoRef, clienteId, moneda, montoPedido, overrideId] = useWatch({
    control: form.control,
    name: ['pedidoRef', 'clienteId', 'moneda', 'montoPedido', 'overrideId'],
  });

  // Snapshot indicativo del crédito de la línea (cliente, moneda).
  const credito = useCreditoDisponible(clienteId || null);
  // C7.3 supuesto (e): solo la línea Activa cuenta; bloqueada = sin línea.
  const lineaMoneda = credito.data?.lineas.find(
    (l) => l.moneda === moneda && l.estado === EstadoLineaCredito.Activa,
  );
  const lineaBloqueada =
    lineaMoneda == null && (credito.data?.lineas.some((l) => l.moneda === moneda) ?? false);
  const disponible = lineaMoneda?.disponible ?? null;
  const alcanza = disponible != null && montoPedido > 0 && disponible >= montoPedido;
  // C7.3: cobertura indicativa (el backend decide con la serie y el dato vivo).
  const cobertura = lineaMoneda ? calcularCobertura(disponible, montoPedido) : null;
  const pendiente = !alcanza && cobertura != null && cobertura >= UMBRAL_COBERTURA_AUTORIZACION;

  // Overrides vigentes del usuario actual (solo el beneficiario consume).
  const autorizaciones = useAutorizacionesCredito(
    {
      estado: EstadoAutorizacionCredito.Autorizada,
      beneficiarioUsuarioId: usuarioId ?? undefined,
    },
    { enabled: usuarioId != null },
  );
  const vigentes = autorizacionesConsumibles(autorizaciones.data?.items ?? []);
  // C7.2: la serie que exige autorización conserva el override siempre.
  const reglasSerie = useReglasLiberacionSerie();
  const comportamiento = comportamientoSeriePedido(pedidoRef ?? '', reglasSerie.data ?? []);
  const serieExigeAutorizacion = comportamiento === ComportamientoSerie.NuncaLibera;
  const ofreceOverride = mostrarSelectorOverride(alcanza, cobertura, comportamiento);

  // Si el override deja de ofrecerse (crédito alcanza o cubre < 50 %), se limpia solo.
  useEffect(() => {
    if (!ofreceOverride && overrideId != null) {
      form.setValue('overrideId', null, { shouldDirty: false });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ofreceOverride]);

  const etiquetaBoton = serieExigeAutorizacion
    ? overrideId
      ? 'Liberar con override'
      : 'Registrar retención'
    : alcanza
      ? 'Liberar pedido'
      : overrideId
        ? 'Liberar con override'
        : pendiente
          ? 'Enviar a autorización'
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
          const cob = formatoCobertura(res.porcentajeCobertura);
          if (res.resultado === ResultadoLiberacion.Retenido) {
            toast.warning(`Pedido ${res.pedidoRef}: ${etiqueta}.`, {
              description: `Crédito disponible al decidir: ${formatoMonto(res.creditoDisponibleSnapshot, res.moneda)}${cob ? ` (${cob})` : ''}.`,
            });
          } else if (res.resultado === ResultadoLiberacion.PendienteAutorizacion) {
            toast.info(`Pedido ${res.pedidoRef}: ${etiqueta}.`, {
              description: `${cob ?? 'Cobertura parcial'}. Un gerente de CxC debe autorizarlo desde la bandeja.`,
            });
          } else {
            toast.success(`Pedido ${res.pedidoRef}: ${etiqueta}.`);
          }
          onClose();
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (
              applyServerErrors(form as unknown as Parameters<typeof applyServerErrors>[0], error)
            ) {
              return;
            }
            const { titulo } = mensajeErrorAutorizar(error);
            const conocido =
              error.problem.code === 'CXC_COBERTURA_INSUFICIENTE' ||
              error.problem.code === 'CXC_SIN_LINEA_ACTIVA' ||
              error.problem.code === 'AC_REFERENCIA_DISTINTA';
            toast.error(conocido ? titulo : error.problem.title, {
              description: error.problem.detail ?? codigoSoporte(error),
            });
            return;
          }
          toast.error('Error inesperado al registrar la decisión.');
        },
      },
    );
  }

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      className="flex min-h-0 flex-1 flex-col"
    >
      <SheetBody className="space-y-5">
        <FormGrid>
          <FormCampo span={6} className="space-y-1">
            <Label htmlFor="pedidoRef" className="text-xs">
              Folio del pedido (A+W) <span className="text-danger-fg">*</span>
            </Label>
            <Input
              id="pedidoRef"
              placeholder="p. ej. 3000123456"
              maxLength={40}
              {...form.register('pedidoRef')}
            />
            {form.formState.errors.pedidoRef && (
              <p className="text-xs text-danger-fg">{form.formState.errors.pedidoRef.message}</p>
            )}
          </FormCampo>
          <FormCampo span={6} className="space-y-1">
            <Label htmlFor="montoPedido" className="text-xs">
              Monto del pedido <span className="text-danger-fg">*</span>
            </Label>
            <Input
              id="montoPedido"
              type="number"
              step="0.01"
              min="0"
              {...form.register('montoPedido', { valueAsNumber: true })}
            />
            {form.formState.errors.montoPedido && (
              <p className="text-xs text-danger-fg">{form.formState.errors.montoPedido.message}</p>
            )}
          </FormCampo>
          <FormCampo span={6} className="space-y-1">
            <Label className="text-xs">
              Cliente <span className="text-danger-fg">*</span>
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
              <p className="text-xs text-danger-fg">{form.formState.errors.clienteId.message}</p>
            )}
          </FormCampo>
          <FormCampo span={6} className="space-y-1">
            <Label htmlFor="moneda" className="text-xs">
              Moneda <span className="text-danger-fg">*</span>
            </Label>
            <select
              id="moneda"
              className="h-9 w-full rounded-md border border-line-control bg-transparent px-3 text-sm shadow-card-flat"
              {...form.register('moneda')}
            >
              {MONEDAS_LINEA_CREDITO.map((m) => (
                <option key={m} value={m}>
                  {m}
                </option>
              ))}
            </select>
          </FormCampo>
        </FormGrid>

        {/* ── Snapshot indicativo de crédito ─────────────────────────── */}
        {clienteId && (
          <section
            className={cn(
              'rounded-md border px-4 py-3 text-sm',
              disponible == null
                ? 'bg-surface-muted/40'
                : alcanza
                  ? 'border-line bg-success-bg  '
                  : 'border-line bg-warning-bg  ',
            )}
            aria-live="polite"
          >
            {credito.isLoading ? (
              <p className="text-ink-muted">Evaluando crédito…</p>
            ) : lineaMoneda == null ? (
              <p>
                {textoSinLineaActiva(
                  clienteLabel ?? 'El cliente',
                  moneda,
                  lineaBloqueada,
                  serieExigeAutorizacion,
                )}
              </p>
            ) : (
              <div className="space-y-1">
                <p>
                  Crédito disponible:{' '}
                  <span className="font-mono font-semibold tabular-nums">
                    {formatoMonto(lineaMoneda.disponible, lineaMoneda.moneda)}
                  </span>
                  {credito.data?.datoIncompleto && (
                    <span className="ml-2 text-xs text-warning-fg ">
                      (sin material liberado A+W — indicativo)
                    </span>
                  )}
                </p>
                {montoPedido > 0 && cobertura != null && (
                  <p className="font-medium">{formatoCobertura(cobertura)}</p>
                )}
                {montoPedido > 0 && (
                  <p className="text-xs text-ink-muted">
                    {textoCoberturaPreview(alcanza, pendiente, serieExigeAutorizacion)} Las reglas
                    por serie (p. ej. 5000/7000 siempre liberan) las evalúa el sistema al registrar.
                  </p>
                )}
              </div>
            )}
          </section>
        )}

        {/* ── Override consumible (serie que exige autorización, o crédito 50–99 %) ── */}
        {clienteId && montoPedido > 0 && ofreceOverride && (
          <section className="space-y-1">
            <Label htmlFor="overrideId" className="text-xs">
              Autorización vigente (override)
            </Label>
            {serieExigeAutorizacion && (
              <p className="text-xs text-ink-muted">
                La serie de este pedido solo se libera con autorización; sin override quedará
                retenido.
              </p>
            )}
            {vigentes.length === 0 ? (
              <p className="rounded-md border border-dashed px-3 py-2 text-xs text-ink-muted">
                No tienes autorizaciones vigentes a tu nombre. Pide a tu gerente que cree una desde
                la pestaña Autorizaciones.
              </p>
            ) : (
              <select
                id="overrideId"
                className="h-9 w-full rounded-md border border-line-control bg-transparent px-3 text-sm shadow-card-flat"
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
      </SheetBody>
      <SheetFooter className="flex-row justify-end border-t border-line-divider">
        <Button type="button" variant="ghost" onClick={onClose} disabled={decidir.isPending}>
          Cancelar
        </Button>
        <Button
          type="submit"
          variant={etiquetaBoton === 'Registrar retención' ? 'secondary' : 'default'}
          disabled={decidir.isPending}
        >
          {decidir.isPending ? 'Registrando…' : etiquetaBoton}
        </Button>
      </SheetFooter>
    </form>
  );
}
