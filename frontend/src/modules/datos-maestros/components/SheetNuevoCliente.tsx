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
  NuevoClienteContext,
  type NuevoClienteApi,
} from '@/modules/datos-maestros/components/nuevo-cliente-context';
import {
  CrearClienteSchema,
  type CrearClienteValues,
} from '@/modules/datos-maestros/schemas/cliente';
import { useCrearCliente } from '@/modules/datos-maestros/api';
import { RegimenFiscalSelector } from '@/components/erp/selectors/RegimenFiscalSelector';

/**
 * <c>&lt;NuevoClienteProvider/&gt;</c> + Sheet asociado (P4 del patrón
 * cross-módulo, ADR-0048). Slide-from-right max-w-2xl con el alta
 * manual de cliente (<c>origen = Manual</c>; la auto-provisión desde
 * A+W no pasa por aquí).
 *
 * <para><b>Confirm al cerrar con isDirty</b>: imita
 * <c>NuevoProveedorProvider</c>. Post-submit: invalida la lista, toast
 * con clave, navega a <c>/admin/datos-maestros/clientes/$id</c> y
 * cierra el Sheet con <c>force: true</c>. Los defaults fiscales (uso
 * CFDI, forma/método de pago, moneda) se completan en el detalle.</para>
 */
export function NuevoClienteProvider({ children }: { children: ReactNode }) {
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
      'Tienes cambios sin guardar en el nuevo cliente. ¿Descartarlos y cerrar?',
    );
    if (confirmar) {
      isDirtyRef.current = false;
      setAbierto(false);
    }
  }, []);

  const api = useMemo<NuevoClienteApi>(
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
    <NuevoClienteContext.Provider value={api}>
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
            <SheetTitle>Nuevo cliente</SheetTitle>
            <SheetDescription>
              Alta manual en el master de clientes. Los datos fiscales
              pueden completarse después en el detalle — sin ellos el
              cliente no puede timbrar (aparece en la bandeja «Fiscales
              incompletos»).
            </SheetDescription>
          </SheetHeader>

          <div className="px-6 pb-6">
            {abierto && (
              <NuevoClienteForm
                onClose={cerrarConConfirm}
                onDirtyChange={setDirty}
              />
            )}
          </div>
        </SheetContent>
      </Sheet>
    </NuevoClienteContext.Provider>
  );
}

interface NuevoClienteFormProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange: (dirty: boolean) => void;
}

function NuevoClienteForm({ onClose, onDirtyChange }: NuevoClienteFormProps) {
  const navigate = useNavigate();
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearCliente();

  const form = useForm<CrearClienteValues>({
    resolver: zodResolver(CrearClienteSchema),
    defaultValues: {
      clave: '',
      razonSocial: '',
      referenciaExterna: null,
      rfc: null,
      regimenFiscal: null,
      codigoPostalFiscal: null,
      email: null,
      telefono: null,
    },
  });

  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  function onSubmit(values: CrearClienteValues) {
    crear.mutate(
      {
        payload: {
          clave: values.clave,
          razonSocial: values.razonSocial,
          referenciaExterna: values.referenciaExterna,
          rfc: values.rfc,
          regimenFiscal: values.regimenFiscal,
          codigoPostalFiscal: values.codigoPostalFiscal,
          usoCfdiDefault: null,
          formaPagoDefault: null,
          metodoPagoDefault: null,
          // null → el backend aplica el default MXN.
          monedaDefault: null,
          esGenerico: null,
          email: values.email,
          telefono: values.telefono,
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Cliente ${resp.clave} creado`);
          form.reset(values);
          onClose({ force: true });
          navigate({
            to: '/admin/datos-maestros/clientes/$id',
            params: { id: resp.id },
          });
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'CLIENTE_CLAVE_DUPLICADA') {
              form.setError('clave', {
                type: error.code,
                message: 'Ya existe un cliente con esa clave.',
              });
              return;
            }
            if (error.code === 'CLIENTE_REFERENCIA_DUPLICADA') {
              form.setError('referenciaExterna', {
                type: error.code,
                message: 'Ya existe un cliente con esa referencia externa.',
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
          toast.error('Error inesperado al crear el cliente.');
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
          placeholder="C-001"
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
        label="Referencia externa"
        hint="Opcional. Código del cliente en A+W (correlación). Inmutable."
        error={form.formState.errors.referenciaExterna?.message}
      >
        <Controller
          name="referenciaExterna"
          control={form.control}
          render={({ field }) => (
            <Input
              maxLength={50}
              placeholder="AW-12345"
              className="font-mono"
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
        label="RFC"
        hint="Opcional aquí; requerido para timbrar."
        error={form.formState.errors.rfc?.message}
      >
        <Controller
          name="rfc"
          control={form.control}
          render={({ field }) => (
            <Input
              maxLength={13}
              placeholder="ACM010101ABC"
              className="font-mono"
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

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <FormRow
          label="Régimen fiscal"
          hint="Catálogo SAT c_RegimenFiscal."
          error={form.formState.errors.regimenFiscal?.message}
        >
          <Controller
            name="regimenFiscal"
            control={form.control}
            render={({ field }) => (
              <RegimenFiscalSelector
                value={field.value}
                onChange={field.onChange}
              />
            )}
          />
        </FormRow>

        <FormRow
          label="Código postal fiscal"
          hint="5 dígitos."
          error={form.formState.errors.codigoPostalFiscal?.message}
        >
          <Controller
            name="codigoPostalFiscal"
            control={form.control}
            render={({ field }) => (
              <Input
                maxLength={5}
                inputMode="numeric"
                placeholder="76100"
                className="font-mono"
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
      </div>

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
              placeholder="contacto@cliente.com"
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
          {crear.isPending ? 'Creando…' : 'Crear cliente'}
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
