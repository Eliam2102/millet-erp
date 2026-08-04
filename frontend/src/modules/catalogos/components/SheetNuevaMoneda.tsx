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
  NuevaMonedaContext,
  type NuevaMonedaApi,
} from '@/modules/catalogos/components/nueva-moneda-context';
import {
  CrearMonedaSchema,
  type CrearMonedaValues,
} from '@/modules/catalogos/schemas/moneda';
import { useCrearMoneda } from '@/modules/catalogos/api';

/**
 * <c>&lt;NuevaMonedaProvider/&gt;</c> + Sheet asociado (P4 del patrón
 * cross-módulo). Análogo a <see cref="NuevoArticuloProvider"/>: confirm
 * al cerrar con isDirty, force al guardar exitoso, navegación al
 * detalle.
 */
export function NuevaMonedaProvider({ children }: { children: ReactNode }) {
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
      'Tienes cambios sin guardar en la nueva moneda. ¿Descartarlos y cerrar?',
    );
    if (confirmar) {
      isDirtyRef.current = false;
      setAbierto(false);
    }
  }, []);

  const api = useMemo<NuevaMonedaApi>(
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
    <NuevaMonedaContext.Provider value={api}>
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
            <SheetTitle>Nueva moneda</SheetTitle>
            <SheetDescription>
              Alta en el catálogo cross-empresa de monedas. El código
              ISO es inmutable; el nombre y los decimales se editan
              después en el detalle.
            </SheetDescription>
          </SheetHeader>

          <div className="px-6 pb-6">
            {abierto && (
              <NuevaMonedaForm
                onClose={cerrarConConfirm}
                onDirtyChange={setDirty}
              />
            )}
          </div>
        </SheetContent>
      </Sheet>
    </NuevaMonedaContext.Provider>
  );
}

interface NuevaMonedaFormProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange: (dirty: boolean) => void;
}

function NuevaMonedaForm({ onClose, onDirtyChange }: NuevaMonedaFormProps) {
  const navigate = useNavigate();
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearMoneda();

  const form = useForm<CrearMonedaValues>({
    resolver: zodResolver(CrearMonedaSchema),
    defaultValues: {
      codigo: '',
      nombre: '',
      decimales: 2,
      activa: true,
    },
  });

  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  function onSubmit(values: CrearMonedaValues) {
    crear.mutate(
      {
        payload: {
          codigo: values.codigo,
          nombre: values.nombre,
          decimales: values.decimales,
          activa: values.activa,
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Moneda ${resp.codigo} creada`);
          form.reset(values);
          onClose({ force: true });
          navigate({
            to: '/admin/catalogos/monedas/$id',
            params: { id: resp.id },
          });
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'MONEDA_CODIGO_DUPLICADO') {
              form.setError('codigo', {
                type: error.code,
                message: 'Ya existe una moneda con ese código.',
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
          toast.error('Error inesperado al crear la moneda.');
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
        label="Código"
        required
        hint="ISO 4217, 3 letras (ej. MXN, USD, EUR). Inmutable."
        error={form.formState.errors.codigo?.message}
      >
        <Input
          autoFocus
          maxLength={3}
          placeholder="MXN"
          className="font-mono uppercase"
          {...form.register('codigo')}
        />
      </FormRow>

      <FormRow
        label="Nombre"
        required
        error={form.formState.errors.nombre?.message}
      >
        <Input
          maxLength={100}
          placeholder="Peso mexicano"
          {...form.register('nombre')}
        />
      </FormRow>

      <FormRow
        label="Decimales"
        required
        hint="Número de decimales a desplegar (0–6)."
        error={form.formState.errors.decimales?.message}
      >
        <Input
          type="number"
          min={0}
          max={6}
          step={1}
          {...form.register('decimales', { valueAsNumber: true })}
        />
      </FormRow>

      <FormRow label="Activa">
        <Controller
          name="activa"
          control={form.control}
          render={({ field }) => (
            <label className="inline-flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                className="h-4 w-4 rounded border-input"
                checked={field.value}
                onChange={(e) => field.onChange(e.target.checked)}
              />
              <span>{field.value ? 'Activa' : 'Inactiva'}</span>
            </label>
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
          {crear.isPending ? 'Creando…' : 'Crear moneda'}
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
