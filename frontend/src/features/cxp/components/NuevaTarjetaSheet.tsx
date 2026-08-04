import { hoyLocalISO } from '@/lib/datetime';
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
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  CrearTarjetaSchema,
  type CrearTarjetaValues,
} from '@/features/cxp/schemas/tarjetas-credito';
import { useCrearTarjeta } from '@/features/cxp/api/useTarjetasCredito';
import { EmpleadoSelector, ProveedorSelector } from '@/components/erp';

export interface NuevaTarjetaSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function NuevaTarjetaSheet({
  open,
  onOpenChange,
}: NuevaTarjetaSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>Nueva tarjeta de crédito</SheetTitle>
          <SheetDescription>
            Solo se almacenan los últimos 4 dígitos. El resto del número
            queda enmascarado. PerfilParser define cómo se interpretará el
            estado de cuenta del banco.
          </SheetDescription>
        </SheetHeader>
        {open && <Form onClose={() => onOpenChange(false)} />}
      </SheetContent>
    </Sheet>
  );
}

function Form({ onClose }: { onClose: () => void }) {
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearTarjeta();
  const hoy = hoyLocalISO();

  const form = useForm<CrearTarjetaValues>({
    resolver: zodResolver(CrearTarjetaSchema),
    defaultValues: {
      emisora: '',
      perfilParser: '',
      ultimosCuatro: '',
      nombreAlias: '',
      titularId: '',
      bancoProveedorId: '',
      limiteCreditoMxn: 0,
      monedaDefault: 'MXN',
      diaCorte: 1,
      diaLimitePago: 20,
      vigenciaDesde: hoy,
    },
  });

  function onSubmit(values: CrearTarjetaValues) {
    crear.mutate(
      { command: values, idempotencyKey },
      {
        onSuccess: (res) => {
          toast.success(`Tarjeta ${res.numeroEnmascarado} creada`);
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
          toast.error('Error al crear tarjeta.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <Campo label="Emisora" required error={form.formState.errors.emisora?.message}>
          <Input placeholder="Visa / Mastercard / Amex" {...form.register('emisora')} />
        </Campo>
        <Campo label="Perfil parser" required error={form.formState.errors.perfilParser?.message}>
          <Input placeholder="Ej.: BBVA_NETO_2024" {...form.register('perfilParser')} />
        </Campo>
        <Campo label="Últimos 4 dígitos" required error={form.formState.errors.ultimosCuatro?.message}>
          <Input
            placeholder="1234"
            maxLength={4}
            inputMode="numeric"
            {...form.register('ultimosCuatro')}
          />
        </Campo>
        <Campo label="Alias" required error={form.formState.errors.nombreAlias?.message}>
          <Input placeholder="TC Operaciones" {...form.register('nombreAlias')} />
        </Campo>
        <Campo label="Titular" required error={form.formState.errors.titularId?.message}>
          <Controller
            control={form.control}
            name="titularId"
            render={({ field }) => (
              <EmpleadoSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
              />
            )}
          />
        </Campo>
        <Campo label="Banco (proveedor)" required error={form.formState.errors.bancoProveedorId?.message}>
          <Controller
            control={form.control}
            name="bancoProveedorId"
            render={({ field }) => (
              <ProveedorSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
              />
            )}
          />
        </Campo>
        <Campo label="Límite crédito MXN" required error={form.formState.errors.limiteCreditoMxn?.message}>
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register('limiteCreditoMxn', { valueAsNumber: true })}
          />
        </Campo>
        <Campo label="Moneda default" required>
          <Input placeholder="MXN" maxLength={3} className="uppercase" {...form.register('monedaDefault')} />
        </Campo>
        <Campo label="Día corte (1-31)" required error={form.formState.errors.diaCorte?.message}>
          <Input
            type="number"
            min="1"
            max="31"
            {...form.register('diaCorte', { valueAsNumber: true })}
          />
        </Campo>
        <Campo label="Día límite pago (1-60)" required error={form.formState.errors.diaLimitePago?.message}>
          <Input
            type="number"
            min="1"
            max="60"
            {...form.register('diaLimitePago', { valueAsNumber: true })}
          />
        </Campo>
        <Campo label="Vigencia desde" required>
          <Input type="date" {...form.register('vigenciaDesde')} />
        </Campo>
      </div>

      <SheetFooter className="px-0">
        <Button type="button" variant="ghost" onClick={onClose} disabled={crear.isPending}>
          Cancelar
        </Button>
        <Button type="submit" disabled={crear.isPending}>
          {crear.isPending ? 'Creando…' : 'Crear tarjeta'}
        </Button>
      </SheetFooter>
    </form>
  );
}

function Campo({
  label,
  required,
  error,
  children,
}: {
  label: string;
  required?: boolean;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1">
      <Label className="text-xs">
        {label}
        {required && <span className="ml-1 text-destructive">*</span>}
      </Label>
      {children}
      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
}
