import { useEffect, useState } from 'react';
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
  useAltaColaborador,
} from '@/modules/administracion/api';
import { useRoles } from '@/modules/identidad/api/roles';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { ActualizarEmpleadoPayload } from '@/modules/administracion/api/types';
import type { EmpleadoListItem } from '@/features/catalogos/api';
import {
  DepartamentoSelector,
  EmpleadoSelector,
  PuestoSelector,
  SucursalSelector,
  UsuarioSelector,
} from '@/components/erp';
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
  sucursalFija?: boolean;
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
  sucursalFija = false,
  onCancel,
  onSaved,
}: EmpleadoInlineFormProps) {
  const esEditar = empleado != null;
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useAltaColaborador();
  const actualizar = useActualizarEmpleado();
  const canCrearUsuarios = useHasPermission(PermisosCanonicos.IdentidadUsuariosCrear);
  const canAsignar = useHasPermission(PermisosCanonicos.IdentidadAsignacionesAdministrar);
  const canDarAcceso = canCrearUsuarios && canAsignar;
  const roles = useRoles({ soloActivos: true, limit: 200 }, !esEditar && canDarAcceso);
  const [acceso, setAcceso] = useState<0 | 1 | 2>(0);
  const [emailContacto, setEmailContacto] = useState('');
  const [rolId, setRolId] = useState('');

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

    if (!values.sucursalId || !values.departamentoId || !values.puestoId) {
      toast.error('Selecciona sucursal, departamento y puesto para el alta.');
      return;
    }
    if (acceso !== 0 && (!canDarAcceso || !values.email || !rolId)) {
      toast.error('Para dar acceso, indica correo corporativo y rol.');
      return;
    }
    if (acceso === 2 && !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(emailContacto.trim())) {
      toast.error('Indica un correo de contacto válido para una cuenta nueva.');
      return;
    }
    crear.mutate(
      {
        command: {
          id: '00000000-0000-0000-0000-000000000000',
          clave: values.clave,
          nombre: values.nombre,
          puestoId: values.puestoId,
          jefeDirectoId: values.jefeDirectoId || null,
          sucursalId: values.sucursalId,
          departamentoId: values.departamentoId,
          acceso,
          correoCorporativo: values.email || null,
          emailContacto: acceso === 2 ? emailContacto.trim() : null,
          rolId: acceso === 0 ? null : rolId,
          codigoNomina: values.codigoNomina || null,
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Colaborador "${resp.empleado.nombre}" agregado`, {
            description: acceso === 2
              ? 'La cuenta queda pendiente de provisión; no se ha enviado acceso real.'
              : undefined,
          });
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
          label={esEditar ? 'Email' : 'Correo corporativo'}
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

        <Field
          label={sucursalFija ? 'Sucursal (fijada)' : 'Sucursal'}
          className="md:col-span-4"
        >
          <Controller
            control={form.control}
            name="sucursalId"
            render={({ field }) => (
              <SucursalSelector
                value={field.value || null}
                onChange={(id) => {
                  const nuevo = id ?? '';
                  if (nuevo !== field.value) {
                    field.onChange(nuevo);
                    form.setValue('puestoId', '');
                    form.setValue('departamentoId', '');
                  }
                }}
                disabled={sucursalFija}
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
                onChange={(id) => {
                  const nuevo = id ?? '';
                  if (nuevo !== field.value) {
                    field.onChange(nuevo);
                    form.setValue('puestoId', '');
                  }
                }}
                sucursalId={form.watch('sucursalId') || undefined}
                className="w-full"
              />
            )}
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
                sucursalId={form.watch('sucursalId') || undefined}
                departamentoId={form.watch('departamentoId') || undefined}
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

        {esEditar && <Field label="Usuario del sistema" className="md:col-span-4">
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
        </Field>}

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

      {!esEditar && (
        <div className="grid grid-cols-1 gap-3 border-t pt-3 md:grid-cols-3">
          <Field label="Acceso al ERP">
            <select
              aria-label="Acceso al ERP"
              className="h-9 w-full rounded-md border bg-background px-3 text-sm"
              value={acceso}
              onChange={(e) => setAcceso(Number(e.target.value) as 0 | 1 | 2)}
            >
              <option value={0}>Sin acceso</option>
              {canDarAcceso && <option value={1}>Ya tiene cuenta Microsoft (validación local)</option>}
              {canDarAcceso && <option value={2}>Cuenta Microsoft nueva (pendiente)</option>}
            </select>
          </Field>
          {acceso !== 0 && (
            <Field label="Rol en la empresa" required>
              <select
                aria-label="Rol en la empresa"
                className="h-9 w-full rounded-md border bg-background px-3 text-sm"
                value={rolId}
                onChange={(e) => setRolId(e.target.value)}
              >
                <option value="">Selecciona un rol</option>
                {(roles.data?.items ?? []).filter((r) => r.activo).map((r) => (
                  <option key={r.id} value={r.id}>{r.nombre}</option>
                ))}
              </select>
            </Field>
          )}
          {acceso === 2 && (
            <Field label="Correo personal de contacto" required>
              <Input
                type="email"
                value={emailContacto}
                onChange={(e) => setEmailContacto(e.target.value)}
                placeholder="contacto@ejemplo.com"
              />
            </Field>
          )}
          {acceso === 2 && (
            <p className="text-xs text-amber-700 md:col-span-3">
              El alta quedará pendiente. En este ambiente no se crea una cuenta real ni se envía correo.
            </p>
          )}
          {acceso === 1 && (
            <p className="text-xs text-amber-700 md:col-span-3">
              La cuenta se verifica contra el directorio simulado. Antes de operar con usuarios reales falta conectar Microsoft Graph.
            </p>
          )}
        </div>
      )}

      <div className="flex items-center justify-end gap-2">
        <div className="flex items-center gap-2">
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
