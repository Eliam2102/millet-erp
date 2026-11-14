import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { FormGrid, FormCampo } from '@/components/erp/forms/FormGrid';
import { SheetBody } from '@/components/ui/sheet';
import { hoyLocalISO } from '@/lib/datetime';
import { Controller, useFieldArray, useForm, useWatch } from 'react-hook-form';
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
import { TextAreaField } from '@/components/erp';
import { applyServerErrors, esApiError, useFormIdempotencyKey, codigoSoporte } from '@/lib/api';
import {
  AplicarDevolucionInternaSchema,
  type AplicarDevolucionInternaValues,
} from '@/features/almacen/schemas/devolucion';
import { useAplicarDevolucionInterna, useSubAlmacenes } from '@/features/almacen/api';
import { Field } from '@/features/almacen/components/internal/Field';
import { AsignarUbicacionEnLinea } from '@/features/almacen/components/AsignarUbicacionEnLinea';
import { UbicacionBinSelector } from '@/components/erp/selectors/UbicacionBinSelector';
import { useSalida, useSalidas } from '@/features/almacen/api/useSalidas';
import { EstadoMovimiento } from '@/features/almacen/api/types';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';

export interface AplicarDevolucionInternaSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/** Valores que acepta el backend (A15: «Dañado» va a material en revisión). */
const ESTADOS_MATERIAL = [
  { valor: 'Integro', texto: 'Íntegro' },
  { valor: 'UsadoParcial', texto: 'Usado parcialmente' },
  { valor: 'Danado', texto: 'Dañado' },
] as const;

/**
 * <c>&lt;AplicarDevolucionInternaSheet/&gt;</c> — sub-flujo 8.A:
 * el solicitante devuelve material recibido al almacén. El backend
 * restituye al costo de la salida origen (no CPP) — sin pérdida fiscal.
 */
export function AplicarDevolucionInternaSheet({
  open,
  onOpenChange,
}: AplicarDevolucionInternaSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" size="lg">
        <SheetHeader>
          <SheetTitle>Devolución interna</SheetTitle>
          <SheetDescription>
            Devuelve material desde el solicitante al almacén (material en revisión, sub-almacén
            destino, o regreso al origen). El sistema restituye al costo de la salida origen.
          </SheetDescription>
        </SheetHeader>

        {open && (
          <FormBody onSuccess={() => onOpenChange(false)} onCancel={() => onOpenChange(false)} />
        )}
      </SheetContent>
    </Sheet>
  );
}

function FormBody({ onSuccess, onCancel }: { onSuccess: () => void; onCancel: () => void }) {
  const idempotencyKey = useFormIdempotencyKey();
  const aplicar = useAplicarDevolucionInterna();
  const subAlmacenesQuery = useSubAlmacenes({ limit: 500 });

  const hoyIso = hoyLocalISO();

  const form = useForm<AplicarDevolucionInternaValues>({
    resolver: zodResolver(AplicarDevolucionInternaSchema),
    defaultValues: {
      salidaOrigenId: '',
      subAlmacenDestinoId: '',
      fechaMovimiento: hoyIso,
      estadoMaterial: 'Integro',
      motivo: '',
      observaciones: null,
      lineas: [{ lineaSalidaOrigenId: '', cantidadADevolver: 1, ubicacionId: '' }],
    },
  });

  const lineasFA = useFieldArray({ control: form.control, name: 'lineas' });

  // C7.2b: el bin destino se elige entre las ubicaciones ASIGNADAS del
  // artículo. El artículo se deriva de la línea de salida origen: cargamos la
  // salida y mapeamos lineaSalidaOrigenId → articuloId.
  const salidaOrigenId = useWatch({ control: form.control, name: 'salidaOrigenId' });
  const subAlmacenDestinoId = useWatch({
    control: form.control,
    name: 'subAlmacenDestinoId',
  });
  // ponytail: las 200 salidas registradas más recientes, filtradas por folio en cliente;
  // pasar a búsqueda server-side (`q`) si el volumen lo pide.
  const salidasQuery = useSalidas({ estado: EstadoMovimiento.Registrado, limit: 200 });
  const salidaOrigenQuery = useSalida(
    salidaOrigenId && /^[0-9a-f-]{36}$/i.test(salidaOrigenId) ? salidaOrigenId : undefined,
  );
  const articuloPorLineaSalida = new Map(
    (salidaOrigenQuery.data?.lineas ?? []).map((l) => [l.id, l.articuloId]),
  );
  // SalidaLineaItem no expone la variante: la medida (ancho/alto) distingue el vidrio.
  const esVidrioPorLineaSalida = new Map(
    (salidaOrigenQuery.data?.lineas ?? []).map((l) => [l.id, l.anchoMm != null || l.altoMm != null]),
  );
  const lineasWatch = useWatch({ control: form.control, name: 'lineas' });

  function onSubmit(values: AplicarDevolucionInternaValues) {
    aplicar.mutate(
      {
        command: {
          salidaOrigenId: values.salidaOrigenId,
          subAlmacenDestinoId: values.subAlmacenDestinoId,
          fechaMovimiento: values.fechaMovimiento,
          estadoMaterial: values.estadoMaterial,
          motivo: values.motivo,
          observaciones: values.observaciones ?? null,
          lineas: values.lineas.map((l) => ({
            lineaSalidaOrigenId: l.lineaSalidaOrigenId,
            cantidadADevolver: l.cantidadADevolver,
            ubicacionId: l.ubicacionId,
          })),
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Devolución ${resp.folio} aplicada`);
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
          toast.error('Error inesperado al aplicar la devolución.');
        },
      },
    );
  }

  return (
    <>
      <SheetBody>
        <form
          id="aplicar-devolucion-interna-form"
          onSubmit={form.handleSubmit(onSubmit)}
          noValidate
          className="space-y-4"
        >
          <FormSeccion numero={1} titulo="Datos de devolución">
            <FormCampo span={6}>
              <Field
                label="Salida origen"
                required
                error={form.formState.errors.salidaOrigenId?.message}
              >
                <Controller
                  name="salidaOrigenId"
                  control={form.control}
                  render={({ field }) => (
                    <CatalogoEagerCombobox
                      items={salidasQuery.data?.items ?? []}
                      loading={salidasQuery.isLoading}
                      error={salidasQuery.error}
                      value={field.value}
                      onChange={(id) => {
                        field.onChange(id ?? '');
                        // Las partidas elegidas eran de la salida anterior.
                        form.setValue(
                          'lineas',
                          form.getValues('lineas').map((l) => ({ ...l, lineaSalidaOrigenId: '' })),
                        );
                      }}
                      itemToLabel={(s) => s.folio}
                      renderItem={(s) => (
                        <div className="min-w-0 flex-1">
                          <p className="truncate font-mono text-xs">{s.folio}</p>
                          <p className="truncate text-xs text-ink-muted">{s.fechaMovimiento}</p>
                        </div>
                      )}
                      placeholder="Buscar salida por folio…"
                      searchPlaceholder="Folio de la salida…"
                      emptyListText="No hay salidas registradas."
                      ariaLabel="Seleccionar salida origen"
                    />
                  )}
                />
              </Field>
            </FormCampo>

            <FormCampo span={6}>
              <Field
                label="Sub-almacén destino"
                required
                error={form.formState.errors.subAlmacenDestinoId?.message}
              >
                <Controller
                  name="subAlmacenDestinoId"
                  control={form.control}
                  render={({ field }) => (
                    <Select
                      value={field.value || ''}
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
              <Field
                label="Fecha de movimiento"
                required
                error={form.formState.errors.fechaMovimiento?.message}
              >
                <Controller
                  name="fechaMovimiento"
                  control={form.control}
                  render={({ field }) => <Input {...field} type="date" value={field.value ?? ''} />}
                />
              </Field>
            </FormCampo>

            <FormCampo span={6}>
              <Field
                label="Estado del material"
                required
                error={form.formState.errors.estadoMaterial?.message}
              >
                <Controller
                  name="estadoMaterial"
                  control={form.control}
                  render={({ field }) => (
                    <Select value={field.value || ''} onValueChange={(v) => field.onChange(v)}>
                      <SelectTrigger>
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {ESTADOS_MATERIAL.map((e) => (
                          <SelectItem key={e.valor} value={e.valor}>
                            {e.texto}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  )}
                />
              </Field>
            </FormCampo>
          </FormSeccion>

          <Field label="Motivo" required error={form.formState.errors.motivo?.message}>
            <Controller
              name="motivo"
              control={form.control}
              render={({ field }) => (
                <TextAreaField
                  value={field.value ?? null}
                  onChange={(v) => field.onChange(v ?? '')}
                  maxLength={500}
                  minRows={2}
                />
              )}
            />
          </Field>

          <Field
            label="Observaciones (opcional)"
            error={form.formState.errors.observaciones?.message}
          >
            <Controller
              name="observaciones"
              control={form.control}
              render={({ field }) => (
                <TextAreaField
                  value={field.value ?? null}
                  onChange={(v) => field.onChange(v)}
                  maxLength={500}
                  minRows={2}
                />
              )}
            />
          </Field>

          <section className="space-y-2">
            <div className="flex items-center justify-between">
              <h3 className="text-sm font-semibold">
                Partidas a devolver ({lineasFA.fields.length})
              </h3>
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={() =>
                  lineasFA.append({
                    lineaSalidaOrigenId: '',
                    cantidadADevolver: 1,
                    ubicacionId: '',
                  })
                }
              >
                <Plus className="mr-2 size-3.5" />
                Agregar partida
              </Button>
            </div>

            <div className="space-y-2">
              {lineasFA.fields.map((field, index) => {
                const errs = form.formState.errors.lineas?.[index];
                const lineaOrigenId = lineasWatch?.[index]?.lineaSalidaOrigenId;
                const articuloId = lineaOrigenId
                  ? articuloPorLineaSalida.get(lineaOrigenId)
                  : undefined;
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
                        <Field
                          label="Partida de salida origen"
                          required
                          error={errs?.lineaSalidaOrigenId?.message}
                        >
                          <Controller
                            name={`lineas.${index}.lineaSalidaOrigenId` as const}
                            control={form.control}
                            render={({ field }) => (
                              <Select
                                value={field.value || ''}
                                onValueChange={(v) => field.onChange(v)}
                                disabled={!salidaOrigenQuery.data}
                              >
                                <SelectTrigger
                                  aria-label={`Partida de salida origen ${index + 1}`}
                                  onBlur={field.onBlur}
                                >
                                  <SelectValue
                                    placeholder={
                                      salidaOrigenQuery.data
                                        ? 'Selecciona la partida…'
                                        : 'Primero elige la salida origen'
                                    }
                                  />
                                </SelectTrigger>
                                <SelectContent>
                                  {salidaOrigenQuery.data?.lineas.map((l) => (
                                    <SelectItem key={l.id} value={l.id}>
                                      {`#${l.posicion} · ${l.articuloClave ?? ''} ${l.articuloDescripcion ?? 'Artículo sin descripción'} · ${l.cantidad} ${l.unidadMedida}`}
                                    </SelectItem>
                                  ))}
                                </SelectContent>
                              </Select>
                            )}
                          />
                        </Field>
                      </FormCampo>
                      <FormCampo span={6}>
                        <Field
                          label="Cantidad a devolver"
                          required
                          error={errs?.cantidadADevolver?.message}
                        >
                          <Controller
                            name={`lineas.${index}.cantidadADevolver` as const}
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
                              />
                            )}
                          />
                        </Field>
                      </FormCampo>
                      <FormCampo span={6}>
                        <Field
                          label="Ubicación (rack) destino"
                          required
                          error={errs?.ubicacionId?.message}
                        >
                          <Controller
                            name={`lineas.${index}.ubicacionId` as const}
                            control={form.control}
                            render={({ field }) => (
                              <UbicacionBinSelector
                                modo="asignacion"
                                articuloId={articuloId}
                                subAlmacenId={subAlmacenDestinoId}
                                incluirGeneral={!esVidrioPorLineaSalida.get(lineaOrigenId ?? '')}
                                value={field.value ?? null}
                                onChange={(id) => field.onChange(id ?? '')}
                                accionSinUbicaciones={
                                  articuloId ? (
                                    <AsignarUbicacionEnLinea
                                      articuloId={articuloId}
                                      onAsignada={(ubicacionId) =>
                                        form.setValue(`lineas.${index}.ubicacionId`, ubicacionId, {
                                          shouldDirty: true,
                                          shouldValidate: true,
                                        })
                                      }
                                    />
                                  ) : undefined
                                }
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
        <Button type="button" variant="ghost" onClick={onCancel} disabled={aplicar.isPending}>
          Cancelar
        </Button>
        <Button type="submit" form="aplicar-devolucion-interna-form" disabled={aplicar.isPending}>
          {aplicar.isPending ? 'Aplicando…' : 'Aplicar devolución'}
        </Button>
      </SheetFooter>
    </>
  );
}
