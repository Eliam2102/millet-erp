import { useEffect } from 'react';
import { Controller, useFieldArray, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Plus, Trash2 } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { SucursalSelector } from '@/components/erp';
import { ClienteSelector } from '@/features/facturacion/components/selectors/ClienteSelector';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  PedidoManualSchema,
  EditarPedidoSchema,
  type PedidoManualValues,
} from '@/features/facturacion/schemas/pedido-manual';
import {
  useCrearPedidoManual,
  useEditarPedido,
} from '@/features/facturacion/api/usePedidos';
import {
  ComportamientoFiscal,
  type PedidoFacturableDetalleResponse,
} from '@/features/facturacion/api/types';
import { OPCIONES_COMPORTAMIENTO_FISCAL } from '@/features/facturacion/lib/glosario';
import { CanalVentaSelector } from '@/features/facturacion/components/selectors/CanalVentaSelector';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;NuevoPedidoManual/&gt;</c> — form de captura de pedido manual
 * (FE-F1-PR1). Cabecera + líneas inline (<c>useFieldArray</c>, NUNCA
 * modal — memoria feedback_inline_no_modal_para_items). POST con
 * Idempotency-Key.
 *
 * <para>FAC-UX-PR4: el cliente se elige con
 * <c>&lt;ClienteSelector/&gt;</c> del maestro de clientes (cierra
 * PLATFORM-TODO(&lt;ClienteSelector&gt;) en este form) — al seleccionar
 * se autollenan id + razón social; el nombre queda editable.</para>
 *
 * <para>PLATFORM-TODO(&lt;ProductoSelector&gt;): el producto por línea
 * es opcional y se captura como GUID; las claves SAT van a mano. Cuando
 * el maestro de productos exponga claves/tasas, prellenar desde ahí.</para>
 */
export interface NuevoPedidoManualProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange?: (dirty: boolean) => void;
  /** Si se provee, el form entra en modo EDICIÓN (PUT con If-Match).
   * Sucursal y número de pedido son inmutables y no se muestran. */
  pedido?: PedidoFacturableDetalleResponse;
}

function defaultLinea(): PedidoManualValues['lineas'][number] {
  return {
    productoId: null,
    productoDescripcion: '',
    claveProdServSat: null,
    claveUnidadSat: null,
    cantidad: 1,
    precio: 0,
    descuento: 0,
    requierePedimento: false,
  };
}

const VALORES_INICIALES: PedidoManualValues = {
  numeroPedido: null,
  sucursalId: '',
  clienteId: '',
  clienteNombre: '',
  // Canal id 1 (seed histórico "Tienda Cancún"); si ya no existe en el
  // catálogo, <CanalVentaSelector/> lo normaliza al primer canal activo.
  canalVenta: 1,
  comportamientoFiscal: ComportamientoFiscal.MostradorInmediato,
  moneda: 'MXN',
  obraId: null,
  obraNombre: null,
  comentarios: null,
  lineas: [defaultLinea()],
};

/** '' → null para los campos opcionales antes de mandar al backend. */
function nullIfEmpty(v: string | null | undefined): string | null {
  const s = (v ?? '').trim();
  return s.length > 0 ? s : null;
}

/** Valores iniciales del form en modo EDICIÓN, desde el detalle del pedido.
 * El canal viene como id del catálogo (`canalVentaId`, FAC-ING-PR3); el
 * comportamiento sigue llegando como nombre de enum → número. */
function valoresDesdePedido(
  p: PedidoFacturableDetalleResponse,
): PedidoManualValues {
  return {
    numeroPedido: p.numeroPedido,
    sucursalId: '',
    clienteId: p.clienteId,
    clienteNombre: p.clienteNombre,
    canalVenta: p.canalVentaId,
    comportamientoFiscal:
      ComportamientoFiscal[
        p.comportamientoFiscal as keyof typeof ComportamientoFiscal
      ] ?? VALORES_INICIALES.comportamientoFiscal,
    moneda: p.moneda,
    obraId: p.obraId,
    obraNombre: p.obraNombre,
    comentarios: p.comentarios,
    lineas: p.lineas.map((l) => ({
      productoId: l.productoId,
      productoDescripcion: l.productoDescripcion,
      claveProdServSat: l.claveProdServSat,
      claveUnidadSat: l.claveUnidadSat,
      cantidad: l.cantidad,
      precio: l.precio,
      descuento: l.descuento,
      requierePedimento: l.requierePedimento,
    })),
  };
}

export function NuevoPedidoManual({
  onClose,
  onDirtyChange,
  pedido,
}: NuevoPedidoManualProps) {
  const esEdicion = pedido != null;
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearPedidoManual();
  const editar = useEditarPedido();

  const form = useForm<PedidoManualValues>({
    // En edición se omiten sucursal/numeroPedido (inmutables) del schema.
    resolver: zodResolver(
      (esEdicion ? EditarPedidoSchema : PedidoManualSchema) as typeof PedidoManualSchema,
    ),
    defaultValues: esEdicion ? valoresDesdePedido(pedido) : VALORES_INICIALES,
  });

  const { fields, append, remove } = useFieldArray({
    control: form.control,
    name: 'lineas',
  });

  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange?.(isDirty);
  }, [isDirty, onDirtyChange]);

  // Total estimado en vivo (cantidad·precio − descuento por línea).
  // useWatch (no form.watch) — API memoization-safe para el React Compiler.
  const lineasWatch = useWatch({ control: form.control, name: 'lineas' });
  const monedaWatch = useWatch({ control: form.control, name: 'moneda' });
  const totalEstimado = (lineasWatch ?? []).reduce(
    (acc, l) =>
      acc + Math.max(0, (l.cantidad ?? 0) * (l.precio ?? 0) - (l.descuento ?? 0)),
    0,
  );

  const isPending = crear.isPending || editar.isPending;

  function onError(error: Error) {
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
    toast.error(
      esEdicion
        ? 'Error inesperado al guardar el pedido.'
        : 'Error inesperado al crear el pedido.',
    );
  }

  function onSubmit(values: PedidoManualValues) {
    const lineas = values.lineas.map((l) => ({
      productoId: nullIfEmpty(l.productoId),
      productoDescripcion: l.productoDescripcion,
      claveProdServSat: nullIfEmpty(l.claveProdServSat),
      claveUnidadSat: nullIfEmpty(l.claveUnidadSat),
      cantidad: l.cantidad,
      precio: l.precio,
      descuento: l.descuento,
      requierePedimento: l.requierePedimento,
    }));

    if (esEdicion && pedido != null) {
      editar.mutate(
        {
          id: pedido.id,
          version: pedido.version,
          command: {
            clienteId: values.clienteId,
            clienteNombre: values.clienteNombre,
            canalVenta: values.canalVenta,
            comportamientoFiscal: values.comportamientoFiscal,
            moneda: values.moneda.toUpperCase(),
            obraId: values.obraId,
            obraNombre: nullIfEmpty(values.obraNombre),
            comentarios: nullIfEmpty(values.comentarios),
            lineas,
          },
        },
        {
          onSuccess: (res) => {
            toast.success(`Pedido actualizado · total ${res.total.toFixed(2)}`);
            onClose({ force: true });
          },
          onError,
        },
      );
      return;
    }

    crear.mutate(
      {
        command: {
          numeroPedido: nullIfEmpty(values.numeroPedido),
          sucursalId: values.sucursalId,
          clienteId: values.clienteId,
          clienteNombre: values.clienteNombre,
          canalVenta: values.canalVenta,
          comportamientoFiscal: values.comportamientoFiscal,
          moneda: values.moneda.toUpperCase(),
          obraId: values.obraId,
          obraNombre: nullIfEmpty(values.obraNombre),
          comentarios: nullIfEmpty(values.comentarios),
          lineas,
        },
        idempotencyKey,
      },
      {
        onSuccess: (res) => {
          toast.success(
            `Pedido creado (${res.estado}) · total ${res.total.toFixed(2)}`,
          );
          onClose({ force: true });
        },
        onError,
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} noValidate className="space-y-5">
      {esEdicion && (
        <p className="rounded-md border border-amber-300 bg-amber-50/60 px-3 py-2 text-sm">
          Editando un pedido. La sucursal y el número de pedido son inmutables.
        </p>
      )}
      {/* ── Cabecera ─────────────────────────────────────────────── */}
      <section className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        {!esEdicion && (
          <Campo
            label="Sucursal"
            required
            error={form.formState.errors.sucursalId?.message}
          >
            <Controller
              name="sucursalId"
              control={form.control}
              render={({ field }) => (
                <SucursalSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? '')}
                />
              )}
            />
          </Campo>
        )}

        {!esEdicion && (
          <Campo
            label="Número de pedido"
            error={form.formState.errors.numeroPedido?.message}
          >
            <Input
              placeholder="Opcional"
              maxLength={50}
              {...form.register('numeroPedido')}
            />
          </Campo>
        )}

        <Campo
          label="Cliente"
          required
          error={form.formState.errors.clienteId?.message}
        >
          <Controller
            name="clienteId"
            control={form.control}
            render={({ field }) => (
              <ClienteSelector
                value={field.value || null}
                initialLabel={form.getValues('clienteNombre') || undefined}
                onChange={(item) => {
                  field.onChange(item?.id ?? '');
                  if (item != null) {
                    form.setValue('clienteNombre', item.razonSocial, {
                      shouldDirty: true,
                      shouldValidate: true,
                    });
                  }
                }}
              />
            )}
          />
        </Campo>

        <Campo
          label="Nombre / razón social del cliente"
          required
          error={form.formState.errors.clienteNombre?.message}
        >
          <Input maxLength={254} {...form.register('clienteNombre')} />
        </Campo>

        <Campo
          label="Canal de venta"
          required
          error={form.formState.errors.canalVenta?.message}
        >
          <Controller
            name="canalVenta"
            control={form.control}
            render={({ field }) => (
              <CanalVentaSelector
                value={field.value ?? null}
                onChange={field.onChange}
                etiquetaValorActual={esEdicion ? pedido?.canalVenta : undefined}
              />
            )}
          />
        </Campo>

        <Campo
          label="Comportamiento fiscal"
          required
          error={form.formState.errors.comportamientoFiscal?.message}
        >
          <select
            className="h-9 w-full rounded-md border border-input bg-transparent px-3 text-sm shadow-sm"
            {...form.register('comportamientoFiscal', { valueAsNumber: true })}
          >
            {OPCIONES_COMPORTAMIENTO_FISCAL.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </select>
        </Campo>

        <Campo
          label="Moneda"
          required
          error={form.formState.errors.moneda?.message}
        >
          <Input
            maxLength={3}
            className="uppercase"
            placeholder="MXN"
            {...form.register('moneda')}
          />
        </Campo>

        <Campo
          label="Obra (nombre)"
          error={form.formState.errors.obraNombre?.message}
        >
          <Input placeholder="Opcional" {...form.register('obraNombre')} />
        </Campo>

        <div className="sm:col-span-2">
          <Campo
            label="Comentarios del pedido"
            error={form.formState.errors.comentarios?.message}
            hint="No se incluyen en el CFDI; son notas internas del origen."
          >
            <Input
              placeholder="Opcional"
              maxLength={1000}
              {...form.register('comentarios')}
            />
          </Campo>
        </div>
      </section>

      {/* ── Líneas (inline) ──────────────────────────────────────── */}
      <section className="space-y-2">
        <header className="flex items-center justify-between">
          <h3 className="text-sm font-medium">Líneas ({fields.length})</h3>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => append(defaultLinea())}
          >
            <Plus className="mr-1 h-3 w-3" />
            Agregar línea
          </Button>
        </header>

        {fields.length === 0 && (
          <p className="rounded-md border border-dashed px-3 py-4 text-center text-xs text-muted-foreground">
            Sin líneas. Agrega al menos una.
          </p>
        )}

        <div className="space-y-2">
          {fields.map((field, index) => (
            <LineaInline
              key={field.id}
              index={index}
              form={form}
              onRemove={() => remove(index)}
              puedeQuitar={fields.length > 1}
            />
          ))}
        </div>

        {form.formState.errors.lineas?.message && (
          <p className="text-xs text-destructive">
            {form.formState.errors.lineas.message}
          </p>
        )}
      </section>

      <div className="flex items-center justify-between border-t pt-4">
        <p className="text-sm">
          Total estimado:{' '}
          <span className="font-semibold tabular-nums">
            {totalEstimado.toFixed(2)} {monedaWatch?.toUpperCase()}
          </span>
        </p>
        <div className="flex items-center gap-2">
          <Button
            type="button"
            variant="ghost"
            onClick={() => onClose()}
            disabled={isPending}
          >
            Cancelar
          </Button>
          <Button type="submit" disabled={isPending}>
            {esEdicion
              ? isPending
                ? 'Guardando…'
                : 'Guardar cambios'
              : isPending
                ? 'Creando…'
                : 'Crear pedido'}
          </Button>
        </div>
      </div>
    </form>
  );
}

interface LineaInlineProps {
  index: number;
  form: ReturnType<typeof useForm<PedidoManualValues>>;
  onRemove: () => void;
  puedeQuitar: boolean;
}

function LineaInline({ index, form, onRemove, puedeQuitar }: LineaInlineProps) {
  const errs = form.formState.errors.lineas?.[index];
  return (
    <div
      className={cn(
        'space-y-2 rounded-md border border-dashed border-primary/40 bg-primary/5 p-3',
        errs && 'border-destructive/50',
      )}
    >
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-12">
        <div className="sm:col-span-5">
          <Label className="text-xs">Producto / servicio *</Label>
          <Input
            placeholder="Descripción"
            {...form.register(`lineas.${index}.productoDescripcion` as const)}
          />
          {errs?.productoDescripcion && (
            <p className="text-xs text-destructive">
              {errs.productoDescripcion.message}
            </p>
          )}
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">Clave SAT</Label>
          <Input
            placeholder="01010101"
            maxLength={10}
            {...form.register(`lineas.${index}.claveProdServSat` as const)}
          />
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">Unidad SAT</Label>
          <Input
            placeholder="H87"
            maxLength={10}
            {...form.register(`lineas.${index}.claveUnidadSat` as const)}
          />
        </div>
        <div className="sm:col-span-1">
          <Label className="text-xs">Cant. *</Label>
          <Input
            type="number"
            step="0.0001"
            min="0"
            {...form.register(`lineas.${index}.cantidad` as const, {
              valueAsNumber: true,
            })}
          />
          {errs?.cantidad && (
            <p className="text-xs text-destructive">{errs.cantidad.message}</p>
          )}
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">Precio *</Label>
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register(`lineas.${index}.precio` as const, {
              valueAsNumber: true,
            })}
          />
          {errs?.precio && (
            <p className="text-xs text-destructive">{errs.precio.message}</p>
          )}
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">Descuento</Label>
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register(`lineas.${index}.descuento` as const, {
              valueAsNumber: true,
            })}
          />
        </div>
        <div className="sm:col-span-3">
          <Label className="text-xs">Producto (GUID)</Label>
          <Input
            placeholder="Opcional"
            className="font-mono text-xs"
            {...form.register(`lineas.${index}.productoId` as const)}
          />
        </div>
        <div className="flex items-end gap-3 sm:col-span-5">
          <label className="flex items-center gap-1.5 text-xs">
            <input
              type="checkbox"
              className="size-4 rounded border-input"
              {...form.register(`lineas.${index}.requierePedimento` as const)}
            />
            Requiere pedimento
          </label>
        </div>
        <div className="flex items-end justify-end sm:col-span-4">
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={onRemove}
            disabled={!puedeQuitar}
            className="text-destructive hover:bg-destructive/10"
          >
            <Trash2 className="mr-1 h-3 w-3" />
            Quitar
          </Button>
        </div>
      </div>
    </div>
  );
}

interface CampoProps {
  label: string;
  required?: boolean;
  error?: string;
  hint?: string;
  children: React.ReactNode;
}

function Campo({ label, required, error, hint, children }: CampoProps) {
  return (
    <div className="space-y-1">
      <Label className="text-xs">
        {label}
        {required && <span className="ml-1 text-destructive">*</span>}
      </Label>
      {children}
      {hint != null && (
        <p className="text-[11px] leading-tight text-muted-foreground">{hint}</p>
      )}
      {error != null && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
}
