import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Check, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  AsignarRolSchema,
  type AsignarRolValues,
} from '@/modules/identidad/schemas/usuario';
import { useAsignarRol, useRoles } from '@/modules/identidad/api';
import { useEmpresas } from '@/modules/administracion/api';
import type { RolResponse } from '@/modules/identidad/api/types';
import type { EmpresaResponse } from '@/modules/administracion/api/types';
import { cn } from '@/lib/utils';

/**
 * Form inline (sin modal) para ASIGNAR un rol al usuario en una
 * empresa. Mismo patrón inline-no-modal de
 * <c>SucursalInlineForm</c> / <c>GrupoEntraIdInlineForm</c>: border
 * dashed primary, 2 selects + botones Cancelar/Guardar.
 *
 * <para>Sin modo edición — para cambiar la asignación se revoca y se
 * vuelve a asignar (no hay PATCH backend).</para>
 *
 * <para><b>Wrapper-loader</b>: el form interno (<c>FormReady</c>) se
 * monta sólo cuando empresas y roles ya cargaron. Esto evita que el
 * <c>Select</c> de shadcn alterne entre uncontrolled (value=undefined
 * mientras carga) y controlled (value=uuid tras auto-select), lo que
 * antes provocaba que <c>onValueChange</c> no propagara y el form
 * quedara con valores vacíos. Con la pre-carga, los <c>defaultValues</c>
 * ya tienen el id correcto al primer render del form.</para>
 */
export interface RolesPorEmpresaInlineFormProps {
  usuarioId: string;
  onCancel: () => void;
  onSaved?: () => void;
}

// Sentinel string para el <Select> de shadcn/Radix: Radix no acepta
// value="" (tira error), pero alternar entre undefined y string causa
// que Radix trate el componente como uncontrolled de por vida y deje
// de propagar onValueChange al form (síntoma: Select muestra el item
// seleccionado pero RHF mantiene field.value=""). El sentinel mantiene
// el Select controlled-con-string desde el mount; como ningún
// SelectItem usa este value, Radix muestra el placeholder mientras
// sea el valor activo.
const EMPTY_SELECT = '__none__';

export function RolesPorEmpresaInlineForm({
  usuarioId,
  onCancel,
  onSaved,
}: RolesPorEmpresaInlineFormProps) {
  const empresasQuery = useEmpresas({ limit: 200, soloActivas: true });
  const rolesQuery = useRoles({ soloActivos: true, limit: 200 });

  if (empresasQuery.isLoading || rolesQuery.isLoading) {
    return (
      <div
        className={cn(
          'rounded-md border p-3 text-sm text-muted-foreground',
          'border-dashed border-primary/40 bg-primary/5',
        )}
      >
        Cargando empresas y roles…
      </div>
    );
  }

  return (
    <FormReady
      usuarioId={usuarioId}
      empresas={empresasQuery.data?.items ?? []}
      roles={rolesQuery.data?.items ?? []}
      onCancel={onCancel}
      onSaved={onSaved}
    />
  );
}

interface FormReadyProps {
  usuarioId: string;
  empresas: ReadonlyArray<EmpresaResponse>;
  roles: ReadonlyArray<RolResponse>;
  onCancel: () => void;
  onSaved?: () => void;
}

function FormReady({
  usuarioId,
  empresas,
  roles,
  onCancel,
  onSaved,
}: FormReadyProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const asignar = useAsignarRol();

  // Auto-seleccionar la empresa si solo hay una activa — patrón
  // habitual para tenants con sucursal única (memoria
  // <c>project_oc_submodulo_scope</c>). Se hace via defaultValues (no
  // setValue posterior) para que el Select arranque controlled desde
  // el primer render y onValueChange propague correctamente.
  const form = useForm<AsignarRolValues>({
    resolver: zodResolver(AsignarRolSchema),
    defaultValues: {
      empresaId: empresas.length === 1 ? empresas[0].id : '',
      rolId: '',
    },
  });

  function onSubmit(values: AsignarRolValues) {
    asignar.mutate(
      {
        usuarioId,
        payload: { empresaId: values.empresaId, rolId: values.rolId },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Asignación creada');
          form.reset({
            empresaId: empresas.length === 1 ? empresas[0].id : '',
            rolId: '',
          });
          onSaved?.();
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (
              error.code === 'USUARIO_YA_ASIGNADO_A_EMPRESA_ROL' ||
              error.code === 'USUARIO_EMPRESA_ROL_DUPLICADO'
            ) {
              toast.error(
                'El usuario ya tiene ese rol en esa empresa.',
              );
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
          toast.error('Error inesperado al asignar el rol.');
        },
      },
    );
  }

  const isPending = asignar.isPending;

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
        'border-dashed border-primary/40 bg-primary/5',
      )}
      aria-label="Asignar rol en empresa"
    >
      <div className="grid grid-cols-1 gap-2 md:grid-cols-12">
        <Field
          label="Empresa"
          required
          error={form.formState.errors.empresaId?.message}
          className="md:col-span-6"
        >
          <Controller
            name="empresaId"
            control={form.control}
            render={({ field }) => (
              <Select
                value={field.value === '' ? EMPTY_SELECT : field.value}
                onValueChange={(v) =>
                  field.onChange(v === EMPTY_SELECT ? '' : v)
                }
                disabled={isPending}
              >
                <SelectTrigger>
                  <SelectValue placeholder="Selecciona empresa" />
                </SelectTrigger>
                <SelectContent>
                  {empresas.map((e) => (
                    <SelectItem key={e.id} value={e.id}>
                      <span className="font-mono text-xs">{e.rfc}</span>
                      <span className="ml-2 text-muted-foreground">
                        {e.razonSocial}
                      </span>
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          />
        </Field>

        <Field
          label="Rol"
          required
          error={form.formState.errors.rolId?.message}
          className="md:col-span-6"
        >
          <Controller
            name="rolId"
            control={form.control}
            render={({ field }) => (
              <Select
                value={field.value === '' ? EMPTY_SELECT : field.value}
                onValueChange={(v) =>
                  field.onChange(v === EMPTY_SELECT ? '' : v)
                }
                disabled={isPending}
              >
                <SelectTrigger>
                  <SelectValue placeholder="Selecciona rol" />
                </SelectTrigger>
                <SelectContent>
                  {roles.map((r) => (
                    <SelectItem key={r.id} value={r.id}>
                      <span className="font-mono text-xs">{r.codigo}</span>
                      <span className="ml-2 text-muted-foreground">
                        {r.nombre}
                      </span>
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
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
          <Check className="mr-1 h-4 w-4" />
          {isPending ? 'Guardando…' : 'Guardar'}
        </Button>
      </div>
    </form>
  );
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
