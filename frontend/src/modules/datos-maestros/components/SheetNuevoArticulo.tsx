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
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  NuevoArticuloContext,
  type NuevoArticuloApi,
} from '@/modules/datos-maestros/components/nuevo-articulo-context';
import {
  CrearArticuloSchema,
  type CrearArticuloValues,
} from '@/modules/datos-maestros/schemas/articulo';
import { useCrearArticulo } from '@/modules/datos-maestros/api';
import { CategoriaSelector } from '@/components/erp/selectors/CategoriaSelector';
import { Naturaleza } from '@/modules/datos-maestros/api/types';
import { UnidadMedidaSelect } from '@/modules/catalogos/components/UnidadMedidaSelect';

/**
 * <c>&lt;NuevoArticuloProvider/&gt;</c> + Sheet asociado (P4 del
 * patrón cross-módulo). Análogo a
 * <see cref="NuevoProveedorProvider"/>.
 */
export function NuevoArticuloProvider({ children }: { children: ReactNode }) {
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
      'Tienes cambios sin guardar en el nuevo artículo. ¿Descartarlos y cerrar?',
    );
    if (confirmar) {
      isDirtyRef.current = false;
      setAbierto(false);
    }
  }, []);

  const api = useMemo<NuevoArticuloApi>(
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
    <NuevoArticuloContext.Provider value={api}>
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
            <SheetTitle>Nuevo artículo</SheetTitle>
            <SheetDescription>
              Alta del artículo en el catálogo cross-empresa. Datos
              opcionales (descripción larga, precio referencia) se
              ajustan después en el detalle.
            </SheetDescription>
          </SheetHeader>

          <div className="px-6 pb-6">
            {abierto && (
              <NuevoArticuloForm
                onClose={cerrarConConfirm}
                onDirtyChange={setDirty}
              />
            )}
          </div>
        </SheetContent>
      </Sheet>
    </NuevoArticuloContext.Provider>
  );
}

interface NuevoArticuloFormProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange: (dirty: boolean) => void;
}

function NuevoArticuloForm({ onClose, onDirtyChange }: NuevoArticuloFormProps) {
  const navigate = useNavigate();
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearArticulo();

  const form = useForm<CrearArticuloValues>({
    resolver: zodResolver(CrearArticuloSchema),
    defaultValues: {
      clave: '',
      nombre: '',
      unidadMedidaId: '',
      naturaleza: Naturaleza.Estandar,
      descripcionLarga: null,
      categoriaId: null,
      precioReferenciaMonto: null,
      precioReferenciaMoneda: null,
    },
  });

  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  function onSubmit(values: CrearArticuloValues) {
    crear.mutate(
      {
        payload: {
          clave: values.clave,
          nombre: values.nombre,
          unidadMedidaId: values.unidadMedidaId,
          naturaleza: values.naturaleza,
          descripcionLarga: values.descripcionLarga,
          categoriaId: values.categoriaId,
          precioReferenciaMonto: values.precioReferenciaMonto,
          precioReferenciaMoneda: values.precioReferenciaMoneda,
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Artículo ${resp.clave} creado`);
          form.reset(values);
          onClose({ force: true });
          navigate({
            to: '/admin/datos-maestros/articulos/$id',
            params: { id: resp.id },
          });
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'ARTICULO_CLAVE_DUPLICADA') {
              form.setError('clave', {
                type: error.code,
                message: 'Ya existe un artículo con esa clave.',
              });
              return;
            }
            if (error.code === 'ARTICULO_MONEDA_NO_REGISTRADA') {
              form.setError('precioReferenciaMoneda', {
                type: error.code,
                message: 'La moneda no existe en el catálogo.',
              });
              return;
            }
            if (error.code === 'ARTICULO_UNIDAD_MEDIDA_NO_REGISTRADA') {
              form.setError('unidadMedidaId', {
                type: error.code,
                message: 'La unidad seleccionada no existe o no está activa.',
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
          toast.error('Error inesperado al crear el artículo.');
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
        label="Clave"
        required
        hint="Identificador corto. Inmutable."
        error={form.formState.errors.clave?.message}
      >
        <Input
          autoFocus
          maxLength={20}
          placeholder="A-001"
          className="font-mono"
          {...form.register('clave')}
        />
      </FormRow>

      <FormRow
        label="Nombre"
        required
        error={form.formState.errors.nombre?.message}
      >
        <Input
          maxLength={254}
          placeholder="Caja de cartón 30×30×30"
          {...form.register('nombre')}
        />
      </FormRow>

      <FormRow
        label="Unidad de medida"
        required
        hint="Del catálogo de unidades."
        error={form.formState.errors.unidadMedidaId?.message}
      >
        <Controller
          name="unidadMedidaId"
          control={form.control}
          render={({ field }) => (
            <UnidadMedidaSelect
              value={field.value || null}
              onChange={(id) => field.onChange(id ?? '')}
            />
          )}
        />
      </FormRow>

      <FormRow
        label="Naturaleza"
        required
        hint="Alimenta la matriz de aprobación de Compras."
        error={form.formState.errors.naturaleza?.message}
      >
        <Controller
          name="naturaleza"
          control={form.control}
          render={({ field }) => (
            <Select
              value={String(field.value)}
              onValueChange={(v) => field.onChange(Number(v))}
            >
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={String(Naturaleza.Estandar)}>
                  Estándar
                </SelectItem>
                <SelectItem value={String(Naturaleza.Servicio)}>
                  Servicio
                </SelectItem>
                <SelectItem value={String(Naturaleza.Critico)}>
                  Crítico
                </SelectItem>
                <SelectItem value={String(Naturaleza.Riesgo)}>
                  Riesgo
                </SelectItem>
              </SelectContent>
            </Select>
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
          {crear.isPending ? 'Creando…' : 'Crear artículo'}
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
