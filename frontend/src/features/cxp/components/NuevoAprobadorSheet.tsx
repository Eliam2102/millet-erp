import { hoyLocalISO } from '@/lib/datetime';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { EmpleadoSelector } from '@/components/erp';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
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
  CrearAprobadorLimiteSchema,
  type CrearAprobadorLimiteValues,
} from '@/features/cxp/schemas/admin';
import { useCrearAprobadorLimite } from '@/features/cxp/api/useAdminCxp';
import {
  TipoGastoAprobador,
  TipoGastoAprobadorLabels,
} from '@/features/cxp/api/types';

/**
 * <c>&lt;NuevoAprobadorSheet/&gt;</c> — alta de aprobador con límite
 * para un tipo de gasto (Caja chica / Viáticos / TC / Otros sin OC).
 *
 * <para>PLATFORM-TODO(&lt;EmpleadoSelector&gt;): hoy el UUID de
 * empleado se pega a mano. Cuando se conecte el catálogo de empleados,
 * reemplazar Input por combobox.</para>
 */
export interface NuevoAprobadorSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

const TIPOS: readonly TipoGastoAprobador[] = [
  TipoGastoAprobador.ReembolsoCajaChica,
  TipoGastoAprobador.Viaticos,
  TipoGastoAprobador.TarjetaCreditoEmpresarial,
  TipoGastoAprobador.OtrosSinOc,
];

export function NuevoAprobadorSheet({
  open,
  onOpenChange,
}: NuevoAprobadorSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Nuevo aprobador con límite</SheetTitle>
          <SheetDescription>
            Asigna un monto máximo de autorización por tipo de gasto a
            un empleado, con vigencia opcional.
          </SheetDescription>
        </SheetHeader>
        {open && <Form onClose={() => onOpenChange(false)} />}
      </SheetContent>
    </Sheet>
  );
}

function Form({ onClose }: { onClose: () => void }) {
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearAprobadorLimite();
  const hoy = hoyLocalISO();

  const form = useForm<CrearAprobadorLimiteValues>({
    resolver: zodResolver(CrearAprobadorLimiteSchema),
    defaultValues: {
      empleadoId: '',
      tipoGasto: TipoGastoAprobador.ReembolsoCajaChica,
      montoMax: 0,
      moneda: 'MXN',
      vigenciaDesde: hoy,
      vigenciaHasta: null,
    },
  });

  function onSubmit(values: CrearAprobadorLimiteValues) {
    crear.mutate(
      { command: values, idempotencyKey },
      {
        onSuccess: () => {
          toast.success('Aprobador creado');
          onClose();
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                error,
              )
            )
              return;
            toast.error(error.problem.title);
            return;
          }
          toast.error('Error al crear aprobador.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
      <div className="space-y-1">
        <Label className="text-xs">
          Empleado <span className="text-destructive">*</span>
        </Label>
        <Controller
          control={form.control}
          name="empleadoId"
          render={({ field }) => (
            <EmpleadoSelector
              value={field.value || null}
              onChange={(id) => field.onChange(id ?? '')}
            />
          )}
        />
        {form.formState.errors.empleadoId && (
          <p className="text-xs text-destructive">
            {form.formState.errors.empleadoId.message}
          </p>
        )}
      </div>

      <div className="space-y-1">
        <Label className="text-xs">
          Tipo de gasto <span className="text-destructive">*</span>
        </Label>
        <Controller
          control={form.control}
          name="tipoGasto"
          render={({ field }) => (
            <Select
              value={String(field.value)}
              onValueChange={(v) => field.onChange(Number(v))}
            >
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {TIPOS.map((t) => (
                  <SelectItem key={t} value={String(t)}>
                    {TipoGastoAprobadorLabels[t]}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1">
          <Label className="text-xs">
            Monto máximo <span className="text-destructive">*</span>
          </Label>
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register('montoMax', { valueAsNumber: true })}
          />
          {form.formState.errors.montoMax && (
            <p className="text-xs text-destructive">
              {form.formState.errors.montoMax.message}
            </p>
          )}
        </div>
        <div className="space-y-1">
          <Label className="text-xs">
            Moneda <span className="text-destructive">*</span>
          </Label>
          <Input
            placeholder="MXN"
            maxLength={3}
            className="uppercase"
            {...form.register('moneda')}
          />
        </div>
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1">
          <Label className="text-xs">
            Vigencia desde <span className="text-destructive">*</span>
          </Label>
          <Input type="date" {...form.register('vigenciaDesde')} />
        </div>
        <div className="space-y-1">
          <Label className="text-xs">Vigencia hasta (opcional)</Label>
          <Input type="date" {...form.register('vigenciaHasta')} />
          {form.formState.errors.vigenciaHasta && (
            <p className="text-xs text-destructive">
              {form.formState.errors.vigenciaHasta.message}
            </p>
          )}
        </div>
      </div>

      <SheetFooter className="px-0">
        <Button
          type="button"
          variant="ghost"
          onClick={onClose}
          disabled={crear.isPending}
        >
          Cancelar
        </Button>
        <Button type="submit" disabled={crear.isPending}>
          {crear.isPending ? 'Creando…' : 'Crear aprobador'}
        </Button>
      </SheetFooter>
    </form>
  );
}
