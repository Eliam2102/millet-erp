import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
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
  NuevaSerieContext,
  type NuevaSerieApi,
} from '@/modules/administracion/components/nueva-serie-context';
import {
  CrearSerieSchema,
  type CrearSerieValues,
} from '@/modules/administracion/schemas/serie';
import {
  useCrearSerie,
  useEmpresa,
  useEmpresas,
} from '@/modules/administracion/api';
import {
  ReinicioPeriodo,
  TipoDocumentoSerie,
} from '@/modules/administracion/api/types';
import {
  REINICIO_PERIODO_DESCRIPCION,
  REINICIO_PERIODO_LABEL,
  TIPO_DOCUMENTO_LABEL,
  previewFolioAproximado,
} from '@/modules/administracion/components/series-labels';
import { cn } from '@/lib/utils';

const SUCURSAL_NINGUNA = '__ninguna__';

const TIPO_DOC_VALUES: readonly TipoDocumentoSerie[] = [
  TipoDocumentoSerie.OrdenCompra,
  TipoDocumentoSerie.Cfdi,
  TipoDocumentoSerie.NotaCredito,
  TipoDocumentoSerie.Poliza,
  TipoDocumentoSerie.FacturaAnticipo,
];

const REINICIO_OPTIONS: readonly ReinicioPeriodo[] = [
  ReinicioPeriodo.None,
  ReinicioPeriodo.Anual,
  ReinicioPeriodo.Mensual,
];

/**
 * <c>&lt;NuevaSerieProvider/&gt;</c> + Sheet asociado (P4 del patrón
 * cross-módulo). Slide-from-right con form de alta de serie. Mismo
 * patrón que <c>NuevaEmpresaProvider</c>: confirm al cerrar dirty,
 * <c>force: true</c> tras success.
 *
 * <para>El form expone Empresa, Sucursal (opcional, dependiente de la
 * Empresa elegida via <c>useEmpresa</c>), Tipo de Documento, Prefijo,
 * Sufijo y Reinicio de Periodo (radios "Eterno" / "Anual" /
 * "Mensual"). El preview del próximo folio se aproxima client-side
 * porque el backend solo lo calcula post-creación.</para>
 */
export function NuevaSerieProvider({ children }: { children: ReactNode }) {
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
      'Tienes cambios sin guardar en la nueva serie. ¿Descartarlos y cerrar?',
    );
    if (confirmar) {
      isDirtyRef.current = false;
      setAbierto(false);
    }
  }, []);

  const api = useMemo<NuevaSerieApi>(
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
    <NuevaSerieContext.Provider value={api}>
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
            <SheetTitle>Nueva serie</SheetTitle>
            <SheetDescription>
              Configura una serie de folios para una empresa y tipo de
              documento. La sucursal es opcional (cross-sucursal si se deja
              vacío).
            </SheetDescription>
          </SheetHeader>

          <div className="px-6 pb-6">
            {abierto && (
              <NuevaSerieForm
                onClose={cerrarConConfirm}
                onDirtyChange={setDirty}
              />
            )}
          </div>
        </SheetContent>
      </Sheet>
    </NuevaSerieContext.Provider>
  );
}

interface NuevaSerieFormProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange: (dirty: boolean) => void;
}

function NuevaSerieForm({ onClose, onDirtyChange }: NuevaSerieFormProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearSerie();
  const empresasQuery = useEmpresas({ limit: 200 });
  const empresas = empresasQuery.data?.items ?? [];

  const form = useForm<CrearSerieValues>({
    resolver: zodResolver(CrearSerieSchema),
    defaultValues: {
      empresaId: '',
      sucursalId: null,
      tipoDocumento: TipoDocumentoSerie.OrdenCompra,
      prefijo: '',
      sufijo: null,
      reinicioPeriodo: ReinicioPeriodo.Anual,
    },
  });

  // useWatch (estable, suscripción granular). form.watch dispara el
  // lint react-hooks/incompatible-library.
  const empresaId = useWatch({ control: form.control, name: 'empresaId' });
  const prefijo = useWatch({ control: form.control, name: 'prefijo' });
  const sufijo = useWatch({ control: form.control, name: 'sufijo' });
  const reinicio = useWatch({
    control: form.control,
    name: 'reinicioPeriodo',
  });

  // Cargar sucursales solo cuando hay empresa seleccionada (el endpoint
  // de detalle de empresa retorna las sucursales, evitando un nuevo
  // hook server-side dedicado).
  const empresaDetalle = useEmpresa(
    empresaId.length > 0 ? empresaId : null,
  );
  const sucursales = empresaDetalle.data?.sucursales ?? [];

  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  // Si cambia la empresa, resetea sucursalId a null.
  useEffect(() => {
    form.setValue('sucursalId', null, { shouldDirty: false });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [empresaId]);
  const previewFolio =
    prefijo.length > 0
      ? previewFolioAproximado(prefijo, sufijo, reinicio)
      : '—';

  function onSubmit(values: CrearSerieValues) {
    crear.mutate(
      {
        command: {
          empresaId: values.empresaId,
          sucursalId: values.sucursalId,
          tipoDocumento: values.tipoDocumento,
          prefijo: values.prefijo,
          sufijo: values.sufijo,
          reinicioPeriodo: values.reinicioPeriodo,
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Serie ${resp.prefijo} creada`, {
            description: TIPO_DOCUMENTO_LABEL[resp.tipoDocumento],
          });
          form.reset(values);
          onClose({ force: true });
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'SERIE_DUPLICADA') {
              toast.error('Ya existe una serie con esos datos.');
              return;
            }
            if (error.code === 'EMPRESA_NO_ENCONTRADA') {
              form.setError('empresaId', {
                type: error.code,
                message: 'La empresa seleccionada no existe.',
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
          toast.error('Error inesperado al crear la serie.');
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
        label="Empresa"
        required
        error={form.formState.errors.empresaId?.message}
      >
        <Controller
          name="empresaId"
          control={form.control}
          render={({ field }) => (
            <Select
              value={field.value.length > 0 ? field.value : undefined}
              onValueChange={(v) => field.onChange(v)}
            >
              <SelectTrigger>
                <SelectValue
                  placeholder={
                    empresasQuery.isLoading
                      ? 'Cargando empresas…'
                      : 'Selecciona la empresa'
                  }
                />
              </SelectTrigger>
              <SelectContent>
                {empresas.map((e) => (
                  <SelectItem key={e.id} value={e.id}>
                    <span className="font-mono text-xs">{e.rfc}</span> —{' '}
                    {e.razonSocial}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
      </FormRow>

      <FormRow
        label="Sucursal"
        hint={
          empresaId.length === 0
            ? 'Selecciona primero una empresa.'
            : 'Opcional. Sin sucursal aplica a todas (cross-sucursal).'
        }
        error={form.formState.errors.sucursalId?.message}
      >
        <Controller
          name="sucursalId"
          control={form.control}
          render={({ field }) => (
            <Select
              value={field.value ?? SUCURSAL_NINGUNA}
              onValueChange={(v) =>
                field.onChange(v === SUCURSAL_NINGUNA ? null : v)
              }
              disabled={empresaId.length === 0 || empresaDetalle.isLoading}
            >
              <SelectTrigger>
                <SelectValue placeholder="Todas las sucursales" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={SUCURSAL_NINGUNA}>
                  Todas las sucursales
                </SelectItem>
                {sucursales.map((s) => (
                  <SelectItem key={s.id} value={s.id}>
                    <span className="font-mono text-xs">{s.clave}</span> —{' '}
                    {s.nombre}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
      </FormRow>

      <FormRow
        label="Tipo de documento"
        required
        error={form.formState.errors.tipoDocumento?.message}
      >
        <Controller
          name="tipoDocumento"
          control={form.control}
          render={({ field }) => (
            <Select
              value={String(field.value)}
              onValueChange={(v) =>
                field.onChange(Number(v) as TipoDocumentoSerie)
              }
            >
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {TIPO_DOC_VALUES.map((v) => (
                  <SelectItem key={v} value={String(v)}>
                    {TIPO_DOCUMENTO_LABEL[v]}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
      </FormRow>

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        <FormRow
          label="Prefijo"
          required
          hint="1-10 caracteres, ej. OC, FAC, NC."
          error={form.formState.errors.prefijo?.message}
        >
          <Input
            maxLength={10}
            placeholder="OC"
            {...form.register('prefijo')}
          />
        </FormRow>

        <FormRow
          label="Sufijo"
          hint="Opcional, hasta 10 caracteres."
          error={form.formState.errors.sufijo?.message}
        >
          <Input
            maxLength={10}
            placeholder=""
            {...form.register('sufijo', {
              setValueAs: (v: unknown) =>
                typeof v === 'string' && v.trim().length === 0
                  ? null
                  : typeof v === 'string'
                    ? v.trim()
                    : v,
            })}
          />
        </FormRow>
      </div>

      <FormRow
        label="Reinicio de periodo"
        required
        error={form.formState.errors.reinicioPeriodo?.message}
      >
        <Controller
          name="reinicioPeriodo"
          control={form.control}
          render={({ field }) => (
            <div
              role="radiogroup"
              aria-label="Reinicio de periodo"
              className="space-y-2"
            >
              {REINICIO_OPTIONS.map((value) => {
                const checked = field.value === value;
                return (
                  <label
                    key={value}
                    className={cn(
                      'flex cursor-pointer items-start gap-3 rounded-md border p-3 text-sm',
                      checked
                        ? 'border-primary bg-primary/5'
                        : 'border-input bg-background hover:bg-muted/40',
                    )}
                  >
                    <input
                      type="radio"
                      className="mt-0.5"
                      checked={checked}
                      onChange={() => field.onChange(value)}
                    />
                    <div className="flex-1">
                      <p className="font-medium">
                        {REINICIO_PERIODO_LABEL[value]}
                      </p>
                      <p className="text-xs text-muted-foreground">
                        {REINICIO_PERIODO_DESCRIPCION[value]}
                      </p>
                    </div>
                  </label>
                );
              })}
            </div>
          )}
        />
      </FormRow>

      <div className="rounded-md border bg-muted/30 p-3 text-xs">
        <span className="text-muted-foreground">Vista previa aproximada:</span>{' '}
        <span className="font-mono">{previewFolio}</span>
        <p className="mt-1 text-muted-foreground">
          El backend confirmará el folio exacto al crear la serie.
        </p>
      </div>

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
          {crear.isPending ? 'Creando…' : 'Crear serie'}
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
