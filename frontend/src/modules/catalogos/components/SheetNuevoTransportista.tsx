import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
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
import { useCrearTransportista } from '@/modules/catalogos/api';
import {
  CrearTransportistaSchema,
  type CrearTransportistaValues,
} from '@/modules/catalogos/schemas/transportista';
import {
  NuevoTransportistaContext,
  type NuevoTransportistaApi,
} from '@/modules/catalogos/components/nuevo-transportista-context';

export function NuevoTransportistaProvider({
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
  const api = useMemo<NuevoTransportistaApi>(
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
    <NuevoTransportistaContext.Provider value={api}>
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
            <SheetTitle>Nuevo transportista</SheetTitle>
            <SheetDescription>
              Alta en el catálogo cross-empresa de transportistas. La
              clave es inmutable; email y teléfono son opcionales.
            </SheetDescription>
          </SheetHeader>
          <div className="px-6 pb-6">
            {abierto && (
              <NuevoTransportistaForm
                onClose={cerrar}
                onDirtyChange={setDirty}
              />
            )}
          </div>
        </SheetContent>
      </Sheet>
    </NuevoTransportistaContext.Provider>
  );
}

function NuevoTransportistaForm({
  onClose,
  onDirtyChange,
}: {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange: (d: boolean) => void;
}) {
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearTransportista();
  const form = useForm<CrearTransportistaValues>({
    resolver: zodResolver(CrearTransportistaSchema),
    defaultValues: { clave: '', nombre: '', email: null, telefono: null },
  });
  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  function onSubmit(values: CrearTransportistaValues) {
    crear.mutate(
      {
        payload: {
          clave: values.clave,
          nombre: values.nombre,
          email: values.email,
          telefono: values.telefono,
        },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success(`Transportista ${values.clave} creado`);
          form.reset(values);
          onClose({ force: true });
        },
        onError: (err) => {
          if (esApiError(err)) {
            if (err.code === 'TRANSPORTISTA_CLAVE_DUPLICADA') {
              form.setError('clave', {
                type: err.code,
                message: 'Ya existe un transportista con esa clave.',
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
          toast.error('Error al crear el transportista.');
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
          placeholder="DHL"
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
          placeholder="DHL México"
          {...form.register('nombre')}
        />
      </FormRow>
      <FormRow
        label="Email"
        hint="Opcional. Vacío = no aplica."
        error={form.formState.errors.email?.message}
      >
        <Controller
          name="email"
          control={form.control}
          render={({ field }) => (
            <Input
              type="email"
              maxLength={254}
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
        label="Teléfono"
        hint="Opcional."
        error={form.formState.errors.telefono?.message}
      >
        <Controller
          name="telefono"
          control={form.control}
          render={({ field }) => (
            <Input
              maxLength={50}
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
          {crear.isPending ? 'Creando…' : 'Crear transportista'}
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
