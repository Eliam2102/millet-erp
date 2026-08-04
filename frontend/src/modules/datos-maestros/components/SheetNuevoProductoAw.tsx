import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { useNavigate } from '@tanstack/react-router';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  NuevoProductoAwContext,
  type NuevoProductoAwApi,
} from '@/modules/datos-maestros/components/nuevo-producto-aw-context';
import {
  CrearProductoAwSchema,
  type CrearProductoAwValues,
} from '@/modules/datos-maestros/schemas/producto-aw';
import { useCrearProductoAw } from '@/modules/datos-maestros/api';
import { CategoriaSelector } from '@/components/erp/selectors/CategoriaSelector';
import { UnidadMedidaSelect } from '@/modules/catalogos/components/UnidadMedidaSelect';

/**
 * <c>&lt;NuevoProductoAwProvider/&gt;</c> + Sheet asociado (P4 del
 * patrón cross-módulo, ADR-0048). Alta manual de producto de venta
 * (<c>origen = Manual</c>; útil para adelantar el catálogo antes del
 * primer pedido A+W). Los atributos fiscales restantes (objeto de
 * impuesto, tasas) se completan en el detalle.
 */
export function NuevoProductoAwProvider({
  children,
}: {
  children: ReactNode;
}) {
  const [abierto, setAbierto] = useState(false);
  const isDirtyRef = useRef(false);

  const setDirty = useCallback((dirty: boolean) => {
    isDirtyRef.current = dirty;
  }, []);

  const cerrarConConfirm = useCallback((opts?: { force?: boolean }) => {
    if (opts?.force === true || !isDirtyRef.current) {
      isDirtyRef.current = false;
      setAbierto(false);
      return;
    }
    const confirmar = window.confirm(
      'Tienes cambios sin guardar en el nuevo producto. ¿Descartarlos y cerrar?',
    );
    if (confirmar) {
      isDirtyRef.current = false;
      setAbierto(false);
    }
  }, []);

  const api = useMemo<NuevoProductoAwApi>(
    () => ({
      abrir: () => {
        isDirtyRef.current = false;
        setAbierto(true);
      },
      cerrar: cerrarConConfirm,
      setDirty,
    }),
    [cerrarConConfirm, setDirty],
  );

  return (
    <NuevoProductoAwContext.Provider value={api}>
      {children}
      <Sheet
        open={abierto}
        onOpenChange={(open) => {
          if (!open) {
            cerrarConConfirm();
            return;
          }
          setAbierto(true);
        }}
      >
        <SheetContent
          side="right"
          className="w-full overflow-y-auto sm:max-w-2xl"
        >
          <SheetHeader>
            <SheetTitle>Nuevo producto A+W</SheetTitle>
            <SheetDescription>
              Alta manual en el master de productos de venta. Las claves
              SAT pueden completarse después en el detalle — sin ellas el
              producto no puede timbrar (aparece en la bandeja «Fiscales
              incompletos»).
            </SheetDescription>
          </SheetHeader>

          <div className="px-6 pb-6">
            {abierto && (
              <NuevoProductoAwForm
                onClose={cerrarConConfirm}
                onDirtyChange={setDirty}
              />
            )}
          </div>
        </SheetContent>
      </Sheet>
    </NuevoProductoAwContext.Provider>
  );
}

interface NuevoProductoAwFormProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange: (dirty: boolean) => void;
}

function NuevoProductoAwForm({
  onClose,
  onDirtyChange,
}: NuevoProductoAwFormProps) {
  const navigate = useNavigate();
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearProductoAw();

  const form = useForm<CrearProductoAwValues>({
    resolver: zodResolver(CrearProductoAwSchema),
    defaultValues: {
      referenciaExterna: '',
      descripcion: '',
      unidadMedida: '',
      unidadMedidaId: null,
      categoriaId: null,
      claveProdServSat: null,
      claveUnidadSat: null,
    },
  });

  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  function onSubmit(values: CrearProductoAwValues) {
    crear.mutate(
      {
        payload: {
          referenciaExterna: values.referenciaExterna,
          descripcion: values.descripcion,
          unidadMedida: values.unidadMedida,
          unidadMedidaId: values.unidadMedidaId,
          categoriaId: values.categoriaId,
          claveProdServSat: values.claveProdServSat,
          claveUnidadSat: values.claveUnidadSat,
          objetoImp: null,
          tasaIvaTraslado: null,
          tasaRetencionIva: null,
          tasaRetencionIsr: null,
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Producto ${resp.referenciaExterna} creado`);
          form.reset(values);
          onClose({ force: true });
          navigate({
            to: '/admin/datos-maestros/productos-aw/$id',
            params: { id: resp.id },
          });
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'PRODUCTO_AW_REFERENCIA_DUPLICADA') {
              form.setError('referenciaExterna', {
                type: error.code,
                message: 'Ya existe un producto con esa referencia.',
              });
              return;
            }
            if (error.code === 'UNIDAD_MEDIDA_NO_ENCONTRADA') {
              form.setError('unidadMedidaId', {
                type: error.code,
                message: 'La unidad seleccionada no existe en el catálogo.',
              });
              return;
            }
            if (error.code === 'CATEGORIA_NO_ENCONTRADA') {
              form.setError('categoriaId', {
                type: error.code,
                message: 'La categoría seleccionada no existe en el catálogo.',
              });
              return;
            }
            if (
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                error,
              )
            ) {
              return;
            }
            toast.error(error.problem.title, {
              description: error.traceId
                ? `Código: ${error.traceId}`
                : undefined,
            });
            return;
          }
          toast.error('Error inesperado al crear el producto A+W.');
        },
      },
    );
  }

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      className="space-y-4 rounded-md border bg-card p-4"
    >
      <FormRow
        label="Referencia externa"
        required
        hint="Referencia del producto en A+W (correlación). Inmutable."
        error={form.formState.errors.referenciaExterna?.message}
      >
        <Input
          autoFocus
          maxLength={50}
          placeholder="VID-TEMP-6MM"
          className="font-mono"
          {...form.register('referenciaExterna')}
        />
      </FormRow>

      <FormRow
        label="Descripción"
        required
        error={form.formState.errors.descripcion?.message}
      >
        <Input
          maxLength={254}
          placeholder="Vidrio templado 6 mm"
          {...form.register('descripcion')}
        />
      </FormRow>

      <FormRow
        label="Unidad de medida (texto)"
        required
        hint="Snapshot corto (PZA, M2, …). Se sincroniza si asignas unidad del catálogo."
        error={form.formState.errors.unidadMedida?.message}
      >
        <Input
          maxLength={20}
          placeholder="M2"
          className="font-mono"
          {...form.register('unidadMedida')}
        />
      </FormRow>

      <FormRow
        label="Unidad del catálogo"
        hint="Opcional. Del catálogo de unidades (ADR-0046)."
        error={form.formState.errors.unidadMedidaId?.message}
      >
        <Controller
          name="unidadMedidaId"
          control={form.control}
          render={({ field }) => (
            <UnidadMedidaSelect
              value={field.value ?? null}
              onChange={field.onChange}
              permitirVacio
            />
          )}
        />
      </FormRow>

      <FormRow
        label="Categoría"
        hint="Opcional. Del catálogo de categorías."
        error={form.formState.errors.categoriaId?.message}
      >
        <Controller
          name="categoriaId"
          control={form.control}
          render={({ field }) => (
            <CategoriaSelector
              value={field.value ?? null}
              onChange={field.onChange}
            />
          )}
        />
      </FormRow>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <FormRow
          label="Clave prod/serv SAT"
          hint="Opcional aquí; requerida para timbrar."
          error={form.formState.errors.claveProdServSat?.message}
        >
          <Controller
            name="claveProdServSat"
            control={form.control}
            render={({ field }) => (
              <Input
                maxLength={8}
                inputMode="numeric"
                placeholder="43211701"
                className="font-mono"
                value={field.value ?? ''}
                onChange={(e) =>
                  field.onChange(
                    e.target.value.length > 0 ? e.target.value : null,
                  )
                }
              />
            )}
          />
        </FormRow>

        <FormRow
          label="Clave unidad SAT"
          hint="Opcional aquí; requerida para timbrar."
          error={form.formState.errors.claveUnidadSat?.message}
        >
          <Controller
            name="claveUnidadSat"
            control={form.control}
            render={({ field }) => (
              <Input
                maxLength={5}
                placeholder="H87"
                className="font-mono"
                value={field.value ?? ''}
                onChange={(e) =>
                  field.onChange(
                    e.target.value.length > 0 ? e.target.value : null,
                  )
                }
              />
            )}
          />
        </FormRow>
      </div>

      <div className="flex flex-wrap items-center justify-end gap-2 border-t pt-3">
        <Button
          type="button"
          variant="ghost"
          onClick={() => onClose()}
          disabled={crear.isPending}
        >
          Cancelar
        </Button>
        <Button type="submit" disabled={crear.isPending}>
          {crear.isPending ? 'Creando…' : 'Crear producto'}
        </Button>
      </div>
    </form>
  );
}

interface FormRowProps {
  label: string;
  required?: boolean;
  hint?: string;
  error?: string;
  children: ReactNode;
}

function FormRow({ label, required, hint, error, children }: FormRowProps) {
  return (
    <div className="space-y-1.5">
      <label className="flex items-center gap-1 text-sm font-medium">
        {label}
        {required && (
          <span aria-hidden="true" className="text-rose-600">
            *
          </span>
        )}
      </label>
      {children}
      {hint != null && error == null && (
        <p className="text-xs text-muted-foreground">{hint}</p>
      )}
      {error != null && (
        <p role="alert" className="text-xs text-rose-600">
          {error}
        </p>
      )}
    </div>
  );
}
