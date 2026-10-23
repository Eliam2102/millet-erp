import { SheetBody } from '@/components/ui/sheet';
import { FormCampo } from '@/components/erp/forms/FormGrid';
import { FormGrid } from '@/components/erp/forms/FormGrid';
import { useEffect, useId, useState } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { CondicionesPagoSelector, DatePickerField, Field, ProveedorSelector } from '@/components/erp';
import { mensajeErrorAdjunto } from '@/components/erp/adjuntos/api';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { comprasKeys } from '@/features/compras/api/keys';
import {
  TIPO_ADJUNTO_COTIZACION_ID,
  type CotizacionComparativo,
  type PartidaComparativo,
} from '@/features/compras/api/types';
import {
  useEditarCotizacion,
  useRegistrarCotizacion,
} from '@/features/compras/api/useCotizaciones';
import { ConMotivo } from '@/features/compras/components/ConMotivo';
import {
  aRegistrarCotizacionRequest,
  cotizacionSchema,
  type CotizacionValues,
} from '@/features/compras/schemas/cotizacion';
import { applyServerErrors, esApiError, useFormIdempotencyKey } from '@/lib/api';
import { hoyLocalISO } from '@/lib/datetime';
import { useUploadFile } from '@/lib/hooks/useUploadFile';
import { useUnsavedChangesGuard } from '@/lib/hooks/useUnsavedChangesGuard';

const MONEDAS = ['MXN', 'USD', 'EUR'] as const;

export interface CotizacionSheetProps {
  abierto: boolean;
  onClose: (opts?: { force?: boolean }) => void;
  requisicionId: string;
  folio: string;
  partidas: PartidaComparativo[];
  /** Si viene, el sheet edita esa cotización (solo antes de elegir). */
  cotizacion?: CotizacionComparativo;
}

const etiquetaPartida = (p: PartidaComparativo) =>
  `${p.posicion} · ${p.articuloClave ?? ''} ${p.articuloNombre ?? ''} · ${p.cantidadDeCompra} ${p.unidad}`.replace(
    /\s+/g,
    ' ',
  );

function FormularioCotizacion({
  onClose,
  onCancelar,
  onDirtyChange,
  requisicionId,
  partidas,
  cotizacion,
}: Pick<CotizacionSheetProps, 'onClose' | 'requisicionId' | 'partidas' | 'cotizacion'> & {
  onCancelar: () => void;
  onDirtyChange: (sucio: boolean) => void;
}) {
  const queryClient = useQueryClient();
  const idempotencyKey = useFormIdempotencyKey();
  const registrar = useRegistrarCotizacion(requisicionId);
  const editar = useEditarCotizacion(requisicionId);
  const subida = useUploadFile();
  const [archivo, setArchivo] = useState<File | null>(null);
  const porComprar = partidas.filter((p) => p.saldoPorComprar > 0);

  const form = useForm<CotizacionValues>({
    resolver: zodResolver(cotizacionSchema),
    mode: 'onBlur',
    defaultValues: {
      proveedorId: cotizacion?.proveedorId ?? '',
      folioProveedor: cotizacion?.folioProveedor ?? '',
      moneda: cotizacion?.moneda ?? 'MXN',
      fechaCotizacion: cotizacion?.fechaCotizacion ?? hoyLocalISO(),
      vigenteHasta: cotizacion?.vigenteHasta ?? '',
      condicionesCredito: cotizacion?.condicionesCredito ?? '',
      condicionesPagoId: cotizacion?.condicionesPagoId ?? '',
      fechaEntrega: cotizacion?.fechaEntrega ?? '',
      lineas: porComprar.map((p) => {
        const previa = cotizacion?.lineas.find(
          (l) => l.lineaRequisicionId === p.lineaRequisicionId,
        );
        return {
          lineaRequisicionId: p.lineaRequisicionId,
          unidadRequisicion: p.unidad,
          unidadCotizada: previa?.unidadCotizada ?? p.unidad,
          factorAUnidadRq: previa?.factorAUnidadRq ?? 1,
          precioUnitario: previa?.precioUnitario ?? Number.NaN,
          diasEntrega: previa?.diasEntrega ?? null,
        };
      }),
    },
  });
  const { isDirty, errors } = form.formState;
  useUnsavedChangesGuard(isDirty);
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);
  const valores = useWatch({ control: form.control });

  const faltante = !valores.proveedorId
    ? 'proveedor'
    : !valores.vigenteHasta
      ? 'vigencia'
      : (valores.lineas ?? []).findIndex(
            (l) => !l?.precioUnitario || Number.isNaN(l.precioUnitario),
          ) >= 0
        ? 'precio de cada partida'
        : null;

  async function onSubmit(values: CotizacionValues) {
    try {
      const request = aRegistrarCotizacionRequest(values);
      const creada = cotizacion
        ? await editar.mutateAsync({ cotizacionId: cotizacion.id, values: request, idempotencyKey })
        : await registrar.mutateAsync({ values: request, idempotencyKey });
      if (archivo) {
        try {
          await subida.upload({
            url: `/api/v1/compras/cotizaciones/${creada.id}/adjuntos`,
            file: archivo,
            fieldName: 'archivo',
            extraFields: { tipoDocumentoId: TIPO_ADJUNTO_COTIZACION_ID },
            idempotencyKey: crypto.randomUUID(),
          });
          await queryClient.invalidateQueries({
            queryKey: comprasKeys.cotizaciones(requisicionId),
          });
        } catch (err) {
          toast.warning(
            cotizacion ? 'Cotización guardada sin el PDF nuevo' : 'Cotización registrada sin PDF',
            { description: mensajeErrorAdjunto(err) },
          );
          onClose({ force: true });
          return;
        }
      }
      toast.success(cotizacion ? 'Cotización actualizada' : 'Cotización registrada');
      onClose({ force: true });
    } catch (err) {
      if (esApiError(err)) {
        const marcado = applyServerErrors(
          form as unknown as Parameters<typeof applyServerErrors>[0],
          err,
        );
        if (marcado === true || marcado === 'campo') return;
        const { title, detail } = err.problem;
        toast.error(
          err.problem.status === 409 ? 'La cotización cambió' : title,
          err.problem.status === 409
            ? { description: 'Recarga el comparativo e inténtalo de nuevo.' }
            : detail && detail !== title
              ? { description: detail }
              : undefined,
        );
      } else {
        toast.error('Error inesperado al guardar la cotización.');
      }
    }
  }

  const archivoInputId = useId();
  const guardando = registrar.isPending || editar.isPending || subida.isUploading;

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      className="flex min-h-0 flex-1 flex-col"
      noValidate
    >
      <SheetBody className="space-y-4">
        <FormGrid className="">
          <FormCampo span={12}>
            <Controller
              control={form.control}
              name="proveedorId"
              render={({ field, fieldState }) => (
                <Field
                  label="Proveedor"
                  required
                  error={fieldState.error?.message}
                  className="sm:col-span-2"
                >
                  {(c) => (
                    <ProveedorSelector
                      triggerProps={c}
                      value={field.value || null}
                      onChange={(v) => field.onChange(v ?? '')}
                    />
                  )}
                </Field>
              )}
            />
          </FormCampo>
          <FormCampo span={6}>
            <Controller control={form.control} name="condicionesPagoId" render={({ field, fieldState }) => (
              <Field label="Condiciones de pago" optional error={fieldState.error?.message}>
                {(c) => <CondicionesPagoSelector triggerProps={c} value={field.value || null} onChange={(v) => field.onChange(v ?? '')} />}
              </Field>
            )} />
          </FormCampo>
          <FormCampo span={6}>
            <Field label="Fecha de entrega" optional error={errors.fechaEntrega?.message}>
              <Input type="date" {...form.register('fechaEntrega')} />
            </Field>
          </FormCampo>
          <FormCampo span={6}>
            <Field label="Folio del proveedor" optional error={errors.folioProveedor?.message}>
              <Input {...form.register('folioProveedor')} className="font-mono" />
            </Field>
          </FormCampo>
          <FormCampo span={6}>
            <Controller
              control={form.control}
              name="moneda"
              render={({ field, fieldState }) => (
                <Field label="Moneda" required error={fieldState.error?.message}>
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger>
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {MONEDAS.map((m) => (
                        <SelectItem key={m} value={m}>
                          {m}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </Field>
              )}
            />
          </FormCampo>
          <FormCampo span={6}>
            <Controller
              control={form.control}
              name="fechaCotizacion"
              render={({ field, fieldState }) => (
                <Field label="Fecha de la cotización" required error={fieldState.error?.message}>
                  <DatePickerField
                    value={field.value || null}
                    onChange={(v) => field.onChange(v ?? '')}
                    maxDate={new Date()}
                  />
                </Field>
              )}
            />
          </FormCampo>
          <FormCampo span={6}>
            <Controller
              control={form.control}
              name="vigenteHasta"
              render={({ field, fieldState }) => (
                <Field label="Vigente hasta" required error={fieldState.error?.message}>
                  <DatePickerField
                    value={field.value || null}
                    onChange={(v) => field.onChange(v ?? '')}
                  />
                </Field>
              )}
            />
          </FormCampo>
          <FormCampo span={12}>
            <Field
              label="Crédito"
              optional
              error={errors.condicionesCredito?.message}
              className="sm:col-span-2"
            >
              <Input {...form.register('condicionesCredito')} placeholder="Ej. 30 días" />
            </Field>
          </FormCampo>
        </FormGrid>

        <section aria-labelledby="partidas-cotizadas" className="space-y-2">
          <h3 id="partidas-cotizadas" className="text-sm font-semibold text-ink">
            Partidas de la requisición
          </h3>
          {errors.lineas?.message && (
            <p className="text-xs text-danger-fg">{errors.lineas.message}</p>
          )}
          {porComprar.map((p, i) => {
            const linea = valores.lineas?.[i];
            const distinta =
              (linea?.unidadCotizada ?? '').trim().toUpperCase() !== p.unidad.trim().toUpperCase();
            const e = errors.lineas?.[i];
            return (
              <div
                key={p.lineaRequisicionId}
                className="space-y-2 rounded-md border border-line-row p-3"
              >
                <p className="text-sm font-medium text-ink">{etiquetaPartida(p)}</p>
                <FormGrid className="">
                  <FormCampo span={3}>
                    <Field label="Unidad cotizada" required error={e?.unidadCotizada?.message}>
                      <Input
                        {...form.register(`lineas.${i}.unidadCotizada`, {
                          onChange: (ev) => {
                            if (
                              ev.target.value.trim().toUpperCase() === p.unidad.trim().toUpperCase()
                            ) {
                              form.setValue(`lineas.${i}.factorAUnidadRq`, 1);
                            }
                          },
                        })}
                        className="uppercase"
                      />
                    </Field>
                  </FormCampo>
                  <FormCampo span={3}>
                    {distinta ? (
                      <Field
                        label={`Equivale a … ${p.unidad}`}
                        required
                        error={e?.factorAUnidadRq?.message}
                      >
                        <Input
                          type="number"
                          step="any"
                          inputMode="decimal"
                          className="tabular-nums"
                          {...form.register(`lineas.${i}.factorAUnidadRq`, { valueAsNumber: true })}
                        />
                      </Field>
                    ) : (
                      <p className="self-end pb-2 text-xs text-ink-muted">Factor 1</p>
                    )}
                  </FormCampo>
                  <FormCampo span={3}>
                    <Field label="Precio" required error={e?.precioUnitario?.message}>
                      <Input
                        type="number"
                        step="any"
                        inputMode="decimal"
                        className="tabular-nums"
                        {...form.register(`lineas.${i}.precioUnitario`, { valueAsNumber: true })}
                      />
                    </Field>
                  </FormCampo>
                  <FormCampo span={3}>
                    <Field label="Entrega (días)" optional error={e?.diasEntrega?.message}>
                      <Input
                        type="number"
                        min={0}
                        max={365}
                        className="tabular-nums"
                        {...form.register(`lineas.${i}.diasEntrega`, {
                          setValueAs: (v) => (v === '' || v == null ? null : Number(v)),
                        })}
                      />
                    </Field>
                  </FormCampo>
                </FormGrid>
              </div>
            );
          })}
        </section>

        <div className="space-y-2">
          <Field
            label="Cotización del proveedor (PDF) *"
            hint={
              cotizacion?.archivoNombre
                ? `Actual: ${cotizacion.archivoNombre}. Si subes uno nuevo, el anterior se conserva en el expediente.`
                : 'Se adjunta a la OC si esta cotización resulta elegida'
            }
          >
            <div className="flex items-center gap-3">
              <label
                htmlFor={archivoInputId}
                className="inline-flex cursor-pointer items-center rounded-md border border-line-control bg-surface-card px-3 py-1.5 text-sm font-medium has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-brand hover:bg-surface-subtle"
              >
                Seleccionar PDF
                <input
                  id={archivoInputId}
                  type="file"
                  aria-label="Cotización del proveedor (PDF)"
                  accept="application/pdf"
                  className="sr-only"
                  onChange={(ev) => setArchivo(ev.target.files?.[0] ?? null)}
                />
              </label>
              <span className="min-w-0 truncate text-sm text-ink-muted">
                {archivo?.name ?? 'Ningún archivo seleccionado'}
              </span>
            </div>
          </Field>
          {!archivo && !cotizacion?.archivoAdjuntoId && (
            <p role="status" className="rounded-md bg-warning-bg px-3 py-2 text-xs text-warning-fg">
              Sin PDF la cotización queda registrada pero no se podrá elegir.
            </p>
          )}
        </div>
      </SheetBody>

      <SheetFooter className="border-t border-line-row p-4 sm:justify-between">
        <Button type="button" variant="ghost" onClick={onCancelar} disabled={guardando}>
          Cancelar
        </Button>
        <ConMotivo motivo={faltante ? `Falta: ${faltante}` : null}>
          <Button type="submit" disabled={guardando || faltante !== null}>
            {guardando ? 'Guardando…' : cotizacion ? 'Guardar cambios' : 'Registrar cotización'}
          </Button>
        </ConMotivo>
      </SheetFooter>
    </form>
  );
}

/** F1-COM-02 — Sheet «Registrar cotización» (cabecera, partidas y PDF). El PDF se sube con el id ya creado. */
export function CotizacionSheet({
  abierto,
  onClose,
  folio,
  requisicionId,
  partidas,
  cotizacion,
}: CotizacionSheetProps) {
  const [sucio, setSucio] = useState(false);
  // Confirm al cerrar con cambios (patrón NuevaRequisicionProvider).
  const solicitarCierre = () => {
    if (sucio && !window.confirm('Tienes cambios sin guardar. ¿Cerrar y descartarlos?')) return;
    onClose();
  };
  return (
    <Sheet open={abierto} onOpenChange={(o) => !o && solicitarCierre()}>
      <SheetContent side="right" size="lg">
        <SheetHeader>
          <SheetTitle>{cotizacion ? 'Editar cotización' : 'Registrar cotización'}</SheetTitle>
          <SheetDescription>{folio} · solo Compras</SheetDescription>
        </SheetHeader>
        {abierto && (
          <FormularioCotizacion
            onClose={onClose}
            onCancelar={solicitarCierre}
            onDirtyChange={setSucio}
            requisicionId={requisicionId}
            partidas={partidas}
            cotizacion={cotizacion}
          />
        )}
      </SheetContent>
    </Sheet>
  );
}
