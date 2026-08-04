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
import { Textarea } from '@/components/ui/textarea';
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
  NuevoRolContext,
  type NuevoRolApi,
} from '@/modules/identidad/components/nuevo-rol-context';
import {
  CrearRolSchema,
  type CrearRolValues,
} from '@/modules/identidad/schemas/rol';
import { useCrearRol } from '@/modules/identidad/api';

/**
 * <c>&lt;NuevoRolProvider/&gt;</c> + Sheet asociado (P4 del patrón
 * cross-módulo). Slide-from-right max-w-2xl con form de alta de rol
 * (Código + Nombre + Descripción).
 *
 * <para><b>Confirm al cerrar con isDirty</b>: imita
 * <c>NuevaEmpresaProvider</c>. La key (Esc, click fuera, X interno)
 * solicita confirmación.</para>
 *
 * <para><b>Post-submit</b>: invalida la lista, toast con código,
 * navega a <c>/admin/roles/$id</c> y cierra el Sheet con
 * <c>force: true</c>.</para>
 */
export function NuevoRolProvider({ children }: { children: ReactNode }) {
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
      'Tienes cambios sin guardar en el nuevo rol. ¿Descartarlos y cerrar?',
    );
    if (confirmar) {
      isDirtyRef.current = false;
      setAbierto(false);
    }
  }, []);

  const api = useMemo<NuevoRolApi>(
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
    <NuevoRolContext.Provider value={api}>
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
            <SheetTitle>Nuevo rol</SheetTitle>
            <SheetDescription>
              Alta de rol. Los permisos y grupos de Entra ID se asignan
              después en el detalle.
            </SheetDescription>
          </SheetHeader>

          <div className="px-6 pb-6">
            {abierto && (
              <NuevoRolForm
                onClose={cerrarConConfirm}
                onDirtyChange={setDirty}
              />
            )}
          </div>
        </SheetContent>
      </Sheet>
    </NuevoRolContext.Provider>
  );
}

interface NuevoRolFormProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange: (dirty: boolean) => void;
}

function NuevoRolForm({ onClose, onDirtyChange }: NuevoRolFormProps) {
  const navigate = useNavigate();
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearRol();

  const form = useForm<CrearRolValues>({
    resolver: zodResolver(CrearRolSchema),
    defaultValues: {
      codigo: '',
      nombre: '',
      descripcion: null,
    },
  });

  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  function onSubmit(values: CrearRolValues) {
    crear.mutate(
      {
        command: {
          codigo: values.codigo,
          nombre: values.nombre,
          descripcion: values.descripcion,
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Rol "${resp.codigo}" creado`, {
            description: resp.nombre,
          });
          form.reset(values);
          onClose({ force: true });
          navigate({
            to: '/admin/roles/$id',
            params: { id: resp.id },
          });
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'ROL_CODIGO_DUPLICADO') {
              form.setError('codigo', {
                type: error.code,
                message: 'Ya existe un rol con ese código.',
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
          toast.error('Error inesperado al crear el rol.');
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
        hint="Lowercase, letras/dígitos/guiones (ej. admin-compras, auditor)."
        error={form.formState.errors.codigo?.message}
      >
        <Input
          autoFocus
          maxLength={64}
          placeholder="admin-compras"
          className="font-mono"
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
          placeholder="Administrador de Compras"
          {...form.register('nombre')}
        />
      </FormRow>

      <FormRow
        label="Descripción"
        hint="Opcional. Máximo 500 caracteres."
        error={form.formState.errors.descripcion?.message}
      >
        <Controller
          name="descripcion"
          control={form.control}
          render={({ field }) => (
            <Textarea
              maxLength={500}
              rows={3}
              placeholder="Para qué se usa este rol…"
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
          {crear.isPending ? 'Creando…' : 'Crear rol'}
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
