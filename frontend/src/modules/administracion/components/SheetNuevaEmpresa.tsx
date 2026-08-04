import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
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
  NuevaEmpresaContext,
  type NuevaEmpresaApi,
} from '@/modules/administracion/components/nueva-empresa-context';
import {
  CrearEmpresaSchema,
  type CrearEmpresaValues,
} from '@/modules/administracion/schemas/empresa';
import { useCrearEmpresa } from '@/modules/administracion/api';

/**
 * <c>&lt;NuevaEmpresaProvider/&gt;</c> + Sheet asociado (P4 del patrón
 * cross-módulo). Slide-from-right max-w-2xl con form completo de alta
 * de empresa (RFC + Razón social + Régimen fiscal + Nombre comercial).
 *
 * <para><b>Confirm al cerrar con isDirty</b>: imita
 * <c>NuevaRequisicionProvider</c>. La key (Esc, click fuera, X interno)
 * solicita confirmación; la apertura siempre la dispara el caller via
 * <c>useNuevaEmpresa().abrir()</c>.</para>
 *
 * <para><b>Post-submit</b>: invalida la lista, toast con RFC, navega a
 * <c>/admin/empresas/$id</c> y cierra el Sheet con <c>force: true</c>.</para>
 */
export function NuevaEmpresaProvider({ children }: { children: ReactNode }) {
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
      'Tienes cambios sin guardar en la nueva empresa. ¿Descartarlos y cerrar?',
    );
    if (confirmar) {
      isDirtyRef.current = false;
      setAbierto(false);
    }
  }, []);

  const api = useMemo<NuevaEmpresaApi>(
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
    <NuevaEmpresaContext.Provider value={api}>
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
            <SheetTitle>Nueva empresa</SheetTitle>
            <SheetDescription>
              Alta de razón social. Las sucursales y departamentos se
              gestionan después en el detalle.
            </SheetDescription>
          </SheetHeader>

          <div className="px-6 pb-6">
            {abierto && (
              <NuevaEmpresaForm
                onClose={cerrarConConfirm}
                onDirtyChange={setDirty}
              />
            )}
          </div>
        </SheetContent>
      </Sheet>
    </NuevaEmpresaContext.Provider>
  );
}

interface NuevaEmpresaFormProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange: (dirty: boolean) => void;
}

function NuevaEmpresaForm({ onClose, onDirtyChange }: NuevaEmpresaFormProps) {
  const navigate = useNavigate();
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearEmpresa();

  const form = useForm<CrearEmpresaValues>({
    resolver: zodResolver(CrearEmpresaSchema),
    defaultValues: {
      rfc: '',
      razonSocial: '',
      regimenFiscal: '',
      nombreComercial: null,
    },
  });

  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  function onSubmit(values: CrearEmpresaValues) {
    crear.mutate(
      {
        command: {
          rfc: values.rfc,
          razonSocial: values.razonSocial,
          regimenFiscal: values.regimenFiscal,
          nombreComercial: values.nombreComercial,
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Empresa ${resp.rfc} creada`, {
            description: resp.razonSocial,
          });
          form.reset(values);
          onClose({ force: true });
          navigate({
            to: '/admin/empresas/$id',
            params: { id: resp.id },
          });
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'EMPRESA_RFC_DUPLICADO') {
              form.setError('rfc', {
                type: error.code,
                message: 'Ya existe una empresa con ese RFC.',
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
          toast.error('Error inesperado al crear la empresa.');
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
        label="RFC"
        required
        hint="12 caracteres (persona moral) o 13 (persona física)."
        error={form.formState.errors.rfc?.message}
      >
        <Input
          autoFocus
          maxLength={13}
          placeholder="MIL010101ABC"
          {...form.register('rfc')}
        />
      </FormRow>

      <FormRow
        label="Razón social"
        required
        error={form.formState.errors.razonSocial?.message}
      >
        <Input
          maxLength={254}
          placeholder="Millet S.A. de C.V."
          {...form.register('razonSocial')}
        />
      </FormRow>

      <FormRow
        label="Régimen fiscal"
        required
        hint="Código SAT, máx. 10 caracteres (ej. 601, 603, 612)."
        error={form.formState.errors.regimenFiscal?.message}
      >
        <Input
          maxLength={10}
          placeholder="601"
          {...form.register('regimenFiscal')}
        />
      </FormRow>

      <FormRow
        label="Nombre comercial"
        hint="Opcional. Si aplica, el alias con el que opera la empresa."
        error={form.formState.errors.nombreComercial?.message}
      >
        <Controller
          name="nombreComercial"
          control={form.control}
          render={({ field }) => (
            <Input
              maxLength={254}
              placeholder="Millet"
              value={field.value ?? ''}
              onChange={(e) =>
                field.onChange(e.target.value.length > 0 ? e.target.value : null)
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
          {crear.isPending ? 'Creando…' : 'Crear empresa'}
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
