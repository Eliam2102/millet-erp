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
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  AplicarDevolucionInternaSchema,
  type AplicarDevolucionInternaValues,
} from '@/features/almacen/schemas/devolucion';
import {
  useAplicarDevolucionInterna,
  useSubAlmacenes,
} from '@/features/almacen/api';
import { Field } from '@/features/almacen/components/internal/Field';
import { UbicacionBinSelector } from '@/components/erp/selectors/UbicacionBinSelector';
import { useSalida } from '@/features/almacen/api/useSalidas';

export interface AplicarDevolucionInternaSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

const ESTADOS_MATERIAL = [
  'Sobrante',
  'Sin uso',
  'Defectuoso',
  'Para revisión',
  'Mal entregado',
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
      <SheetContent side="right" className="sm:max-w-2xl">
        <SheetHeader>
          <SheetTitle>Devolución interna (8.A)</SheetTitle>
          <SheetDescription>
            Devuelve material desde el solicitante al almacén
            (MAT-REV, sub-almacén destino, o regreso al origen). El
            backend restituye al costo de la salida origen.
          </SheetDescription>
        </SheetHeader>

        {open && (
          <FormBody
            onSuccess={() => onOpenChange(false)}
            onCancel={() => onOpenChange(false)}
          />
        )}
      </SheetContent>
    </Sheet>
  );
}

function FormBody({
  onSuccess,
  onCancel,
}: {
  onSuccess: () => void;
  onCancel: () => void;
}) {
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
      estadoMaterial: 'Sobrante',
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
  const salidaOrigenQuery = useSalida(
    salidaOrigenId && /^[0-9a-f-]{36}$/i.test(salidaOrigenId)
      ? salidaOrigenId
      : undefined,
  );
  const articuloPorLineaSalida = new Map(
    (salidaOrigenQuery.data?.lineas ?? []).map((l) => [l.id, l.articuloId]),
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
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                error,
              )
            ) {
              return;
            }
            toast.error(error.problem.title, {
              description: error.traceId ? `Código: ${error.traceId}` : undefined,
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
      <form
        id="aplicar-devolucion-interna-form"
        onSubmit={form.handleSubmit(onSubmit)}
        noValidate
        className="flex-1 space-y-4 overflow-y-auto px-6"
      >
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
          <Field
            label="Salida origen"
            required
            error={form.formState.errors.salidaOrigenId?.message}
          >
            <Controller
              name="salidaOrigenId"
              control={form.control}
              render={({ field }) => (
                <Input
                  {...field}
                  value={field.value ?? ''}
                  placeholder="GUID de la salida"
                  spellCheck={false}
                />
              )}
            />
          </Field>

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

          <Field
            label="Fecha de movimiento"
            required
            error={form.formState.errors.fechaMovimiento?.message}
          >
            <Controller
              name="fechaMovimiento"
              control={form.control}
              render={({ field }) => (
                <Input {...field} type="date" value={field.value ?? ''} />
              )}
            />
          </Field>

          <Field
            label="Estado del material"
            required
            error={form.formState.errors.estadoMaterial?.message}
          >
            <Controller
              name="estadoMaterial"
              control={form.control}
              render={({ field }) => (
                <Select
                  value={field.value || ''}
                  onValueChange={(v) => field.onChange(v)}
                >
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {ESTADOS_MATERIAL.map((e) => (
                      <SelectItem key={e} value={e}>
                        {e}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
          </Field>
        </div>

        <Field
          label="Motivo"
          required
          error={form.formState.errors.motivo?.message}
        >
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
              Líneas a devolver ({lineasFA.fields.length})
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
              <Plus className="mr-2 h-4 w-4" />
              Agregar línea
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
                  className="rounded-md border border-dashed border-primary/40 bg-primary/5 p-3 space-y-2"
                >
                  <div className="flex items-start justify-between gap-2">
                    <span className="text-xs font-semibold text-muted-foreground">
                      Línea #{index + 1}
                    </span>
                    <Button
                      type="button"
                      size="sm"
                      variant="ghost"
                      onClick={() => lineasFA.remove(index)}
                      aria-label={`Quitar línea ${index + 1}`}
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                  <div className="grid grid-cols-1 gap-2 md:grid-cols-2">
                    <Field
                      label="Línea de salida origen"
                      required
                      error={errs?.lineaSalidaOrigenId?.message}
                    >
                      <Controller
                        name={`lineas.${index}.lineaSalidaOrigenId` as const}
                        control={form.control}
                        render={({ field }) => (
                          <Input
                            {...field}
                            value={field.value ?? ''}
                            placeholder="GUID de la línea de salida"
                            spellCheck={false}
                          />
                        )}
                      />
                    </Field>
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
                                e.target.value === ''
                                  ? ''
                                  : Number(e.target.value),
                              )
                            }
                          />
                        )}
                      />
                    </Field>
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
                            value={field.value ?? null}
                            onChange={(id) => field.onChange(id ?? '')}
                          />
                        )}
                      />
                    </Field>
                  </div>
                </div>
              );
            })}
          </div>
        </section>
      </form>

      <SheetFooter>
        <Button
          type="button"
          variant="ghost"
          onClick={onCancel}
          disabled={aplicar.isPending}
        >
          Cancelar
        </Button>
        <Button
          type="submit"
          form="aplicar-devolucion-interna-form"
          disabled={aplicar.isPending}
        >
          {aplicar.isPending ? 'Aplicando…' : 'Aplicar devolución'}
        </Button>
      </SheetFooter>
    </>
  );
}
