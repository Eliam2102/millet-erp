import { useEffect, useMemo, useState } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { ClienteSelectorCxc } from '@/features/cxc/components/ClienteSelectorCxc';
import {
  MatchingAplicacionTable,
} from '@/features/cxc/components/MatchingAplicacionTable';
import {
  useCrearPropuestaAplicacion,
  useFacturasAbiertas,
  useToleranciasNoFiscal,
} from '@/features/cxc/api/useAplicaciones';
import { MONEDAS_LINEA_CREDITO } from '@/features/cxc/api/types';
import { formatoMonto } from '@/features/cxc/lib/glosario';
import {
  PropuestaAplicacionSchema,
  type PropuestaAplicacionValues,
} from '@/features/cxc/schemas/propuesta-aplicacion';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;NuevaPropuestaAplicacion/&gt;</c> — matching depósito ↔
 * facturas (05-frontend-diseno §4.1): cabecera del depósito (monto,
 * moneda, referencia, remittance) + facturas abiertas del cliente con
 * checkbox e importe editable inline. Footer con Σ aplicado, diferencia
 * y — si el depósito viene corto dentro de tolerancia — el ajuste no
 * fiscal (comisión bancaria) que NO genera CFDI. Sin remittance el
 * botón queda deshabilitado (regla 2.1).
 */
export interface NuevaPropuestaAplicacionProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange?: (dirty: boolean) => void;
}

export function NuevaPropuestaAplicacion({
  onClose,
  onDirtyChange,
}: NuevaPropuestaAplicacionProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearPropuestaAplicacion();
  const tolerancias = useToleranciasNoFiscal();

  const form = useForm<PropuestaAplicacionValues>({
    resolver: zodResolver(PropuestaAplicacionSchema),
    defaultValues: {
      clienteId: '',
      depositoRef: '',
      montoDeposito: 0,
      moneda: 'MXN',
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
  // Redondeo a centavos: sumaAplicada es una suma de floats; sin redondear,
  // 50.05 + 50.05 vs 100.10 puede dar ±1e-13 y disparar excedente/ajuste
  // falsos ante un match exacto.
  const diferencia =
    Math.round(((montoDeposito || 0) - sumaAplicada) * 100) / 100;
  const toleranciaLista = tolerancias.isSuccess;
  const tolerancia = tolerancias.data?.[moneda] ?? 0;
  // Un depósito corto (diferencia < 0) necesita la tolerancia para decidir
  // ajuste-no-fiscal vs excede. Si aún no cargó (o falló), NO se trata como
  // "excede con tolerancia 0" (bloqueo engañoso): se bloquea con aviso.
  const faltaTolerancia = diferencia < 0 && !toleranciaLista;
  const esAjusteNoFiscal =
    toleranciaLista && diferencia < 0 && Math.abs(diferencia) < tolerancia;
  const excedeTolerancia =
    toleranciaLista && diferencia < 0 && Math.abs(diferencia) >= tolerancia;
  const depositoExcedente = diferencia > 0 && seleccion.size > 0;
  const importesInvalidos =
    Array.from(seleccion.entries()).some(([uuid, imp]) => {
      const f = facturas.data?.find((x) => x.uuid === uuid);
      return imp <= 0 || (f != null && imp > f.saldo);
    });

  const sinRemittance = (remittanceRef ?? '').trim() === '';
  const puedeProponer =
    !sinRemittance &&
    seleccion.size > 0 &&
    !importesInvalidos &&
    !excedeTolerancia &&
    !depositoExcedente &&
    !faltaTolerancia &&
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
          facturas: Array.from(seleccion.entries()).map(
            ([facturaUuid, importeAplicado]) => ({
              facturaUuid,
              importeAplicado,
              numParcialidad: null,
            }),
          ),
        },
        idempotencyKey,
      },
      {
        onSuccess: (res) => {
          toast.success(
            `Propuesta creada para el depósito ${res.depositoRef} (${res.facturas.length} factura${res.facturas.length === 1 ? '' : 's'}).`,
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
                  : error.traceId
                    ? `Código: ${error.traceId}`
                    : undefined),
            });
            return;
          }
          toast.error('Error inesperado al crear la propuesta.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} noValidate className="space-y-5">
      {/* ── Depósito ─────────────────────────────────────────────── */}
      <section className="space-y-2">
        <h3 className="text-sm font-medium">Depósito</h3>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <div className="space-y-1 sm:col-span-2">
            <Label className="text-xs">
              Cliente <span className="text-destructive">*</span>
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
              <p className="text-xs text-destructive">
                {form.formState.errors.clienteId.message}
              </p>
            )}
          </div>
          <div className="space-y-1">
            <Label htmlFor="depositoRef" className="text-xs">
              Referencia del depósito <span className="text-destructive">*</span>
            </Label>
            <Input
              id="depositoRef"
              placeholder="p. ej. SPEI 2026-07-14 #12345"
              maxLength={80}
              {...form.register('depositoRef')}
            />
            {form.formState.errors.depositoRef && (
              <p className="text-xs text-destructive">
                {form.formState.errors.depositoRef.message}
              </p>
            )}
          </div>
          <div className="space-y-1">
            <Label htmlFor="remittanceRef" className="text-xs">
              Remittance del cliente <span className="text-destructive">*</span>
            </Label>
            <Input
              id="remittanceRef"
              placeholder="Referencia del aviso de pago del cliente"
              maxLength={120}
              {...form.register('remittanceRef')}
            />
            <p className="text-[11px] leading-tight text-muted-foreground">
              Sin remittance no se puede proponer (regla 2.1).
            </p>
            {form.formState.errors.remittanceRef && (
              <p className="text-xs text-destructive">
                {form.formState.errors.remittanceRef.message}
              </p>
            )}
          </div>
          <div className="space-y-1">
            <Label htmlFor="montoDeposito" className="text-xs">
              Monto del depósito <span className="text-destructive">*</span>
            </Label>
            <Input
              id="montoDeposito"
              type="number"
              step="0.01"
              min="0"
              {...form.register('montoDeposito', { valueAsNumber: true })}
            />
            {form.formState.errors.montoDeposito && (
              <p className="text-xs text-destructive">
                {form.formState.errors.montoDeposito.message}
              </p>
            )}
          </div>
          <div className="space-y-1">
            <Label htmlFor="monedaDep" className="text-xs">
              Moneda <span className="text-destructive">*</span>
            </Label>
            <select
              id="monedaDep"
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
        </div>
      </section>

      {/* ── Facturas abiertas (matching) ─────────────────────────── */}
      {clienteId ? (
        <section className="space-y-2">
          <h3 className="text-sm font-medium">
            Facturas abiertas del cliente ({moneda})
          </h3>
          <MatchingAplicacionTable
            query={facturas}
            seleccion={seleccion}
            onCambiar={cambiarSeleccion}
            disabled={crear.isPending}
          />
        </section>
      ) : (
        <p className="rounded-md border border-dashed px-3 py-4 text-center text-sm text-muted-foreground">
          Elige el cliente para cargar sus facturas abiertas.
        </p>
      )}

      {/* ── Footer Σ / diferencia / ajuste ───────────────────────── */}
      <section
        className={cn(
          'space-y-1 rounded-md border px-4 py-3 text-sm',
          excedeTolerancia || depositoExcedente
            ? 'border-destructive/50 bg-destructive/5'
            : esAjusteNoFiscal
              ? 'border-amber-300 bg-amber-50/60 dark:border-amber-800 dark:bg-amber-950/30'
              : 'bg-muted/30',
        )}
        aria-live="polite"
      >
        <div className="flex flex-wrap items-center justify-between gap-2 font-mono tabular-nums">
          <span>Σ aplicado: {formatoMonto(sumaAplicada, moneda)}</span>
          <span>
            Diferencia:{' '}
            <span
              className={cn(
                diferencia !== 0 && 'font-semibold',
                (excedeTolerancia || depositoExcedente) && 'text-destructive',
              )}
            >
              {formatoMonto(diferencia, moneda)}
            </span>
          </span>
        </div>
        {depositoExcedente && (
          <p className="text-xs text-destructive">
            El depósito excede la suma aplicada — un excedente no es ajuste
            no fiscal; captúralo como anticipo o corrige el desglose.
          </p>
        )}
        {excedeTolerancia && (
          <p className="text-xs text-destructive">
            La diferencia excede la tolerancia no fiscal (
            {formatoMonto(tolerancia, moneda)}). Corrige los importes o
            gestiona la diferencia con el cliente.
          </p>
        )}
        {esAjusteNoFiscal && (
          <p className="text-xs text-amber-800 dark:text-amber-300">
            La diferencia se registrará como ajuste no fiscal (p. ej.
            comisión bancaria) dentro de la tolerancia de{' '}
            {formatoMonto(tolerancia, moneda)} — NO genera CFDI.
          </p>
        )}
        {faltaTolerancia && (
          <p className="text-xs text-muted-foreground">
            {tolerancias.isError
              ? 'No se pudo cargar la tolerancia no fiscal; reintenta para poder proponer un depósito corto.'
              : 'Cargando tolerancia no fiscal…'}
          </p>
        )}
      </section>

      <div className="flex items-center justify-end gap-2 border-t pt-4">
        <Button
          type="button"
          variant="ghost"
          onClick={() => onClose()}
          disabled={crear.isPending}
        >
          Cancelar
        </Button>
        <Button
          type="submit"
          disabled={crear.isPending || !puedeProponer}
          title={
            sinRemittance
              ? 'Sin remittance del cliente no se puede proponer (regla 2.1).'
              : seleccion.size === 0
                ? 'Selecciona al menos una factura.'
                : undefined
          }
        >
          {crear.isPending ? 'Proponiendo…' : 'Proponer aplicación'}
        </Button>
      </div>
    </form>
  );
}
