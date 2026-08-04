import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
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
  CrearPoliticaViaticosSchema,
  type CrearPoliticaViaticosValues,
} from '@/features/cxp/schemas/admin';
import { PuestoSelector } from '@/components/erp';
import { useCrearPoliticaViaticos } from '@/features/cxp/api/useAdminCxp';
import {
  TipoDestinoViatico,
  TipoDestinoViaticoLabels,
} from '@/features/cxp/api/types';

/**
 * <c>&lt;NuevaPoliticaViaticosSheet/&gt;</c> — alta de política
 * puesto + destino (Nacional/Internacional) con tope diario y días
 * máximos.
 *
 * <para>PLATFORM-TODO(&lt;PuestoSelector&gt;): hoy el UUID de puesto
 * se pega a mano. Cuando se conecte el catálogo de puestos
 * (PLATFORM-TODO backend <c>PuestosEnAdmin</c>), reemplazar Input por
 * combobox.</para>
 */
export interface NuevaPoliticaViaticosSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function NuevaPoliticaViaticosSheet({
  open,
  onOpenChange,
}: NuevaPoliticaViaticosSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Nueva política de viáticos</SheetTitle>
          <SheetDescription>
            Tope por puesto y destino (Nacional / Internacional). El
            backend valida contra esta política al solicitar viáticos.
          </SheetDescription>
        </SheetHeader>
        {open && <Form onClose={() => onOpenChange(false)} />}
      </SheetContent>
    </Sheet>
  );
}

function Form({ onClose }: { onClose: () => void }) {
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearPoliticaViaticos();

  const form = useForm<CrearPoliticaViaticosValues>({
    resolver: zodResolver(CrearPoliticaViaticosSchema),
    defaultValues: {
      puestoId: '',
      tipoDestino: TipoDestinoViatico.Nacional,
      montoMaxDia: 0,
      diasMax: 1,
      moneda: 'MXN',
    },
  });

  function onSubmit(values: CrearPoliticaViaticosValues) {
    crear.mutate(
      { command: values, idempotencyKey },
      {
        onSuccess: () => {
          toast.success('Política creada');
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
          toast.error('Error al crear política.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
      <div className="space-y-1">
        <Label className="text-xs">
          Puesto <span className="text-destructive">*</span>
        </Label>
        <Controller
          control={form.control}
          name="puestoId"
          render={({ field }) => (
            <PuestoSelector
              value={field.value || null}
              onChange={(id) => field.onChange(id ?? '')}
            />
          )}
        />
        {form.formState.errors.puestoId && (
          <p className="text-xs text-destructive">
            {form.formState.errors.puestoId.message}
          </p>
        )}
      </div>

      <div className="space-y-1">
        <Label className="text-xs">
          Tipo de destino <span className="text-destructive">*</span>
        </Label>
        <Controller
          control={form.control}
          name="tipoDestino"
          render={({ field }) => (
            <Select
              value={String(field.value)}
              onValueChange={(v) => field.onChange(Number(v))}
            >
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={String(TipoDestinoViatico.Nacional)}>
                  {TipoDestinoViaticoLabels[TipoDestinoViatico.Nacional]}
                </SelectItem>
                <SelectItem value={String(TipoDestinoViatico.Internacional)}>
                  {TipoDestinoViaticoLabels[TipoDestinoViatico.Internacional]}
                </SelectItem>
              </SelectContent>
            </Select>
          )}
        />
      </div>

      <div className="grid grid-cols-3 gap-3">
        <div className="space-y-1">
          <Label className="text-xs">
            Monto/día <span className="text-destructive">*</span>
          </Label>
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register('montoMaxDia', { valueAsNumber: true })}
          />
        </div>
        <div className="space-y-1">
          <Label className="text-xs">
            Días máx. <span className="text-destructive">*</span>
          </Label>
          <Input
            type="number"
            min="1"
            {...form.register('diasMax', { valueAsNumber: true })}
          />
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
          {crear.isPending ? 'Creando…' : 'Crear política'}
        </Button>
      </SheetFooter>
    </form>
  );
}
