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
import { useCrearUsoPrincipal } from '@/modules/catalogos/api';
import {
  CrearUsoPrincipalSchema,
  type CrearUsoPrincipalValues,
} from '@/modules/catalogos/schemas/uso-principal';
import {
  NuevoUsoPrincipalContext,
  type NuevoUsoPrincipalApi,
} from '@/modules/catalogos/components/nuevo-uso-principal-context';

export function NuevoUsoPrincipalProvider({
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
  const api = useMemo<NuevoUsoPrincipalApi>(
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
    <NuevoUsoPrincipalContext.Provider value={api}>
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
            <SheetTitle>Nuevo uso principal</SheetTitle>
            <SheetDescription>
              Alta en el catálogo cross-empresa de usos principales (no
              producción). La clave es inmutable.
            </SheetDescription>
          </SheetHeader>
          <div className="px-6 pb-6">
            {abierto && (
              <NuevoUsoPrincipalForm onClose={cerrar} onDirtyChange={setDirty} />
            )}
          </div>
        </SheetContent>
      </Sheet>
    </NuevoUsoPrincipalContext.Provider>
  );
}

function NuevoUsoPrincipalForm({
  onClose,
  onDirtyChange,
}: {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange: (d: boolean) => void;
}) {
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearUsoPrincipal();
  const form = useForm<CrearUsoPrincipalValues>({
    resolver: zodResolver(CrearUsoPrincipalSchema),
    defaultValues: { clave: '', nombre: '' },
  });
  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  function onSubmit(values: CrearUsoPrincipalValues) {
    crear.mutate(
      { payload: values, idempotencyKey },
      {
        onSuccess: () => {
          toast.success(`Uso ${values.clave} creado`);
          form.reset(values);
          onClose({ force: true });
        },
        onError: (err) => {
          if (esApiError(err)) {
            if (err.code === 'USO_PRINCIPAL_CLAVE_DUPLICADA') {
              form.setError('clave', {
                type: err.code,
                message: 'Ya existe un uso con esa clave.',
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
          toast.error('Error al crear el uso principal.');
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
          placeholder="MTTO"
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
          maxLength={100}
          placeholder="Mantenimiento"
          {...form.register('nombre')}
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
          {crear.isPending ? 'Creando…' : 'Crear uso'}
        </Button>
      </div>
    </form>
  );
}

function FormRow({
  label,
  required,
  hint,
  error,
  children,
}: {
  label: string;
  required?: boolean;
  hint?: string;
  error?: string;
  children: ReactNode;
}) {
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
