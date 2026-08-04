import { useEffect, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { ClienteSelectorCxc } from '@/features/cxc/components/ClienteSelectorCxc';
import { useCrearLineaCredito } from '@/features/cxc/api/useLineasCredito';
import {
  CLASIFICACIONES_CREDITO,
  MONEDAS_LINEA_CREDITO,
  OrigenLineaCredito,
} from '@/features/cxc/api/types';
import {
  NuevaLineaCreditoSchema,
  SIN_CLASIFICACION,
  type NuevaLineaCreditoValues,
} from '@/features/cxc/schemas/linea-credito';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';

/**
 * <c>&lt;NuevaLineaCredito/&gt;</c> — form del Sheet "Nueva línea de
 * crédito" (CXC-FE-PR2, P4). Una sola línea Activa por (cliente,
 * moneda) — el backend responde <c>LC_DUPLICADA</c> como 422 y se
 * muestra como error de negocio.
 */
export interface NuevaLineaCreditoProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange?: (dirty: boolean) => void;
}

const VALORES_INICIALES: NuevaLineaCreditoValues = {
  clienteId: '',
  moneda: 'MXN',
  limite: 0,
  origen: OrigenLineaCredito.Solunion,
  plazoDias: 30,
  clasificacion: null,
};

export function NuevaLineaCredito({
  onClose,
  onDirtyChange,
}: NuevaLineaCreditoProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearLineaCredito();
  const [clienteLabel, setClienteLabel] = useState<string | undefined>();

  const form = useForm<NuevaLineaCreditoValues>({
    resolver: zodResolver(NuevaLineaCreditoSchema),
    defaultValues: VALORES_INICIALES,
  });

  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange?.(isDirty);
  }, [isDirty, onDirtyChange]);

  function onSubmit(values: NuevaLineaCreditoValues) {
    crear.mutate(
      {
        command: {
          clienteId: values.clienteId,
          moneda: values.moneda,
          limite: values.limite,
          origen: values.origen as OrigenLineaCredito,
          plazoDias: values.plazoDias,
          clasificacion: values.clasificacion,
        },
        idempotencyKey,
      },
      {
        onSuccess: (res) => {
          toast.success(
            `Línea de crédito creada (${clienteLabel ?? 'cliente'} · ${res.moneda}).`,
          );
          onClose({ force: true });
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
          toast.error('Error inesperado al crear la línea de crédito.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} noValidate className="space-y-5">
      <section className="space-y-2">
        <h3 className="text-sm font-medium">Cliente y moneda</h3>
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
            <p className="text-[11px] leading-tight text-muted-foreground">
              Una línea activa por cliente y moneda; sin conversión entre
              divisas.
            </p>
          </div>
          <div className="space-y-1">
            <Label htmlFor="origen" className="text-xs">
              Origen del límite <span className="text-destructive">*</span>
            </Label>
            <select
              id="origen"
              className="h-9 w-full rounded-md border border-input bg-transparent px-3 text-sm shadow-sm"
              {...form.register('origen', { valueAsNumber: true })}
            >
              <option value={OrigenLineaCredito.Solunion}>
                SOLUNION (asegurado)
              </option>
              <option value={OrigenLineaCredito.Interno}>
                Interno (asignado por Millet)
              </option>
            </select>
          </div>
        </div>
      </section>

      <section className="space-y-2">
        <h3 className="text-sm font-medium">Condiciones</h3>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
          <div className="space-y-1">
            <Label htmlFor="limite" className="text-xs">
              Límite <span className="text-destructive">*</span>
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
                setValueAs: (v) =>
                  v === SIN_CLASIFICACION || v === '' ? null : v,
              })}
            >
              <option value={SIN_CLASIFICACION}>Sin clasificar</option>
              {CLASIFICACIONES_CREDITO.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
            <p className="text-[11px] leading-tight text-muted-foreground">
              A/B/C/E — atributo asignado por Crédito y Cobranza.
            </p>
          </div>
        </div>
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
        <Button type="submit" disabled={crear.isPending}>
          {crear.isPending ? 'Creando…' : 'Crear línea'}
        </Button>
      </div>
    </form>
  );
}
