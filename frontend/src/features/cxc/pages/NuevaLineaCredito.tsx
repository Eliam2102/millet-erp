import { FormSeccion, FormCampo } from '@/components/erp';
import { SheetBody, SheetFooter } from '@/components/ui/sheet';

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
import { applyServerErrors, esApiError, useFormIdempotencyKey, codigoSoporte } from '@/lib/api';

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

export function NuevaLineaCredito({ onClose, onDirtyChange }: NuevaLineaCreditoProps) {
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
          toast.success(`Línea de crédito creada (${clienteLabel ?? 'cliente'} · ${res.moneda}).`);
          onClose({ force: true });
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (
              applyServerErrors(form as unknown as Parameters<typeof applyServerErrors>[0], error)
            ) {
              return;
            }
            toast.error(error.problem.title, {
              description: error.problem.detail ?? codigoSoporte(error),
            });
            return;
          }
          toast.error('Error inesperado al crear la línea de crédito.');
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
        <FormSeccion titulo="Cliente y moneda">
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
            <p className="text-2xs leading-tight text-ink-muted">
              Una línea activa por cliente y moneda; sin conversión entre divisas.
            </p>
          </FormCampo>
          <FormCampo span={6} className="space-y-1">
            <Label htmlFor="origen" className="text-xs">
              Origen del límite <span className="text-danger-fg">*</span>
            </Label>
            <select
              id="origen"
              className="h-9 w-full rounded-md border border-line-control bg-transparent px-3 text-sm shadow-card-flat"
              {...form.register('origen', { valueAsNumber: true })}
            >
              <option value={OrigenLineaCredito.Solunion}>SOLUNION (asegurado)</option>
              <option value={OrigenLineaCredito.Interno}>Interno (asignado por Millet)</option>
            </select>
          </FormCampo>
        </FormSeccion>

        <FormSeccion titulo="Condiciones">
          <FormCampo span={4} className="space-y-1">
            <Label htmlFor="limite" className="text-xs">
              Límite <span className="text-danger-fg">*</span>
            </Label>
            <Input
              id="limite"
              type="number"
              step="0.01"
              min="0"
              {...form.register('limite', { valueAsNumber: true })}
            />
            {form.formState.errors.limite && (
              <p className="text-xs text-danger-fg">{form.formState.errors.limite.message}</p>
            )}
          </FormCampo>
          <FormCampo span={4} className="space-y-1">
            <Label htmlFor="plazoDias" className="text-xs">
              Plazo (días) <span className="text-danger-fg">*</span>
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
              <p className="text-xs text-danger-fg">{form.formState.errors.plazoDias.message}</p>
            )}
          </FormCampo>
          <FormCampo span={4} className="space-y-1">
            <Label htmlFor="clasificacion" className="text-xs">
              Clasificación
            </Label>
            <select
              id="clasificacion"
              className="h-9 w-full rounded-md border border-line-control bg-transparent px-3 text-sm shadow-card-flat"
              {...form.register('clasificacion', {
                setValueAs: (v) => (v === SIN_CLASIFICACION || v === '' ? null : v),
              })}
            >
              <option value={SIN_CLASIFICACION}>Sin clasificar</option>
              {CLASIFICACIONES_CREDITO.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
            <p className="text-2xs leading-tight text-ink-muted">
              A/B/C/E — atributo asignado por Crédito y Cobranza.
            </p>
          </FormCampo>
        </FormSeccion>
      </SheetBody>
      <SheetFooter className="flex-row justify-end border-t border-line-divider">
        <Button type="button" variant="ghost" onClick={() => onClose()} disabled={crear.isPending}>
          Cancelar
        </Button>
        <Button type="submit" disabled={crear.isPending}>
          {crear.isPending ? 'Creando…' : 'Crear línea'}
        </Button>
      </SheetFooter>
    </form>
  );
}
