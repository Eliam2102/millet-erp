import { useEffect, useMemo } from 'react';
import {
  Controller,
  useFieldArray,
  useForm,
  useWatch,
} from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigate } from '@tanstack/react-router';
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
  AvisoUnidadNoResoluble,
  DECIMALES_FALLBACK,
  evaluarDecimalesFila,
  MENSAJE_DECIMALES_UNIDAD,
  OrdenCompraSelector,
  ProveedorSelector,
  stepParaDecimales,
  TextAreaField,
  useDecimalesUnidad,
} from '@/components/erp';
import { SubAlmacenSelector } from '@/components/erp/selectors/SubAlmacenSelector';
import { FacturaPicker } from '@/features/cxp/components/FacturaPicker';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  IniciarDevolucionAProveedorSchema,
  type IniciarDevolucionAProveedorValues,
} from '@/features/almacen/schemas/devolucion';
import { useIniciarDevolucionProveedor } from '@/features/almacen/api/useDevolucionesProveedor';
import { useRecepcion } from '@/features/almacen/api/useRecepciones';
import type { RecepcionLineaItem } from '@/features/almacen/api/types';
import { RecepcionPicker } from '@/features/almacen/components/RecepcionPicker';
import { Field } from '@/features/almacen/components/internal/Field';

export interface NuevaDevolucionProveedorSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * <c>&lt;NuevaDevolucionProveedorSheet/&gt;</c> — slide-from-right
 * para iniciar una devolución a proveedor (8.B, FE-F4-PR1). Crea el
 * registro en <c>Borrador</c>; las evidencias y el flujo de
 * autorización se manejan en el detalle.
 *
 * <para>Las referencias cruzadas usan selectores reales (no GUIDs
 * crudos): recepción origen vía <c>RecepcionPicker</c> (filtrada por la
 * OC elegida), factura CxP vía <c>FacturaPicker</c> (filtrada por
 * proveedor) y sub-almacén vía <c>SubAlmacenSelector</c>. Elegir una
 * recepción prellena sub-almacén/OC vacíos, y elegir una línea de la
 * recepción prellena artículo, cantidad, UM y costo de esa línea.</para>
 */
export function NuevaDevolucionProveedorSheet({
  open,
  onOpenChange,
}: NuevaDevolucionProveedorSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-3xl">
        <SheetHeader>
          <SheetTitle>Nueva devolución a proveedor</SheetTitle>
          <SheetDescription>
            Crea el registro en estado Borrador. Después adjunta
            evidencias y solicita autorización a Dirección.
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
  const navigate = useNavigate();
  const idempotencyKey = useFormIdempotencyKey();
  const iniciar = useIniciarDevolucionProveedor();

  const form = useForm<IniciarDevolucionAProveedorValues>({
    resolver: zodResolver(IniciarDevolucionAProveedorSchema),
    defaultValues: useMemo(
      () => ({
        proveedorId: '',
        motivo: '',
        recepcionOrigenId: null,
        facturaProveedorOrigenId: null,
        ordenCompraOrigenId: null,
        subAlmacenOrigenId: null,
        lineas: [
          {
            articuloId: '',
            cantidad: 1,
            unidadMedida: 'PZA',
            costoUnitarioMxn: 0,
            lineaRecepcionOrigenId: null,
          },
        ],
      }),
      [],
    ),
  });

  const lineasFA = useFieldArray({ control: form.control, name: 'lineas' });

  // Para filtrar OCs y facturas CxP por proveedor seleccionado.
  const proveedorIdWatched = useWatch({
    control: form.control,
    name: 'proveedorId',
  });
  // Para filtrar recepciones por la OC seleccionada.
  const ordenCompraIdWatched = useWatch({
    control: form.control,
    name: 'ordenCompraOrigenId',
  });
  const recepcionIdWatched = useWatch({
    control: form.control,
    name: 'recepcionOrigenId',
  });

  // Detalle de la recepción origen — alimenta el select de líneas y el
  // prellenado de sub-almacén/OC.
  const recepcionQuery = useRecepcion(recepcionIdWatched ?? undefined);

  // Al resolver la recepción elegida, prellena sub-almacén y OC si el
  // usuario no los ha capturado (la recepción es la fuente natural).
  useEffect(() => {
    const det = recepcionQuery.data;
    if (!det || det.id !== recepcionIdWatched) return;
    if (!form.getValues('subAlmacenOrigenId')) {
      form.setValue('subAlmacenOrigenId', det.subAlmacenId, {
        shouldDirty: true,
      });
    }
    if (!form.getValues('ordenCompraOrigenId') && det.ordenCompraId) {
      form.setValue('ordenCompraOrigenId', det.ordenCompraId, {
        shouldDirty: true,
      });
    }
  }, [recepcionQuery.data, recepcionIdWatched, form]);

  function handleRecepcionChange(id: string | null) {
    form.setValue('recepcionOrigenId', id, { shouldDirty: true });
    // Las refs de línea apuntaban a la recepción anterior — limpiarlas.
    form.getValues('lineas').forEach((_, i) => {
      form.setValue(`lineas.${i}.lineaRecepcionOrigenId`, null);
    });
  }

  function handleSelectLineaRecepcion(index: number, lineaId: string | null) {
    form.setValue(`lineas.${index}.lineaRecepcionOrigenId`, lineaId, {
      shouldDirty: true,
    });
    if (!lineaId) return;
    const linea = recepcionQuery.data?.lineas.find((l) => l.id === lineaId);
    if (!linea) return;
    const opts = { shouldDirty: true, shouldValidate: true } as const;
    form.setValue(`lineas.${index}.articuloId`, linea.articuloId, opts);
    form.setValue(`lineas.${index}.cantidad`, linea.cantidad, opts);
    form.setValue(`lineas.${index}.unidadMedida`, linea.unidadMedida, opts);
    form.setValue(
      `lineas.${index}.costoUnitarioMxn`,
      linea.costoUnitarioMxn,
      opts,
    );
  }

  const lookup = useDecimalesUnidad();

  function onSubmit(values: IniciarDevolucionAProveedorValues) {
    // Advisory: decimales por unidad (string→código; backend autoritativo).
    let decimalesMal = false;
    values.lineas.forEach((l, i) => {
      // Solo 'invalidos' bloquea; 'no-resoluble' avisa (sin bloquear) en la línea.
      if (
        evaluarDecimalesFila(lookup, {
          unidadMedida: l.unidadMedida,
          cantidad: l.cantidad,
        }) === 'invalidos'
      ) {
        form.setError(
          `lineas.${i}.cantidad` as Parameters<typeof form.setError>[0],
          { type: 'decimales', message: MENSAJE_DECIMALES_UNIDAD },
        );
        decimalesMal = true;
      }
    });
    if (decimalesMal) return;
    iniciar.mutate(
      {
        command: {
          proveedorId: values.proveedorId,
          motivo: values.motivo,
          recepcionOrigenId: values.recepcionOrigenId ?? null,
          facturaProveedorOrigenId: values.facturaProveedorOrigenId ?? null,
          ordenCompraOrigenId: values.ordenCompraOrigenId ?? null,
          subAlmacenOrigenId: values.subAlmacenOrigenId ?? null,
          lineas: values.lineas.map((l) => ({
            articuloId: l.articuloId,
            cantidad: l.cantidad,
            unidadMedida: l.unidadMedida,
            costoUnitarioMxn: l.costoUnitarioMxn,
            lineaRecepcionOrigenId: l.lineaRecepcionOrigenId ?? null,
          })),
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success('Devolución a proveedor creada', {
            description:
              'Está en Borrador. Adjunta evidencias y solicita autorización.',
          });
          onSuccess();
          navigate({
            to: '/almacen/devoluciones/proveedor/$id',
            params: { id: resp.devolucionId },
          });
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
          toast.error('Error inesperado al iniciar la devolución.');
        },
      },
    );
  }

  return (
    <>
      <form
        id="nueva-devolucion-proveedor-form"
        onSubmit={form.handleSubmit(onSubmit)}
        noValidate
        className="flex-1 space-y-4 overflow-y-auto px-6"
      >
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
          <Field
            label="Proveedor"
            required
            error={form.formState.errors.proveedorId?.message}
          >
            <Controller
              name="proveedorId"
              control={form.control}
              render={({ field }) => (
                <ProveedorSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? '')}
                />
              )}
            />
          </Field>

          <Field
            label="Sub-almacén origen (opcional)"
            error={form.formState.errors.subAlmacenOrigenId?.message}
          >
            <Controller
              name="subAlmacenOrigenId"
              control={form.control}
              render={({ field }) => (
                <SubAlmacenSelector
                  value={field.value ?? null}
                  onChange={(id) => field.onChange(id ?? null)}
                />
              )}
            />
          </Field>

          <Field
            label="OC origen (opcional)"
            error={form.formState.errors.ordenCompraOrigenId?.message}
          >
            <Controller
              name="ordenCompraOrigenId"
              control={form.control}
              render={({ field }) => (
                <OrdenCompraSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? null)}
                  proveedorId={proveedorIdWatched ?? undefined}
                />
              )}
            />
          </Field>

          <Field
            label="Recepción origen (opcional)"
            error={form.formState.errors.recepcionOrigenId?.message}
          >
            <Controller
              name="recepcionOrigenId"
              control={form.control}
              render={({ field }) => (
                <RecepcionPicker
                  value={field.value ?? null}
                  onChange={handleRecepcionChange}
                  ordenCompraId={ordenCompraIdWatched ?? null}
                />
              )}
            />
          </Field>

          <Field
            label="Factura proveedor origen (opcional)"
            error={form.formState.errors.facturaProveedorOrigenId?.message}
          >
            <Controller
              name="facturaProveedorOrigenId"
              control={form.control}
              render={({ field }) => (
                <FacturaPicker
                  value={field.value ?? null}
                  onChange={(id) => field.onChange(id ?? null)}
                  proveedorId={proveedorIdWatched || null}
                />
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

        <section className="space-y-2">
          <div className="flex items-center justify-between">
            <h3 className="text-sm font-semibold">
              Líneas ({lineasFA.fields.length})
            </h3>
            <Button
              type="button"
              size="sm"
              variant="outline"
              onClick={() =>
                lineasFA.append({
                  articuloId: '',
                  cantidad: 1,
                  unidadMedida: 'PZA',
                  costoUnitarioMxn: 0,
                  lineaRecepcionOrigenId: null,
                })
              }
            >
              <Plus className="mr-2 h-4 w-4" />
              Agregar línea
            </Button>
          </div>

          {form.formState.errors.lineas &&
            !Array.isArray(form.formState.errors.lineas) && (
              <p role="alert" className="text-xs text-rose-600">
                {form.formState.errors.lineas.message}
              </p>
            )}

          <div className="space-y-2">
            {lineasFA.fields.map((field, index) => (
              <LineaForm
                key={field.id}
                index={index}
                control={form.control}
                errors={
                  form.formState.errors.lineas?.[index] as
                    | LineaErrors
                    | undefined
                }
                lineasRecepcion={
                  recepcionIdWatched ? recepcionQuery.data?.lineas : undefined
                }
                onSelectLineaRecepcion={(lineaId) =>
                  handleSelectLineaRecepcion(index, lineaId)
                }
                onRemove={() => lineasFA.remove(index)}
              />
            ))}
          </div>
        </section>
      </form>

      <SheetFooter>
        <Button
          type="button"
          variant="ghost"
          onClick={onCancel}
          disabled={iniciar.isPending}
        >
          Cancelar
        </Button>
        <Button
          type="submit"
          form="nueva-devolucion-proveedor-form"
          disabled={iniciar.isPending}
        >
          {iniciar.isPending ? 'Creando…' : 'Crear devolución'}
        </Button>
      </SheetFooter>
    </>
  );
}

interface LineaErrors {
  articuloId?: { message?: string };
  cantidad?: { message?: string };
  unidadMedida?: { message?: string };
  costoUnitarioMxn?: { message?: string };
  lineaRecepcionOrigenId?: { message?: string };
}

/** Sentinel para "sin línea origen" — shadcn Select no acepta value="". */
const SIN_LINEA = '__ninguna__';

function etiquetaLinea(l: RecepcionLineaItem): string {
  const articulo = l.articuloClave ?? `${l.articuloId.slice(0, 8)}…`;
  const desc = l.articuloDescripcion ? ` · ${l.articuloDescripcion}` : '';
  return `#${l.posicion} · ${articulo}${desc} · ${l.cantidad} ${l.unidadMedida}`;
}

function LineaForm({
  index,
  control,
  errors,
  lineasRecepcion,
  onSelectLineaRecepcion,
  onRemove,
}: {
  index: number;
  control: ReturnType<
    typeof useForm<IniciarDevolucionAProveedorValues>
  >['control'];
  errors: LineaErrors | undefined;
  /** Líneas de la recepción origen elegida; undefined = sin recepción. */
  lineasRecepcion: RecepcionLineaItem[] | undefined;
  onSelectLineaRecepcion: (lineaId: string | null) => void;
  onRemove: () => void;
}) {
  const lookupLinea = useDecimalesUnidad();
  const umCodigo = useWatch({
    control,
    name: `lineas.${index}.unidadMedida` as const,
  });
  const decResuelto = lookupLinea.porCodigo(umCodigo);
  const decimalesLinea = decResuelto ?? DECIMALES_FALLBACK;
  const unidadNoResoluble = decResuelto == null;
  return (
    <div className="rounded-md border border-dashed border-primary/40 bg-primary/5 p-3 space-y-2">
      <div className="flex items-start justify-between gap-2">
        <span className="text-xs font-semibold text-muted-foreground">
          Línea #{index + 1}
        </span>
        <Button
          type="button"
          size="sm"
          variant="ghost"
          onClick={onRemove}
          aria-label={`Quitar línea ${index + 1}`}
        >
          <Trash2 className="h-4 w-4" />
        </Button>
      </div>

      <div className="grid grid-cols-1 gap-2 md:grid-cols-2">
        <div className="md:col-span-2">
          <Field
            label="Línea de recepción origen (opcional)"
            error={errors?.lineaRecepcionOrigenId?.message}
          >
            <Controller
              name={`lineas.${index}.lineaRecepcionOrigenId` as const}
              control={control}
              render={({ field }) => (
                <Select
                  value={field.value ?? SIN_LINEA}
                  onValueChange={(v) =>
                    onSelectLineaRecepcion(v === SIN_LINEA ? null : v)
                  }
                  disabled={lineasRecepcion == null}
                >
                  <SelectTrigger>
                    <SelectValue
                      placeholder={
                        lineasRecepcion == null
                          ? 'Selecciona primero la recepción origen'
                          : 'Selecciona la línea recibida'
                      }
                    />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={SIN_LINEA}>Sin línea origen</SelectItem>
                    {(lineasRecepcion ?? []).map((l) => (
                      <SelectItem key={l.id} value={l.id}>
                        {etiquetaLinea(l)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
          </Field>
        </div>

        <Field label="Artículo" required error={errors?.articuloId?.message}>
          <Controller
            name={`lineas.${index}.articuloId` as const}
            control={control}
            render={({ field }) => (
              <ArticuloSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
              />
            )}
          />
        </Field>

        <Field label="Cantidad" required error={errors?.cantidad?.message}>
          <Controller
            name={`lineas.${index}.cantidad` as const}
            control={control}
            render={({ field }) => (
              <Input
                type="number"
                inputMode="decimal"
                step={stepParaDecimales(decimalesLinea)}
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
          <AvisoUnidadNoResoluble visible={unidadNoResoluble} />
        </Field>

        <Field
          label="UM"
          required
          error={errors?.unidadMedida?.message}
        >
          <Controller
            name={`lineas.${index}.unidadMedida` as const}
            control={control}
            render={({ field }) => (
              <Input
                {...field}
                value={field.value ?? ''}
                maxLength={20}
                placeholder="PZA / KG / L…"
              />
            )}
          />
        </Field>

        <Field
          label="Costo unitario MXN"
          required
          error={errors?.costoUnitarioMxn?.message}
        >
          <Controller
            name={`lineas.${index}.costoUnitarioMxn` as const}
            control={control}
            render={({ field }) => (
              <Input
                type="number"
                inputMode="decimal"
                step="0.01"
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
      </div>
    </div>
  );
}
