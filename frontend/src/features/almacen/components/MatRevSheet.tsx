import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { FormGrid, FormCampo } from '@/components/erp/forms/FormGrid';
import { SheetBody } from '@/components/ui/sheet';
/* eslint-disable react-hooks/incompatible-library -- react-hook-form watch es intencional en este formulario dinámico; React Compiler omite su memoización de forma segura. */
import { hoyLocalISO } from '@/lib/datetime';
import { Controller, useFieldArray, useForm, type FieldPath } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Plus, Trash2 } from 'lucide-react';
import { toast } from 'sonner';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
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
  ArticuloSelector,
  cabeEnDecimales,
  DECIMALES_FALLBACK,
  MENSAJE_DECIMALES_UNIDAD,
  stepParaDecimales,
  TextAreaField,
  useDecimalesUnidad,
} from '@/components/erp';
import { UbicacionBinSelector } from '@/components/erp/selectors/UbicacionBinSelector';
import { applyServerErrors, esApiError, useFormIdempotencyKey, codigoSoporte } from '@/lib/api';
import {
  BajaPorDanoSchema,
  ReincorporacionTrasRevisionSchema,
  type BajaPorDanoValues,
  type ReincorporacionTrasRevisionValues,
} from '@/features/almacen/schemas/devolucion';
import {
  useBajaPorDano,
  useReincorporarTrasRevision,
  useSubAlmacenes,
} from '@/features/almacen/api';
import { Field } from '@/features/almacen/components/internal/Field';
import { MedidaSalidaSelect } from '@/features/almacen/components/MedidaSalidaSelect';

/**
 * <c>&lt;MatRevSheet/&gt;</c> — sheet doble (Baja por daño /
 * Reincorporar) controlado por <c>modo</c>. Ambos sub-flujos
 * comparten 90% del shape (sub-almacén + fecha + motivo + líneas
 * artículo+cantidad), así que un solo componente con switch evita
 * duplicar 200 LoC.
 *
 * <list type="bullet">
 *   <item><b>baja</b>: MAT-REV → destrucción. Sub-almacén origen
 *     (campo <c>subAlmacenMatRevId</c>).</item>
 *   <item><b>reincorporar</b>: MAT-REV → sub-almacén activo. Sub-almacén
 *     destino (campo <c>subAlmacenDestinoId</c>).</item>
 * </list>
 */
export type MatRevModo = 'baja' | 'reincorporar';

export interface MatRevSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  modo: MatRevModo;
}

export function MatRevSheet({ open, onOpenChange, modo }: MatRevSheetProps) {
  const titulo =
    modo === 'baja' ? 'Baja por daño (material en revisión)' : 'Reincorporar tras revisión';
  const descripcion =
    modo === 'baja'
      ? 'Decisión Calidad: el material en revisión se da de baja por destrucción. No vuelve al inventario activo.'
      : 'Decisión Calidad: el material en revisión vuelve al inventario activo en el sub-almacén destino, al CPP.';

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" size="lg">
        <SheetHeader>
          <SheetTitle>{titulo}</SheetTitle>
          <SheetDescription>{descripcion}</SheetDescription>
        </SheetHeader>

        {open && (
          <FormBody
            modo={modo}
            onSuccess={() => onOpenChange(false)}
            onCancel={() => onOpenChange(false)}
          />
        )}
      </SheetContent>
    </Sheet>
  );
}

function FormBody({
  modo,
  onSuccess,
  onCancel,
}: {
  modo: MatRevModo;
  onSuccess: () => void;
  onCancel: () => void;
}) {
  const idempotencyKey = useFormIdempotencyKey();
  const baja = useBajaPorDano();
  const reincorporar = useReincorporarTrasRevision();
  const subAlmacenesQuery = useSubAlmacenes({ limit: 500 });

  const hoyIso = hoyLocalISO();

  type FormValues = BajaPorDanoValues | ReincorporacionTrasRevisionValues;

  const form = useForm<FormValues>({
    resolver: zodResolver(modo === 'baja' ? BajaPorDanoSchema : ReincorporacionTrasRevisionSchema),
    defaultValues:
      modo === 'baja'
        ? {
            subAlmacenMatRevId: '',
            fechaMovimiento: hoyIso,
            motivo: '',
            lineas: [{ articuloId: '', unidadMedidaId: null, cantidad: 1, ubicacionId: null }],
          }
        : {
            subAlmacenDestinoId: '',
            fechaMovimiento: hoyIso,
            motivo: '',
            lineas: [{ articuloId: '', unidadMedidaId: null, cantidad: 1, ubicacionId: null }],
          },
  });

  const lineasFA = useFieldArray({ control: form.control, name: 'lineas' });

  const lookup = useDecimalesUnidad();

  function onSubmit(values: FormValues) {
    // Advisory: decimales por unidad (unidadMedidaId del artículo; backend autoritativo).
    let decimalesMal = false;
    values.lineas.forEach((l, i) => {
      const dec = lookup.porId((l as { unidadMedidaId?: string | null }).unidadMedidaId);
      // Con medida la cantidad son hojas (enteras): los m² los deriva el envío.
      if (l.productoAwVarianteId) {
        if (!Number.isInteger(l.cantidad)) {
          form.setError(`lineas.${i}.cantidad` as Parameters<typeof form.setError>[0], {
            type: 'hojas',
            message: 'Con medida, captura la cantidad en hojas enteras.',
          });
          decimalesMal = true;
        }
        return;
      }
      if (
        dec != null &&
        l.cantidad != null &&
        Number.isFinite(l.cantidad) &&
        !cabeEnDecimales(l.cantidad, dec)
      ) {
        form.setError(`lineas.${i}.cantidad` as Parameters<typeof form.setError>[0], {
          type: 'decimales',
          message: MENSAJE_DECIMALES_UNIDAD,
        });
        decimalesMal = true;
      }
    });
    if (decimalesMal) return;
    const mut = modo === 'baja' ? baja : reincorporar;
    // unidadMedidaId es solo-frontend → se quita del payload.
    const command = {
      ...values,
      lineas: values.lineas.map((l) => ({
        articuloId: l.articuloId,
        // Con medida: hojas × m² por hoja (el área ya viene a 4 decimales).
        cantidad: l.areaHojaM2 ? Math.round(l.cantidad * l.areaHojaM2 * 10000) / 10000 : l.cantidad,
        productoAwVarianteId: l.productoAwVarianteId ?? null,
        ubicacionId: (l as { ubicacionId?: string | null }).ubicacionId ?? null,
      })),
    };
    mut.mutate(
      // El shape del command coincide con el del schema; TS necesita ayuda
      // por el discriminated.
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      { command: command as any, idempotencyKey },
      {
        onSuccess: (resp) => {
          toast.success(`${modo === 'baja' ? 'Baja' : 'Reincorporación'} ${resp.folio} aplicada`);
          onSuccess();
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (
              applyServerErrors(form as unknown as Parameters<typeof applyServerErrors>[0], error)
            ) {
              return;
            }
            toast.error(error.problem.title, {
              description: codigoSoporte(error),
            });
            return;
          }
          toast.error('Error inesperado.');
        },
      },
    );
  }

  const isPending = baja.isPending || reincorporar.isPending;
  const subAlmacenLabel =
    modo === 'baja' ? 'Sub-almacén de material en revisión (origen)' : 'Sub-almacén destino';
  const subAlmacenField = modo === 'baja' ? 'subAlmacenMatRevId' : 'subAlmacenDestinoId';
  // C7.2b: bin por línea. Baja (salida) elige por saldo; reincorporación
  // (entrada) por asignación. El sub-almacén de cabecera acota el selector.
  const subAlmacenId = form.watch(subAlmacenField as FieldPath<FormValues>) as string | undefined;
  const binModo = modo === 'baja' ? 'saldo' : 'asignacion';

  const errors = form.formState.errors as Record<string, { message?: string } | undefined>;

  return (
    <>
      <SheetBody>
        <form
          id="mat-rev-form"
          onSubmit={form.handleSubmit(onSubmit)}
          noValidate
          className="space-y-4"
        >
          <FormSeccion numero={1} titulo="Datos del movimiento">
            <FormCampo span={6}>
              <Field label={subAlmacenLabel} required error={errors[subAlmacenField]?.message}>
                <Controller
                  // eslint-disable-next-line @typescript-eslint/no-explicit-any
                  name={subAlmacenField as any}
                  control={form.control}
                  render={({ field }) => (
                    <Select
                      value={(field.value as string) || ''}
                      onValueChange={(v) => field.onChange(v)}
                      disabled={subAlmacenesQuery.isLoading}
                    >
                      <SelectTrigger>
                        <SelectValue placeholder="Selecciona sub-almacén" />
                      </SelectTrigger>
                      <SelectContent>
                        {(subAlmacenesQuery.data?.items ?? []).map((s) => (
                          <SelectItem key={s.id} value={s.id}>
                            {s.clave} · {s.nombre}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  )}
                />
              </Field>
            </FormCampo>

            <FormCampo span={6}>
              <Field label="Fecha de movimiento" required error={errors.fechaMovimiento?.message}>
                <Controller
                  name="fechaMovimiento"
                  control={form.control}
                  render={({ field }) => (
                    <Input {...field} type="date" value={(field.value as string) ?? ''} />
                  )}
                />
              </Field>
            </FormCampo>
          </FormSeccion>

          <Field label="Motivo" required error={errors.motivo?.message}>
            <Controller
              name="motivo"
              control={form.control}
              render={({ field }) => (
                <TextAreaField
                  value={(field.value as string) ?? null}
                  onChange={(v) => field.onChange(v ?? '')}
                  maxLength={500}
                  minRows={2}
                />
              )}
            />
          </Field>

          <section className="space-y-2">
            <div className="flex items-center justify-between">
              <h3 className="text-sm font-semibold">Partidas ({lineasFA.fields.length})</h3>
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={() => lineasFA.append({ articuloId: '', cantidad: 1, ubicacionId: null })}
              >
                <Plus className="mr-2 size-3.5" />
                Agregar partida
              </Button>
            </div>

            <div className="space-y-2">
              {lineasFA.fields.map((field, index) => {
                const lineaErrs = (
                  form.formState.errors.lineas as
                    | Array<
                        | { articuloId?: { message?: string }; cantidad?: { message?: string } }
                        | undefined
                      >
                    | undefined
                )?.[index];
                const umIdLinea = form.watch(
                  `lineas.${index}.unidadMedidaId` as FieldPath<FormValues>,
                ) as string | null | undefined;
                const articuloIdLinea = form.watch(
                  `lineas.${index}.articuloId` as FieldPath<FormValues>,
                ) as string | undefined;
                const binErr = (lineaErrs as { ubicacionId?: { message?: string } } | undefined)
                  ?.ubicacionId?.message;
                const varianteLinea = form.watch(
                  `lineas.${index}.productoAwVarianteId` as FieldPath<FormValues>,
                ) as string | null | undefined;
                const decimalesLinea = varianteLinea
                  ? 0
                  : (lookup.porId(umIdLinea) ?? DECIMALES_FALLBACK);
                return (
                  <div
                    key={field.id}
                    className="rounded-md border border-dashed border-brand/40 bg-brand/5 p-3 space-y-2"
                  >
                    <div className="flex items-start justify-between gap-2">
                      <span className="text-xs font-semibold text-ink-muted">
                        Partida #{index + 1}
                      </span>
                      <Button
                        type="button"
                        size="sm"
                        variant="ghost"
                        onClick={() => lineasFA.remove(index)}
                        aria-label={`Quitar partida ${index + 1}`}
                      >
                        <Trash2 className="h-4 w-4" />
                      </Button>
                    </div>
                    <FormGrid>
                      <FormCampo span={6}>
                        <Field label="Artículo" required error={lineaErrs?.articuloId?.message}>
                          <Controller
                            name={`lineas.${index}.articuloId` as const}
                            control={form.control}
                            render={({ field }) => (
                              <ArticuloSelector
                                value={(field.value as string) || null}
                                onChange={(id) => field.onChange(id ?? '')}
                                onSelect={(articulo) => {
                                  form.setValue(
                                    `lineas.${index}.unidadMedidaId` as Parameters<
                                      typeof form.setValue
                                    >[0],
                                    articulo.unidadMedidaId,
                                  );
                                  // Otro artículo, otras medidas: limpia la elegida.
                                  form.setValue(
                                    `lineas.${index}.productoAwVarianteId` as FieldPath<FormValues>,
                                    null as never,
                                  );
                                }}
                              />
                            )}
                          />
                        </Field>
                      </FormCampo>
                      <FormCampo span={12}>
                        <Controller
                          name={`lineas.${index}.productoAwVarianteId` as FieldPath<FormValues>}
                          control={form.control}
                          render={({ field }) => (
                            <MedidaSalidaSelect
                              articuloId={articuloIdLinea}
                              value={(field.value as string) ?? null}
                              ariaLabel={`Medida de la hoja, partida ${index + 1}`}
                              onChange={(id, area) => {
                                field.onChange(id);
                                form.setValue(
                                  `lineas.${index}.areaHojaM2` as FieldPath<FormValues>,
                                  (id ? area : null) as never,
                                );
                              }}
                            />
                          )}
                        />
                      </FormCampo>
                      <FormCampo span={6}>
                        <Field
                          label={varianteLinea ? 'Cantidad (hojas)' : 'Cantidad'}
                          required
                          error={lineaErrs?.cantidad?.message}
                        >
                          <Controller
                            name={`lineas.${index}.cantidad` as const}
                            control={form.control}
                            render={({ field }) => (
                              <Input
                                type="number"
                                inputMode="decimal"
                                step={stepParaDecimales(decimalesLinea)}
                                min="0"
                                value={(field.value as number) ?? ''}
                                onChange={(e) =>
                                  field.onChange(
                                    e.target.value === '' ? '' : Number(e.target.value),
                                  )
                                }
                              />
                            )}
                          />
                        </Field>
                      </FormCampo>
                      <FormCampo span={6}>
                        <Field
                          label="Ubicación (rack)"
                          required={modo === 'reincorporar'}
                          error={binErr}
                        >
                          <Controller
                            name={`lineas.${index}.ubicacionId` as FieldPath<FormValues>}
                            control={form.control}
                            render={({ field }) => (
                              <UbicacionBinSelector
                                modo={binModo}
                                articuloId={articuloIdLinea}
                                subAlmacenId={subAlmacenId}
                                value={(field.value as string) ?? null}
                                onChange={(id) => field.onChange(id ?? '')}
                              />
                            )}
                          />
                        </Field>
                      </FormCampo>
                    </FormGrid>
                  </div>
                );
              })}
            </div>
          </section>
        </form>
      </SheetBody>

      <SheetFooter>
        <Button type="button" variant="ghost" onClick={onCancel} disabled={isPending}>
          Cancelar
        </Button>
        <Button type="submit" form="mat-rev-form" disabled={isPending}>
          {isPending ? 'Aplicando…' : modo === 'baja' ? 'Aplicar baja' : 'Reincorporar'}
        </Button>
      </SheetFooter>
    </>
  );
}
