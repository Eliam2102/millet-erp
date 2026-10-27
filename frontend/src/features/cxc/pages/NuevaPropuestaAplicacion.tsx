import { ResumenImportes, FormSeccion, FormCampo } from '@/components/erp';

import { SheetBody, SheetFooter } from '@/components/ui/sheet';

import { diferenciaDeposito } from '../lib/diferencia-deposito';
import { useEffect, useMemo, useState } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { ClienteSelectorCxc } from '@/features/cxc/components/ClienteSelectorCxc';
import { MatchingAplicacionTable } from '@/features/cxc/components/MatchingAplicacionTable';
import {
  useCrearPropuestaAplicacion,
  useFacturasAbiertas,
  useToleranciasNoFiscal,
} from '@/features/cxc/api/useAplicaciones';
import {
  MONEDAS_LINEA_CREDITO,
  type DepositoPorIdentificarItem,
} from '@/features/cxc/api/types';
import { formatoMonto } from '@/features/cxc/lib/glosario';
import { sugerirPorAntiguedad } from '@/features/cxc/lib/sugerir-por-antiguedad';
import {
  PropuestaAplicacionSchema,
  type PropuestaAplicacionValues,
} from '@/features/cxc/schemas/propuesta-aplicacion';
import { applyServerErrors, esApiError, useFormIdempotencyKey, codigoSoporte } from '@/lib/api';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;NuevaPropuestaAplicacion/&gt;</c> — matching depósito ↔
 * facturas (05-frontend-diseno §4.1): cabecera del depósito (monto,
 * moneda, referencia, remittance) + facturas abiertas del cliente con
 * checkbox e importe editable inline. Footer con Σ aplicado, diferencia
 * y — si el depósito viene corto dentro de tolerancia — el ajuste no
 * fiscal (comisión bancaria) que NO genera CFDI. Sin remittance el
 * botón queda deshabilitado.
 */
export interface NuevaPropuestaAplicacionProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange?: (dirty: boolean) => void;
  /**
   * C7.4 · CA7.9: depósito sin identificar que se identifica con esta
   * propuesta (prellena monto, moneda, referencia y cliente si Tesorería lo
   * conoce). El remittance sigue siendo obligatorio (R13).
   */
  deposito?: DepositoPorIdentificarItem | null;
}

export function NuevaPropuestaAplicacion({
  onClose,
  onDirtyChange,
  deposito = null,
}: NuevaPropuestaAplicacionProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearPropuestaAplicacion();
  const tolerancias = useToleranciasNoFiscal();

  const form = useForm<PropuestaAplicacionValues>({
    resolver: zodResolver(PropuestaAplicacionSchema),
    defaultValues: {
      clienteId: deposito?.clienteId ?? '',
      depositoRef: deposito?.referencia ?? '',
      montoDeposito: deposito?.disponible ?? 0,
      moneda: (MONEDAS_LINEA_CREDITO as readonly string[]).includes(deposito?.moneda ?? '')
        ? (deposito!.moneda as PropuestaAplicacionValues['moneda'])
        : 'MXN',
      remittanceRef: '',
    },
  });

  // Selección del matching: uuid → importe (estado local, no del form).
  const [seleccion, setSeleccion] = useState<Map<string, number>>(new Map());

  const [clienteId, moneda, montoDeposito, remittanceRef] = useWatch({
    control: form.control,
    name: ['clienteId', 'moneda', 'montoDeposito', 'remittanceRef'],
  });

  const isDirty = form.formState.isDirty || seleccion.size > 0;
  useEffect(() => {
    onDirtyChange?.(isDirty);
  }, [isDirty, onDirtyChange]);

  const facturas = useFacturasAbiertas(clienteId || null, moneda ?? null);

  // Cambio de cliente o moneda invalida la selección previa. Patrón
  // derived-state (mismo que Topbar) — evita setState dentro de un
  // useEffect (regla react-hooks/set-state-in-effect).
  const [claveSeleccion, setClaveSeleccion] = useState(`${clienteId}|${moneda}`);
  if (claveSeleccion !== `${clienteId}|${moneda}`) {
    setClaveSeleccion(`${clienteId}|${moneda}`);
    setSeleccion(new Map());
  }

  function cambiarSeleccion(uuid: string, importe: number | null) {
    setSeleccion((prev) => {
      const next = new Map(prev);
      if (importe == null) next.delete(uuid);
      else next.set(uuid, importe);
      return next;
    });
  }

  const sumaAplicada = useMemo(
    () => Array.from(seleccion.values()).reduce((a, b) => a + b, 0),
    [seleccion],
  );
  const tolerancia = tolerancias.data?.[moneda] ?? 0;
  const { diferencia, faltaTolerancia, esAjusteNoFiscal, excedeTolerancia } = diferenciaDeposito(
    montoDeposito || 0,
    sumaAplicada,
    tolerancias.isSuccess ? tolerancia : null,
  );
  const depositoExcedente = diferencia > 0 && seleccion.size > 0;
  const importesInvalidos = Array.from(seleccion.entries()).some(([uuid, imp]) => {
    const f = facturas.data?.find((x) => x.uuid === uuid);
    // C7.4-a: una factura que no es PPD no admite parcialidades (el backend lo rechaza).
    const tope = f?.disponible ?? f?.saldo;
    return imp <= 0 || (f != null && tope != null && (imp > tope || (f.metodoPago !== 'PPD' && imp < tope)));
  });
  const excedeDeposito = deposito != null && (montoDeposito || 0) > deposito.disponible;

  // V61: «Sugerir» llena importes por antigüedad SIN enviar; una persona revisa y propone.
  function sugerir() {
    setSeleccion(sugerirPorAntiguedad(facturas.data ?? [], montoDeposito || 0));
  }

  const sinRemittance = (remittanceRef ?? '').trim() === '';
  const puedeProponer =
    !sinRemittance &&
    seleccion.size > 0 &&
    !importesInvalidos &&
    !excedeTolerancia &&
    !faltaTolerancia &&
    !excedeDeposito &&
    (montoDeposito || 0) > 0;

  function onSubmit(values: PropuestaAplicacionValues) {
    if (!puedeProponer) return;
    crear.mutate(
      {
        command: {
          clienteId: values.clienteId,
          depositoRef: values.depositoRef.trim(),
          montoDeposito: values.montoDeposito,
          moneda: values.moneda,
          remittanceRef: values.remittanceRef.trim(),
          facturas: Array.from(seleccion.entries()).map(([facturaUuid, importeAplicado]) => ({
            facturaUuid,
            importeAplicado,
          })),
          movimientoBancarioId: deposito?.movimientoBancarioId ?? null,
        },
        idempotencyKey,
      },
      {
        onSuccess: (res) => {
          const parcialidades = res.facturas
            .filter((f) => f.numParcialidad != null)
            .map(
              (f) =>
                `${f.folio ?? f.facturaUuid} parc. ${f.numParcialidad}${
                  f.saldoInsoluto != null ? ` · saldo insoluto ${formatoMonto(f.saldoInsoluto, res.moneda)}` : ''
                }`,
            );
          toast.success(
            `Propuesta creada para el depósito ${res.depositoRef} (${res.facturas.length} factura${res.facturas.length === 1 ? '' : 's'}).`,
            parcialidades.length > 0 ? { description: parcialidades.join(' · ') } : undefined,
          );
          onClose({ force: true });
        },
        onError: (error) => {
          if (esApiError(error)) {
            const mapeado = applyServerErrors(
              form as unknown as Parameters<typeof applyServerErrors>[0],
              error,
            );
            // Los rechazos por LÍNEA (`facturas[...]`) se mapean a campos SIN
            // input visible (viven en el Map `seleccion`): applyServerErrors
            // devuelve true pero el usuario no vería nada → hay que surfacear
            // un toast igual. Solo se suprime si TODO el error cayó en campos
            // visibles del form.
            const errsLinea = (error.problem.errores ?? []).filter((e) =>
              e.campo.startsWith('facturas'),
            );
            if (mapeado && errsLinea.length === 0) return;
            toast.error(error.problem.title, {
              description:
                error.problem.detail ??
                (errsLinea.length > 0
                  ? errsLinea.map((e) => e.mensaje).join(' · ')
                  : codigoSoporte(error)),
            });
            return;
          }
          toast.error('Error inesperado al crear la propuesta.');
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
        {/* ── Depósito ─────────────────────────────────────────────── */}
        {deposito && (
          <p
            className="rounded-md border border-line bg-info-bg px-3 py-2 text-sm text-info-fg"
            data-testid="deposito-identificado"
          >
            Identificando el depósito {deposito.referencia ?? 'sin referencia'} · {deposito.cuenta} ·{' '}
            {deposito.fechaValor.slice(0, 10)} · disponible{' '}
            {formatoMonto(deposito.disponible, deposito.moneda)}. No se aplica hasta que Tesorería
            confirme.
          </p>
        )}
        <FormSeccion titulo="Depósito">
          <FormCampo span={12} className="space-y-1">
            <Label className="text-xs">
              Cliente <span className="text-danger-fg">*</span>
            </Label>
            <Controller
              name="clienteId"
              control={form.control}
              render={({ field }) => (
                <ClienteSelectorCxc
                  value={field.value || null}
                  onChange={(item) => field.onChange(item?.id ?? '')}
                />
              )}
            />
            {form.formState.errors.clienteId && (
              <p className="text-xs text-danger-fg">{form.formState.errors.clienteId.message}</p>
            )}
          </FormCampo>
          <FormCampo span={6} className="space-y-1">
            <Label htmlFor="depositoRef" className="text-xs">
              Referencia del depósito <span className="text-danger-fg">*</span>
            </Label>
            <Input
              id="depositoRef"
              placeholder="p. ej. SPEI 2026-07-14 #12345"
              maxLength={80}
              {...form.register('depositoRef')}
            />
            {form.formState.errors.depositoRef && (
              <p className="text-xs text-danger-fg">{form.formState.errors.depositoRef.message}</p>
            )}
          </FormCampo>
          <FormCampo span={6} className="space-y-1">
            <Label htmlFor="remittanceRef" className="text-xs">
              Remittance del cliente <span className="text-danger-fg">*</span>
            </Label>
            <Input
              id="remittanceRef"
              placeholder="Referencia del aviso de pago del cliente"
              maxLength={120}
              {...form.register('remittanceRef')}
            />
            <p className="text-2xs leading-tight text-ink-muted">
              Sin remittance no se puede proponer.
            </p>
            {form.formState.errors.remittanceRef && (
              <p className="text-xs text-danger-fg">
                {form.formState.errors.remittanceRef.message}
              </p>
            )}
          </FormCampo>
          <FormCampo span={6} className="space-y-1">
            <Label htmlFor="montoDeposito" className="text-xs">
              Monto del depósito <span className="text-danger-fg">*</span>
            </Label>
            <Input
              id="montoDeposito"
              type="number"
              step="0.01"
              min="0"
              {...form.register('montoDeposito', { valueAsNumber: true })}
            />
            {excedeDeposito && deposito && (
              <p className="text-xs text-danger-fg">
                Excede lo disponible del depósito ({formatoMonto(deposito.disponible, deposito.moneda)}).
              </p>
            )}
            {form.formState.errors.montoDeposito && (
              <p className="text-xs text-danger-fg">
                {form.formState.errors.montoDeposito.message}
              </p>
            )}
          </FormCampo>
          <FormCampo span={6} className="space-y-1">
            <Label htmlFor="monedaDep" className="text-xs">
              Moneda <span className="text-danger-fg">*</span>
            </Label>
            <select
              id="monedaDep"
              className="h-9 w-full rounded-md border border-line-control bg-transparent px-3 text-sm shadow-card-flat"
              {...form.register('moneda')}
            >
              {/* C7.4: la moneda del depósito identificado no se cambia. */}
              {(deposito ? [deposito.moneda] : MONEDAS_LINEA_CREDITO).map((m) => (
                <option key={m} value={m}>
                  {m}
                </option>
              ))}
            </select>
          </FormCampo>
        </FormSeccion>

        {/* ── Facturas abiertas (matching) ─────────────────────────── */}
        {clienteId ? (
          <section className="space-y-2">
            <div className="flex items-center justify-between gap-2">
              <h3 className="text-sm font-medium">Facturas abiertas del cliente ({moneda})</h3>
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={sugerir}
                disabled={crear.isPending || !facturas.data?.length || !((montoDeposito || 0) > 0)}
                title="Llena los importes de la factura más antigua a la más reciente; revisa y propón (V61)."
              >
                Sugerir
              </Button>
            </div>
            <MatchingAplicacionTable
              query={facturas}
              seleccion={seleccion}
              onCambiar={cambiarSeleccion}
              disabled={crear.isPending}
            />
          </section>
        ) : (
          <p className="rounded-md border border-dashed px-3 py-4 text-center text-sm text-ink-muted">
            Elige el cliente para cargar sus facturas abiertas.
          </p>
        )}

        {/* ── Footer Σ / diferencia / ajuste ───────────────────────── */}
        <section
          className={cn(
            'space-y-1 rounded-md border px-4 py-3 text-sm',
            excedeTolerancia
              ? 'border-line bg-danger-bg'
              : esAjusteNoFiscal
                ? 'border-line bg-warning-bg'
                : 'bg-surface-muted/30',
          )}
          aria-live="polite"
        >
          <ResumenImportes
            moneda={moneda}
            total={sumaAplicada}
            estadoPago={[
              { etiqueta: 'Depósito', monto: montoDeposito || 0 },
              { etiqueta: 'Diferencia', monto: diferencia },
            ]}
          />
          {depositoExcedente && (
            <p className="text-xs text-danger-fg">
              El excedente quedará como saldo a favor por identificar del cliente cuando Tesorería
              confirme el depósito.
            </p>
          )}
          {excedeTolerancia && (
            <p className="text-xs text-danger-fg">
              La diferencia excede la tolerancia no fiscal ({formatoMonto(tolerancia, moneda)}).
              Corrige los importes o gestiona la diferencia con el cliente.
            </p>
          )}
          {esAjusteNoFiscal && (
            <p className="text-xs text-warning-fg ">
              La diferencia se registrará como ajuste no fiscal (p. ej. comisión bancaria) dentro de
              la tolerancia de {formatoMonto(tolerancia, moneda)} — NO genera CFDI.
            </p>
          )}
          {faltaTolerancia && (
            <p className="text-xs text-ink-muted">
              {tolerancias.isError
                ? 'No se pudo cargar la tolerancia no fiscal; reintenta para poder proponer un depósito corto.'
                : 'Cargando tolerancia no fiscal…'}
            </p>
          )}
        </section>
      </SheetBody>
      <SheetFooter className="flex-row justify-end border-t border-line-divider">
        <Button type="button" variant="ghost" onClick={() => onClose()} disabled={crear.isPending}>
          Cancelar
        </Button>
        <Button
          type="submit"
          disabled={crear.isPending || !puedeProponer}
          title={
            sinRemittance
              ? 'Sin remittance del cliente no se puede proponer.'
              : seleccion.size === 0
                ? 'Selecciona al menos una factura.'
                : undefined
          }
        >
          {crear.isPending ? 'Proponiendo…' : 'Proponer aplicación'}
        </Button>
      </SheetFooter>
    </form>
  );
}
