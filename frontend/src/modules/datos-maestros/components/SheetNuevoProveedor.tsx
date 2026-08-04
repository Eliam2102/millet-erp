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
  NuevoProveedorContext,
  type NuevoProveedorApi,
} from '@/modules/datos-maestros/components/nuevo-proveedor-context';
import {
  CrearProveedorSchema,
  type CrearProveedorValues,
} from '@/modules/datos-maestros/schemas/proveedor';
import { useCrearProveedor } from '@/modules/datos-maestros/api';
import { TipoPersonaProveedor } from '@/modules/datos-maestros/api/types';

/**
 * <c>&lt;NuevoProveedorProvider/&gt;</c> + Sheet asociado (P4 del
 * patrón cross-módulo). Slide-from-right max-w-2xl con form de alta
 * de proveedor.
 *
 * <para><b>Confirm al cerrar con isDirty</b>: imita
 * <c>NuevaEmpresaProvider</c>. Apertura siempre vía
 * <c>useNuevoProveedor().abrir()</c>.</para>
 *
 * <para><b>Post-submit</b>: invalida la lista, toast con clave, navega
 * a <c>/admin/datos-maestros/proveedores/$id</c> y cierra el Sheet
 * con <c>force: true</c>.</para>
 */
export function NuevoProveedorProvider({ children }: { children: ReactNode }) {
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
      'Tienes cambios sin guardar en el nuevo proveedor. ¿Descartarlos y cerrar?',
    );
    if (confirmar) {
      isDirtyRef.current = false;
      setAbierto(false);
    }
  }, []);

  const api = useMemo<NuevoProveedorApi>(
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
    <NuevoProveedorContext.Provider value={api}>
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
            <SheetTitle>Nuevo proveedor</SheetTitle>
            <SheetDescription>
              Alta del proveedor en el catálogo cross-empresa. Datos
              adicionales (condiciones de pago, moneda, etc.) se
              ajustan después en el detalle.
            </SheetDescription>
          </SheetHeader>

          <div className="px-6 pb-6">
            {abierto && (
              <NuevoProveedorForm
                onClose={cerrarConConfirm}
                onDirtyChange={setDirty}
              />
            )}
          </div>
        </SheetContent>
      </Sheet>
    </NuevoProveedorContext.Provider>
  );
}

interface NuevoProveedorFormProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange: (dirty: boolean) => void;
}

function NuevoProveedorForm({
  onClose,
  onDirtyChange,
}: NuevoProveedorFormProps) {
  const navigate = useNavigate();
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearProveedor();

  const form = useForm<CrearProveedorValues>({
    resolver: zodResolver(CrearProveedorSchema),
    defaultValues: {
      clave: '',
      razonSocial: '',
      rfc: '',
      tipoPersona: TipoPersonaProveedor.Moral,
      nombreComercial: null,
      email: null,
      telefono: null,
      condicionesPagoDias: null,
    },
  });

  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  function onSubmit(values: CrearProveedorValues) {
    crear.mutate(
      {
        payload: {
          clave: values.clave,
          razonSocial: values.razonSocial,
          rfc: values.rfc,
          tipoPersona: values.tipoPersona,
          nombreComercial: values.nombreComercial,
          email: values.email,
          telefono: values.telefono,
          condicionesPagoDias: values.condicionesPagoDias,
          monedaPreferidaId: null,
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Proveedor ${resp.clave} creado`);
          form.reset(values);
          onClose({ force: true });
          navigate({
            to: '/admin/datos-maestros/proveedores/$id',
            params: { id: resp.id },
          });
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'PROVEEDOR_CLAVE_DUPLICADA') {
              form.setError('clave', {
                type: error.code,
                message: 'Ya existe un proveedor con esa clave.',
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
          toast.error('Error inesperado al crear el proveedor.');
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
        hint="Identificador corto. Inmutable después de creado."
        error={form.formState.errors.clave?.message}
      >
        <Input
          autoFocus
          maxLength={20}
          placeholder="P-001"
          className="font-mono"
          {...form.register('clave')}
        />
      </FormRow>

      <FormRow
        label="Razón social"
        required
        error={form.formState.errors.razonSocial?.message}
      >
        <Input
          maxLength={254}
          placeholder="Acme S.A. de C.V."
          {...form.register('razonSocial')}
        />
      </FormRow>

      <FormRow
        label="RFC"
        required
        hint="12 caracteres (persona moral) o 13 (persona física)."
        error={form.formState.errors.rfc?.message}
      >
        <Input
          maxLength={13}
          placeholder="ACM010101ABC"
          className="font-mono"
          {...form.register('rfc')}
        />
      </FormRow>

      <FormRow
        label="Tipo persona"
        required
        error={form.formState.errors.tipoPersona?.message}
      >
        <Controller
          name="tipoPersona"
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
                <SelectItem value={String(TipoPersonaProveedor.Moral)}>
                  Moral
                </SelectItem>
                <SelectItem value={String(TipoPersonaProveedor.Fisica)}>
                  Física
                </SelectItem>
              </SelectContent>
            </Select>
          )}
        />
      </FormRow>

      <FormRow
        label="Email"
        hint="Opcional."
        error={form.formState.errors.email?.message}
      >
        <Controller
          name="email"
          control={form.control}
          render={({ field }) => (
            <Input
              type="email"
              maxLength={254}
              placeholder="contacto@proveedor.com"
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
              placeholder="+52 55 1234 5678"
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
          {crear.isPending ? 'Creando…' : 'Crear proveedor'}
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
