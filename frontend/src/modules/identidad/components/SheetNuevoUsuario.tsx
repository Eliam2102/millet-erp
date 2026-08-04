import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { useNavigate } from '@tanstack/react-router';
import { Controller, useForm, useWatch } from 'react-hook-form';
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
import { DepartamentoSelector } from '@/components/erp';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  NuevoUsuarioContext,
  type NuevoUsuarioApi,
} from '@/modules/identidad/components/nuevo-usuario-context';
import {
  CrearUsuarioSchema,
  type CrearUsuarioValues,
} from '@/modules/identidad/schemas/usuario';
import { useCrearUsuario } from '@/modules/identidad/api';
import { useEntraIdUsuarioSearch } from '@/lib/identidad/use-entra-id-usuario-search';

/**
 * <c>&lt;NuevoUsuarioProvider/&gt;</c> + Sheet asociado (P4 del patrón
 * cross-módulo). Slide-from-right max-w-2xl con form de alta de usuario
 * (Email + Nombre + Departamento).
 *
 * <para><b>Confirm al cerrar con isDirty</b>: imita
 * <c>NuevoRolProvider</c> / <c>NuevaEmpresaProvider</c>.</para>
 *
 * <para><b>Post-submit</b>: invalida la lista, toast con email, navega
 * a <c>/admin/usuarios/$id</c> y cierra el Sheet con
 * <c>force: true</c>.</para>
 *
 * <para><b>Sin autocomplete contra Entra ID</b>: el backend genera un
 * placeholder <c>dev-{email}</c> con <c>LocalEntraIdResolverNoOp</c>
 * cuando el admin no provee ObjectId. Mientras llega el resolver
 * (ver <c>PLATFORM-TODO(&lt;EntraIdResolver&gt;)</c>), hay un campo
 * manual "Entra ID Object ID" para que el admin pegue el GUID desde
 * Azure Portal — eso permite que el usuario inicie sesión real con
 * su cuenta de Entra ID. El stub
 * <see cref="useEntraIdUsuarioSearch"/> queda cableado para cuando
 * el resolver real conecte el autocomplete.</para>
 */
export function NuevoUsuarioProvider({ children }: { children: ReactNode }) {
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
      'Tienes cambios sin guardar en el nuevo usuario. ¿Descartarlos y cerrar?',
    );
    if (confirmar) {
      isDirtyRef.current = false;
      setAbierto(false);
    }
  }, []);

  const api = useMemo<NuevoUsuarioApi>(
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
    <NuevoUsuarioContext.Provider value={api}>
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
            <SheetTitle>Nuevo usuario</SheetTitle>
            <SheetDescription>
              Alta de usuario. La asignación de roles por empresa se
              gestiona después en el detalle.
            </SheetDescription>
          </SheetHeader>

          <div className="px-6 pb-6">
            {abierto && (
              <NuevoUsuarioForm
                onClose={cerrarConConfirm}
                onDirtyChange={setDirty}
              />
            )}
          </div>
        </SheetContent>
      </Sheet>
    </NuevoUsuarioContext.Provider>
  );
}

interface NuevoUsuarioFormProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange: (dirty: boolean) => void;
}

function NuevoUsuarioForm({ onClose, onDirtyChange }: NuevoUsuarioFormProps) {
  const navigate = useNavigate();
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearUsuario();

  const form = useForm<CrearUsuarioValues>({
    resolver: zodResolver(CrearUsuarioSchema),
    defaultValues: {
      email: '',
      nombreCompleto: '',
      departamentoId: null,
      entraIdObjectId: null,
    },
  });

  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  // Stub Entra ID: hoy siempre devuelve <c>undefined</c>. Cuando el
  // resolver real llegue, podemos auto-completar nombre + objectId al
  // detectar email válido. La rama de auto-fill no está activa hoy.
  // <c>useWatch</c> en lugar de <c>form.watch</c> para suscripción
  // granular y evitar el lint <c>react-hooks/incompatible-library</c>.
  const emailWatch = useWatch({ control: form.control, name: 'email' });
  useEntraIdUsuarioSearch(emailWatch);

  function onSubmit(values: CrearUsuarioValues) {
    crear.mutate(
      {
        command: {
          email: values.email,
          nombreCompleto: values.nombreCompleto,
          departamentoId: values.departamentoId,
          entraIdObjectId: values.entraIdObjectId,
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Usuario ${resp.email} creado`, {
            description: resp.nombre,
          });
          form.reset(values);
          onClose({ force: true });
          navigate({
            to: '/admin/usuarios/$id',
            params: { id: resp.id },
          });
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'USUARIO_EMAIL_DUPLICADO') {
              form.setError('email', {
                type: error.code,
                message: 'Ya existe un usuario con ese email.',
              });
              return;
            }
            if (error.code === 'USUARIO_OID_DUPLICADO') {
              form.setError('email', {
                type: error.code,
                message:
                  'Ya existe un usuario con el mismo Entra ID Object ID.',
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
          toast.error('Error inesperado al crear el usuario.');
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
        label="Email"
        required
        error={form.formState.errors.email?.message}
      >
        <Input
          type="email"
          autoFocus
          maxLength={254}
          placeholder="usuario@millet.com.mx"
          {...form.register('email')}
        />
      </FormRow>

      <FormRow
        label="Nombre completo"
        required
        error={form.formState.errors.nombreCompleto?.message}
      >
        <Input
          maxLength={254}
          placeholder="Juan Pérez"
          {...form.register('nombreCompleto')}
        />
      </FormRow>

      <FormRow
        label="Entra ID Object ID"
        hint="Pégalo desde Azure Portal → Entra ID → Users → [usuario] → Object ID. Si lo dejas vacío, el usuario quedará con un placeholder y NO podrá iniciar sesión real hasta que el resolver de Entra ID esté disponible."
        error={form.formState.errors.entraIdObjectId?.message}
      >
        <Input
          maxLength={36}
          placeholder="00000000-0000-0000-0000-000000000000"
          autoComplete="off"
          {...form.register('entraIdObjectId')}
        />
      </FormRow>

      <FormRow
        label="Departamento"
        hint="Opcional. Se puede asignar más tarde desde el detalle."
        error={form.formState.errors.departamentoId?.message}
      >
        <Controller
          name="departamentoId"
          control={form.control}
          render={({ field }) => (
            <DepartamentoSelector
              value={field.value}
              onChange={(v) => field.onChange(v)}
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
          {crear.isPending ? 'Creando…' : 'Crear usuario'}
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
