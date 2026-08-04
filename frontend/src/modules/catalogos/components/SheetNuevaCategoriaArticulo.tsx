import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { useForm } from 'react-hook-form';
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
import { useCrearCategoriaArticulo } from '@/modules/catalogos/api';
import {
  CrearCategoriaArticuloSchema,
  type CrearCategoriaArticuloValues,
} from '@/modules/catalogos/schemas/categoria-articulo';
import {
  NuevaCategoriaArticuloContext,
  type NuevaCategoriaArticuloApi,
} from '@/modules/catalogos/components/nueva-categoria-articulo-context';

export function NuevaCategoriaArticuloProvider({
  children,
}: {
  children: ReactNode;
}) {
  const [abierto, setAbierto] = useState(false);
  const isDirtyRef = useRef(false);
  const setDirty = useCallback((d: boolean) => {
    isDirtyRef.current = d;
  }, []);
  const cerrar = useCallback((opts?: { force?: boolean }) => {
    if (opts?.force === true || !isDirtyRef.current) {
      isDirtyRef.current = false;
      setAbierto(false);
      return;
    }
    if (window.confirm('Tienes cambios sin guardar. ¿Descartarlos y cerrar?')) {
      isDirtyRef.current = false;
      setAbierto(false);
    }
  }, []);
  const api = useMemo<NuevaCategoriaArticuloApi>(
    () => ({
      abrir: () => {
        isDirtyRef.current = false;
        setAbierto(true);
      },
      cerrar,
      setDirty,
    }),
    [cerrar, setDirty],
  );
  return (
    <NuevaCategoriaArticuloContext.Provider value={api}>
      {children}
      <Sheet
        open={abierto}
        onOpenChange={(open) => (open ? setAbierto(true) : cerrar())}
      >
        <SheetContent
          side="right"
          className="w-full overflow-y-auto sm:max-w-2xl"
        >
          <SheetHeader>
            <SheetTitle>Nueva categoría de artículo</SheetTitle>
            <SheetDescription>
              Alta en el catálogo cross-empresa de categorías de artículo. El
              nombre es único (sin distinguir mayúsculas ni espacios extra).
            </SheetDescription>
          </SheetHeader>
          <div className="px-6 pb-6">
            {abierto && (
              <NuevaCategoriaArticuloForm
                onClose={cerrar}
                onDirtyChange={setDirty}
              />
            )}
          </div>
        </SheetContent>
      </Sheet>
    </NuevaCategoriaArticuloContext.Provider>
  );
}

function NuevaCategoriaArticuloForm({
  onClose,
  onDirtyChange,
}: {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange: (d: boolean) => void;
}) {
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearCategoriaArticulo();
  const form = useForm<CrearCategoriaArticuloValues>({
    resolver: zodResolver(CrearCategoriaArticuloSchema),
    defaultValues: { nombre: '' },
  });
  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  function onSubmit(values: CrearCategoriaArticuloValues) {
    crear.mutate(
      { payload: values, idempotencyKey },
      {
        onSuccess: () => {
          toast.success(`Categoría «${values.nombre}» creada`);
          form.reset(values);
          onClose({ force: true });
        },
        onError: (err) => {
          if (esApiError(err)) {
            if (err.code === 'CATEGORIA_ARTICULO_NOMBRE_DUPLICADO') {
              form.setError('nombre', {
                type: err.code,
                message: 'Ya existe una categoría con ese nombre.',
              });
              return;
            }
            if (
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                err,
              )
            )
              return;
            toast.error(err.problem.title);
            return;
          }
          toast.error('Error al crear la categoría.');
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
      <div className="space-y-1.5">
        <label className="flex items-center gap-1 text-sm font-medium">
          Nombre
          <span aria-hidden="true" className="text-rose-600">
            *
          </span>
        </label>
        <Input
          autoFocus
          maxLength={100}
          placeholder="MAT DE LIMPIEZA"
          {...form.register('nombre')}
        />
        {form.formState.errors.nombre?.message != null && (
          <p role="alert" className="text-xs text-rose-600">
            {form.formState.errors.nombre.message}
          </p>
        )}
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
          {crear.isPending ? 'Creando…' : 'Crear categoría'}
        </Button>
      </div>
    </form>
  );
}
