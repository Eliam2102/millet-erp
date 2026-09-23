import { useEffect } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Check, Plus, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  EmpleadoSchema,
  type EmpleadoValues,
} from '@/modules/administracion/schemas/empleado';
import {
  useActualizarEmpleado,
  useCrearEmpleado,
} from '@/modules/administracion/api';
import type { ActualizarEmpleadoPayload } from '@/modules/administracion/api/types';
import type { EmpleadoListItem } from '@/features/catalogos/api';
import {
  DepartamentoSelector,
  EmpleadoSelector,
  PuestoSelector,
  SucursalSelector,
  UsuarioSelector,
} from '@/components/erp';
import { useAuthStore } from '@/lib/auth/auth-store';
import { cn } from '@/lib/utils';

/**
 * Form inline (sin modal) para AGREGAR o EDITAR un empleado
 * (ADM-FE-PR1). Mismo patrón que <c>CanalVentaInlineForm</c>. La clave
 * es business key inmutable; la empresa se toma de la sesión actual al
 * crear y es inmutable después.
 *
 * <para>En modo editar, vaciar un campo opcional (email, puesto, jefe,
 * sucursal, departamento, usuario, código de nómina) manda el flag
 * <c>limpiar*</c> correspondiente (convención del PATCH backend:
 * null = no tocar).</para>
 */
export interface EmpleadoInlineFormProps {
  empleado?: EmpleadoListItem | null;
  sucursalIdInicial?: string;
  onCancel: () => void;
  onSaved?: () => void;
}

const VALORES_INICIALES: EmpleadoValues = {
  clave: '',
  nombre: '',
  email: '',
  puestoId: '',
  jefeDirectoId: '',
  sucursalId: '',
  departamentoId: '',
  usuarioId: '',
  codigoNomina: '',
};

export function EmpleadoInlineForm({
  empleado,
  sucursalIdInicial,
  onCancel,
  onSaved,
}: EmpleadoInlineFormProps) {
  const esEditar = empleado != null;
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearEmpleado();
  const actualizar = useActualizarEmpleado();
  const currentEmpresaId = useAuthStore((s) => s.currentEmpresaId);

  const form = useForm<EmpleadoValues>({
    resolver: zodResolver(EmpleadoSchema),
    defaultValues: esEditar
      ? {
          clave: empleado.clave,
          nombre: empleado.nombre,
          email: empleado.email ?? '',
          puestoId: empleado.puestoId ?? '',
          jefeDirectoId: empleado.jefeDirectoId ?? '',
          sucursalId: empleado.sucursalId ?? '',
          departamentoId: empleado.departamentoId ?? '',
          usuarioId: empleado.usuarioId ?? '',
          // El list item no trae codigoNomina; vacío = no tocar (el
          // PATCH solo lo limpia si el usuario lo teclea y borra).
          codigoNomina: '',
        }
      : { ...VALORES_INICIALES, sucursalId: sucursalIdInicial ?? '' },
  });

  useEffect(() => {
    form.setFocus(esEditar ? 'nombre' : 'clave');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const isPending = crear.isPending || actualizar.isPending;

  function onError(error: Error) {
    if (esApiError(error)) {
      if (error.code === 'EMPLEADO_CLAVE_DUPLICADA') {
        form.setError('clave', {
          type: error.code,
          message: 'Ya existe un empleado con esa clave en la empresa.',
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
        description: error.traceId ? `Código: ${error.traceId}` : undefined,
      });
      return;
    }
    toast.error('Error inesperado al guardar el empleado.');
  }

  function onSubmit(values: EmpleadoValues) {
    if (esEditar && empleado != null) {
      actualizar.mutate(
        { id: empleado.id, payload: payloadPatch(values, empleado), idempotencyKey },
        {
          onSuccess: () => {
            toast.success('Empleado actualizado');
            onSaved?.();
          },
          onError,
        },
      );
      return;
    }

    if (!currentEmpresaId) {
      toast.error('No hay empresa seleccionada en la sesión.');
      return;
    }
    crear.mutate(
      {
        command: {
          id: '00000000-0000-0000-0000-000000000000',
          empresaId: currentEmpresaId,
          clave: values.clave,
          nombre: values.nombre,
          email: values.email || null,
          puestoId: values.puestoId || null,
          jefeDirectoId: values.jefeDirectoId || null,
          sucursalId: values.sucursalId || null,
          departamentoId: values.departamentoId || null,
          usuarioId: values.usuarioId || null,
          codigoNomina: values.codigoNomina || null,
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Empleado "${resp.nombre}" agregado`);
          form.reset({ ...VALORES_INICIALES, sucursalId: sucursalIdInicial ?? '' });
          form.setFocus('clave');
          onSaved?.();
        },
        onError,
      },
    );
  }

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      onKeyDown={(e) => {
        if (e.key === 'Escape' && !isPending) {
          e.preventDefault();
          onCancel();
        }
      }}
      className={cn(
        'space-y-3 rounded-md border p-3',
        esEditar
          ? 'border-amber-400 bg-amber-50/40'
          : 'border-dashed border-primary/40 bg-primary/5',
      )}
      aria-label={
        esEditar ? `Editar empleado ${empleado?.clave}` : 'Agregar empleado'
      }
    >
      <div className="grid grid-cols-1 gap-2 md:grid-cols-12">
        <Field
          label="Clave"
          required
          error={form.formState.errors.clave?.message}
          className="md:col-span-3"
        >
          <Input
            maxLength={20}
            placeholder="EMP-001"
            className="font-mono"
            disabled={esEditar}
            title={
              esEditar
                ? 'La clave es inmutable (business key).'
                : 'Clave única del empleado en la empresa.'
            }
            {...form.register('clave')}
          />
        </Field>

        <Field
          label="Nombre"
          required
          error={form.formState.errors.nombre?.message}
          className="md:col-span-5"
        >
          <Input
            maxLength={254}
            placeholder="Juana Pérez"
            {...form.register('nombre')}
          />
        </Field>

        <Field
          label="Email"
          error={form.formState.errors.email?.message}
          className="md:col-span-4"
        >
          <Input
            maxLength={254}
            type="email"
            placeholder="juana.perez@millet.mx"
            {...form.register('email')}
          />
        </Field>

        <Field label="Puesto" className="md:col-span-4">
          <Controller
            control={form.control}
            name="puestoId"
            render={({ field }) => (
              <PuestoSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
                className="w-full"
              />
            )}
          />
        </Field>

        <Field label="Jefe directo (autoriza viáticos N1)" className="md:col-span-4">
          <Controller
            control={form.control}
            name="jefeDirectoId"
            render={({ field }) => (
              <EmpleadoSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
                excludeId={empleado?.id}
                className="w-full"
              />
            )}
          />
        </Field>

        <Field label="Sucursal" className="md:col-span-4">
          <Controller
            control={form.control}
            name="sucursalId"
            render={({ field }) => (
              <SucursalSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
                className="w-full"
              />
            )}
          />
        </Field>

        <Field label="Departamento" className="md:col-span-4">
          <Controller
            control={form.control}
            name="departamentoId"
            render={({ field }) => (
              <DepartamentoSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
                className="w-full"
              />
            )}
          />
        </Field>

        <Field label="Usuario del sistema" className="md:col-span-4">
          <Controller
            control={form.control}
            name="usuarioId"
            render={({ field }) => (
              <UsuarioSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
                className="w-full"
              />
            )}
          />
        </Field>

        <Field
          label="Código de nómina"
          error={form.formState.errors.codigoNomina?.message}
          className="md:col-span-4"
        >
          <Input
            maxLength={20}
            placeholder="A1234"
            className="font-mono"
            title="Referencia al código de nómina/SAP (Axxxx). Sin lógica en el ERP."
            {...form.register('codigoNomina')}
          />
        </Field>
      </div>

      <div className="flex items-center justify-end gap-2">
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onCancel}
          disabled={isPending}
        >
          <X className="mr-1 h-4 w-4" />
          Cancelar
        </Button>
        <Button type="submit" size="sm" disabled={isPending}>
          {esEditar ? (
            <>
              <Check className="mr-1 h-4 w-4" />
              {isPending ? 'Guardando…' : 'Guardar cambios'}
            </>
          ) : (
            <>
              <Plus className="mr-1 h-4 w-4" />
              {isPending ? 'Agregando…' : 'Agregar empleado'}
            </>
          )}
        </Button>
      </div>
    </form>
  );
}

/**
 * Convierte los valores del form al payload del PATCH: campo vaciado
 * que antes tenía valor ⇒ flag <c>limpiar*</c>; vacío que ya era null
 * ⇒ no tocar (null); con valor ⇒ set/replace.
 */
function payloadPatch(
  values: EmpleadoValues,
  anterior: EmpleadoListItem,
): ActualizarEmpleadoPayload {
  const campo = (nuevo: string | undefined, previo: string | null) => {
    const v = nuevo?.trim() ?? '';
    return {
      valor: v === '' ? null : v,
      limpiar: v === '' && previo != null,
    };
  };

  const email = campo(values.email, anterior.email);
  const puesto = campo(values.puestoId, anterior.puestoId);
  const jefe = campo(values.jefeDirectoId, anterior.jefeDirectoId);
  const sucursal = campo(values.sucursalId, anterior.sucursalId);
  const departamento = campo(values.departamentoId, anterior.departamentoId);
  const usuario = campo(values.usuarioId, anterior.usuarioId);
  // codigoNomina no viene en el list item: vacío = no tocar, nunca limpiar.
  const codigoNomina = values.codigoNomina?.trim() || null;

  return {
    nombre: values.nombre,
    email: email.valor,
    limpiarEmail: email.limpiar,
    puestoId: puesto.valor,
    limpiarPuesto: puesto.limpiar,
    jefeDirectoId: jefe.valor,
    limpiarJefeDirecto: jefe.limpiar,
    sucursalId: sucursal.valor,
    limpiarSucursal: sucursal.limpiar,
    departamentoId: departamento.valor,
    limpiarDepartamento: departamento.limpiar,
    usuarioId: usuario.valor,
    limpiarUsuario: usuario.limpiar,
    codigoNomina,
  };
}

interface FieldProps {
  label: string;
  required?: boolean;
  error?: string;
  className?: string;
  children: React.ReactNode;
}

function Field({ label, required, error, className, children }: FieldProps) {
  return (
    <div className={cn('space-y-1', className)}>
      <label className="flex items-center gap-1 text-xs font-medium text-muted-foreground">
        {label}
        {required && (
          <span aria-hidden="true" className="text-rose-600">
            *
          </span>
        )}
      </label>
      {children}
      {error != null && (
        <p role="alert" className="text-xs text-rose-600">
          {error}
        </p>
      )}
    </div>
  );
}
