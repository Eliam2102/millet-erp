import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
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
import { useCrearUnidadMedida } from '@/modules/catalogos/api';
import {
  DimensionUnidad,
  type CrearUnidadMedidaPayload,
} from '@/modules/catalogos/api/types';
import {
  CrearUnidadMedidaSchema,
  type CrearUnidadMedidaValues,
} from '@/modules/catalogos/schemas/unidad-medida';
import {
  NuevaUnidadMedidaContext,
  type NuevaUnidadMedidaApi,
} from '@/modules/catalogos/components/nueva-unidad-medida-context';
import { DimensionSelect, FormRow } from './unidad-medida-fields';

export function NuevaUnidadMedidaProvider({ children }: { children: ReactNode }) {
  const [abierto, setAbierto] = useState(false);
  const isDirtyRef = useRef(false);
  const setDirty = useCallback((d: boolean) => {
    isDirtyRef.current = d;
  }, []);
  const cerrar = useCallback((opts?: { force?: boolean }) => {
    if (opts?.force === true || !isDirtyRef.current) {
      isDirtyRef.current = false;
      setAbierto(false);
      return;
    }
    if (window.confirm('Tienes cambios sin guardar. ¿Descartarlos y cerrar?')) {
      isDirtyRef.current = false;
      setAbierto(false);
    }
  }, []);
  const api = useMemo<NuevaUnidadMedidaApi>(
    () => ({
      abrir: () => {
        isDirtyRef.current = false;
        setAbierto(true);
      },
      cerrar,
      setDirty,
    }),
    [cerrar, setDirty],
  );
  return (
    <NuevaUnidadMedidaContext.Provider value={api}>
      {children}
      <Sheet
        open={abierto}
        onOpenChange={(open) => (open ? setAbierto(true) : cerrar())}
      >
        <SheetContent side="right" className="w-full overflow-y-auto sm:max-w-2xl">
          <SheetHeader>
            <SheetTitle>Nueva unidad de medida</SheetTitle>
            <SheetDescription>
              Alta en el catálogo cross-empresa de unidades de medida. El
              código es inmutable. El factor a la unidad base define la
              conversión dentro de la dimensión.
            </SheetDescription>
          </SheetHeader>
          <div className="px-6 pb-6">
            {abierto && (
              <NuevaUnidadMedidaForm onClose={cerrar} onDirtyChange={setDirty} />
            )}
          </div>
        </SheetContent>
      </Sheet>
    </NuevaUnidadMedidaContext.Provider>
  );
}

function NuevaUnidadMedidaForm({
  onClose,
  onDirtyChange,
}: {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange: (d: boolean) => void;
}) {
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearUnidadMedida();
  const form = useForm<CrearUnidadMedidaValues>({
    resolver: zodResolver(CrearUnidadMedidaSchema),
    defaultValues: {
      codigo: '',
      nombre: '',
      dimension: DimensionUnidad.Conteo,
      factorABase: 1,
      decimales: 0,
      esBase: false,
    },
  });
  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  function onSubmit(values: CrearUnidadMedidaValues) {
    const payload: CrearUnidadMedidaPayload = {
      ...values,
      dimension: values.dimension as DimensionUnidad,
    };
    crear.mutate(
      { payload, idempotencyKey },
      {
        onSuccess: () => {
          toast.success(`Unidad ${values.codigo} creada`);
          form.reset(values);
          onClose({ force: true });
        },
        onError: (err) => {
          if (esApiError(err)) {
            if (err.code === 'UNIDAD_MEDIDA_CODIGO_DUPLICADO') {
              form.setError('codigo', {
                type: err.code,
                message: 'Ya existe una unidad con ese código.',
              });
              return;
            }
            if (
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                err,
              )
            )
              return;
            toast.error(err.problem.title);
            return;
          }
          toast.error('Error al crear la unidad de medida.');
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
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        <FormRow
          label="Código"
          required
          hint="Hasta 20 caracteres (ej. PZA, KG, L). Inmutable."
          error={form.formState.errors.codigo?.message}
        >
          <Input
            autoFocus
            maxLength={20}
            placeholder="PZA"
            className="font-mono uppercase"
            {...form.register('codigo')}
          />
        </FormRow>
        <FormRow
          label="Nombre"
          required
          error={form.formState.errors.nombre?.message}
        >
          <Input maxLength={100} placeholder="Pieza" {...form.register('nombre')} />
        </FormRow>
        <FormRow
          label="Dimensión"
          required
          hint="No se podrá cambiar una vez que la unidad esté en uso."
          error={form.formState.errors.dimension?.message}
        >
          <Controller
            name="dimension"
            control={form.control}
            render={({ field }) => (
              <DimensionSelect
                value={field.value}
                onChange={field.onChange}
              />
            )}
          />
        </FormRow>
        <FormRow
          label="Factor a la base"
          required
          hint="1 si es la unidad base; ej. G = 0.001 (1 g = 0.001 kg)."
          error={form.formState.errors.factorABase?.message}
        >
          <Input
            type="number"
            inputMode="decimal"
            step="any"
            min={0}
            {...form.register('factorABase', { valueAsNumber: true })}
          />
        </FormRow>
        <FormRow
          label="Decimales permitidos"
          required
          hint="0 = discreta (no admite fracciones)."
          error={form.formState.errors.decimales?.message}
        >
          <Input
            type="number"
            inputMode="numeric"
            step={1}
            min={0}
            max={6}
            {...form.register('decimales', { valueAsNumber: true })}
          />
        </FormRow>
        <FormRow label="Es unidad base">
          <Controller
            name="esBase"
            control={form.control}
            render={({ field }) => (
              <label className="inline-flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  className="h-4 w-4 rounded border-input"
                  checked={field.value}
                  onChange={(e) => field.onChange(e.target.checked)}
                />
                <span>{field.value ? 'Base de su dimensión' : 'Unidad derivada'}</span>
              </label>
            )}
          />
        </FormRow>
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
          {crear.isPending ? 'Creando…' : 'Crear unidad'}
        </Button>
      </div>
    </form>
  );
}
