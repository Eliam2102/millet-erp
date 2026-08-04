import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
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
  VehiculoSchema,
  type VehiculoValues,
} from '@/features/facturacion/schemas/carta-porte-catalogos';
import {
  useActualizarVehiculo,
  useCrearVehiculo,
} from '@/features/facturacion/api/cartaPorteCatalogos';
import type { VehiculoListItem } from '@/features/facturacion/api/types';
import { cn } from '@/lib/utils';

/**
 * Form inline (sin modal) para AGREGAR o EDITAR un vehículo del catálogo
 * de Carta Porte. Mismo patrón que <c>CanalVentaInlineForm</c>
 * (memoria <c>feedback_inline_no_modal_para_items</c>).
 *
 * <para><b>Modo agregar</b> (sin <c>vehiculo</c>): border dashed primary.
 * <b>Modo editar</b> (con <c>vehiculo</c>): border solid amber; la PLACA
 * es inmutable (identidad operativa del vehículo — el input queda
 * deshabilitado).</para>
 */
export interface VehiculoInlineFormProps {
  vehiculo?: VehiculoListItem | null;
  onCancel: () => void;
  onSaved?: () => void;
}

const VALORES_INICIALES: VehiculoValues = {
  placa: '',
  configVehicular: '',
  anioModelo: new Date().getFullYear(),
  pesoBrutoVehicular: null,
  tipoPermisoSct: '',
  numPermisoSct: '',
  aseguradora: '',
  polizaSeguro: '',
};

const numeroONull = (v: unknown): number | null => {
  if (v === '' || v == null) return null;
  const n = Number(v);
  return Number.isNaN(n) ? null : n;
};

const oNull = (v: string | undefined): string | null =>
  v == null || v.trim() === '' ? null : v.trim();

export function VehiculoInlineForm({
  vehiculo,
  onCancel,
  onSaved,
}: VehiculoInlineFormProps) {
  const esEditar = vehiculo != null;
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearVehiculo();
  const actualizar = useActualizarVehiculo();

  const form = useForm<VehiculoValues>({
    resolver: zodResolver(VehiculoSchema),
    defaultValues: esEditar
      ? {
          placa: vehiculo.placa,
          configVehicular: vehiculo.configVehicular,
          anioModelo: vehiculo.anioModelo,
          pesoBrutoVehicular: vehiculo.pesoBrutoVehicular,
          tipoPermisoSct: vehiculo.tipoPermisoSct ?? '',
          numPermisoSct: vehiculo.numPermisoSct ?? '',
          aseguradora: vehiculo.aseguradora ?? '',
          polizaSeguro: vehiculo.polizaSeguro ?? '',
        }
      : VALORES_INICIALES,
  });

  useEffect(() => {
    form.setFocus(esEditar ? 'configVehicular' : 'placa');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const isPending = crear.isPending || actualizar.isPending;

  function onError(error: Error) {
    if (esApiError(error)) {
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
    toast.error('Error inesperado al guardar el vehículo.');
  }

  function onSubmit(values: VehiculoValues) {
    const datos = {
      configVehicular: values.configVehicular.toUpperCase(),
      anioModelo: values.anioModelo,
      tipoPermisoSct: oNull(values.tipoPermisoSct)?.toUpperCase() ?? null,
      numPermisoSct: oNull(values.numPermisoSct),
      aseguradora: oNull(values.aseguradora),
      polizaSeguro: oNull(values.polizaSeguro),
      pesoBrutoVehicular: values.pesoBrutoVehicular,
    };
    if (esEditar && vehiculo != null) {
      actualizar.mutate(
        { id: vehiculo.id, payload: datos, idempotencyKey },
        {
          onSuccess: () => {
            toast.success(`Vehículo ${vehiculo.placa} actualizado`);
            onSaved?.();
          },
          onError,
        },
      );
      return;
    }
    crear.mutate(
      {
        command: { placa: values.placa.toUpperCase(), ...datos },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success(`Vehículo ${values.placa.toUpperCase()} agregado`);
          form.reset(VALORES_INICIALES);
          form.setFocus('placa');
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
        esEditar ? `Editar vehículo ${vehiculo?.placa}` : 'Agregar vehículo'
      }
    >
      <div className="grid grid-cols-1 gap-2 md:grid-cols-12">
        <Field
          label="Placa"
          required
          error={form.formState.errors.placa?.message}
          className="md:col-span-3"
        >
          <Input
            className="font-mono uppercase"
            maxLength={20}
            placeholder="ABC-123"
            disabled={esEditar}
            title={esEditar ? 'La placa es inmutable (identidad del vehículo).' : undefined}
            {...form.register('placa')}
          />
        </Field>
        <Field
          label="Config. vehicular"
          required
          error={form.formState.errors.configVehicular?.message}
          hint="c_ConfigAutotransporte, ej. C2R2"
          className="md:col-span-3"
        >
          <Input
            className="uppercase"
            maxLength={10}
            placeholder="C2R2"
            {...form.register('configVehicular')}
          />
        </Field>
        <Field
          label="Año modelo"
          required
          error={form.formState.errors.anioModelo?.message}
          className="md:col-span-3"
        >
          <Input
            type="number"
            min={1990}
            max={2100}
            step={1}
            {...form.register('anioModelo', { valueAsNumber: true })}
          />
        </Field>
        <Field
          label="Peso bruto vehicular (ton)"
          error={form.formState.errors.pesoBrutoVehicular?.message}
          hint="Obligatorio para timbrar Carta Porte 3.1"
          className="md:col-span-3"
        >
          <Input
            type="number"
            step="0.001"
            min="0"
            placeholder="17.5"
            {...form.register('pesoBrutoVehicular', { setValueAs: numeroONull })}
          />
        </Field>

        <Field
          label="Permiso SCT"
          error={form.formState.errors.tipoPermisoSct?.message}
          hint="c_TipoPermiso, ej. TPAF01"
          className="md:col-span-3"
        >
          <Input
            className="uppercase"
            maxLength={10}
            placeholder="TPAF01"
            {...form.register('tipoPermisoSct')}
          />
        </Field>
        <Field
          label="Núm. permiso SCT"
          error={form.formState.errors.numPermisoSct?.message}
          className="md:col-span-3"
        >
          <Input maxLength={50} {...form.register('numPermisoSct')} />
        </Field>
        <Field
          label="Aseguradora"
          error={form.formState.errors.aseguradora?.message}
          className="md:col-span-3"
        >
          <Input maxLength={100} {...form.register('aseguradora')} />
        </Field>
        <Field
          label="Póliza de seguro"
          error={form.formState.errors.polizaSeguro?.message}
          className="md:col-span-3"
        >
          <Input maxLength={50} {...form.register('polizaSeguro')} />
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
              {isPending ? 'Agregando…' : 'Agregar vehículo'}
            </>
          )}
        </Button>
      </div>
    </form>
  );
}

interface FieldProps {
  label: string;
  required?: boolean;
  error?: string;
  hint?: string;
  className?: string;
  children: React.ReactNode;
}

function Field({ label, required, error, hint, className, children }: FieldProps) {
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
      {hint != null && (
        <p className="text-[11px] leading-tight text-muted-foreground">{hint}</p>
      )}
      {error != null && (
        <p role="alert" className="text-xs text-rose-600">
          {error}
        </p>
      )}
    </div>
  );
}
