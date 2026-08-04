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
  RegistrarMovimientoEspecialTcSchema,
  type RegistrarMovimientoEspecialTcValues,
} from '@/features/cxp/schemas/estados-cuenta-tc';
import { useRegistrarMovimientoEspecialTc } from '@/features/cxp/api/useEstadosCuentaTc';
import {
  TipoMovimientoTc,
  TipoMovimientoTcLabels,
} from '@/features/cxp/api/types';
import { UsuarioSelector } from '@/components/erp';
import { TarjetaSelector } from '@/features/cxp/components/TarjetaSelector';

/**
 * <c>&lt;RegistrarMovimientoEspecialSheet/&gt;</c> — captura cargos
 * especiales del banco a la TC: intereses moratorios, anualidad,
 * comisión por divisa (§8.2-§8.4 del anexo TC).
 *
 * <para>Backend valida que el tipo sea uno de los 3 permitidos para
 * "especial" (no acepta CompraConCfdi/SinCfdi/Refund).</para>
 */
export interface RegistrarMovimientoEspecialSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

const TIPOS_ESPECIALES: readonly (typeof TipoMovimientoTc)[keyof typeof TipoMovimientoTc][] = [
  TipoMovimientoTc.GastoFinanciero,
  TipoMovimientoTc.Anualidad,
  TipoMovimientoTc.ComisionDivisa,
];

export function RegistrarMovimientoEspecialSheet({
  open,
  onOpenChange,
}: RegistrarMovimientoEspecialSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Movimiento especial del banco</SheetTitle>
          <SheetDescription>
            Intereses moratorios, anualidad o comisión por divisa. El
            backend rechaza tipos diferentes a estos 3.
          </SheetDescription>
        </SheetHeader>
        {open && <Form onClose={() => onOpenChange(false)} />}
      </SheetContent>
    </Sheet>
  );
}

function Form({ onClose }: { onClose: () => void }) {
  const idempotencyKey = useFormIdempotencyKey();
  const registrar = useRegistrarMovimientoEspecialTc();
  const hoy = hoyLocalISO();

  const form = useForm<RegistrarMovimientoEspecialTcValues>({
    resolver: zodResolver(RegistrarMovimientoEspecialTcSchema),
    defaultValues: {
      tarjetaId: '',
      usuarioQueUsoId: '',
      fechaMovimiento: hoy,
      tipo: TipoMovimientoTc.GastoFinanciero,
      montoOriginal: 0,
      monedaOriginal: 'MXN',
      tipoCambioCaptura: null,
      merchantRaw: '',
      conceptoContable: '',
    },
  });

  function onSubmit(values: RegistrarMovimientoEspecialTcValues) {
    registrar.mutate(
      { command: values, idempotencyKey },
      {
        onSuccess: (res) => {
          toast.success(
            `${TipoMovimientoTcLabels[res.tipo]} registrado (${res.montoMxn.toFixed(2)} MXN)`,
          );
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
          toast.error('Error al registrar movimiento.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
      <Field label="Tarjeta" required>
        <Controller
          control={form.control}
          name="tarjetaId"
          render={({ field }) => (
            <TarjetaSelector
              value={field.value || null}
              onChange={(id) => field.onChange(id ?? '')}
            />
          )}
        />
      </Field>
      <Field label="Usuario que usó" required>
        <Controller
          control={form.control}
          name="usuarioQueUsoId"
          render={({ field }) => (
            <UsuarioSelector
              value={field.value || null}
              onChange={(id) => field.onChange(id ?? '')}
            />
          )}
        />
      </Field>

      <Field label="Tipo" required>
        <Controller
          control={form.control}
          name="tipo"
          render={({ field }) => (
            <Select
              value={String(field.value)}
              onValueChange={(v) => field.onChange(Number(v))}
            >
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {TIPOS_ESPECIALES.map((t) => (
                  <SelectItem key={t} value={String(t)}>
                    {TipoMovimientoTcLabels[t]}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
      </Field>

      <div className="grid grid-cols-2 gap-3">
        <Field label="Fecha" required>
          <Input type="date" {...form.register('fechaMovimiento')} />
        </Field>
        <Field label="Moneda" required>
          <Input
            maxLength={3}
            className="uppercase"
            {...form.register('monedaOriginal')}
          />
        </Field>
        <Field
          label="Monto"
          required
          error={form.formState.errors.montoOriginal?.message}
        >
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register('montoOriginal', { valueAsNumber: true })}
          />
        </Field>
        <Field label="Tipo cambio">
          <Input
            type="number"
            step="0.0001"
            min="0"
            {...form.register('tipoCambioCaptura', { valueAsNumber: true })}
          />
        </Field>
      </div>

      <Field label="Merchant" required>
        <Input
          placeholder="Como aparece en el estado de cuenta"
          {...form.register('merchantRaw')}
        />
      </Field>

      <Field label="Concepto contable" required>
        <Input
          placeholder="Gastos financieros / Anualidad TC"
          {...form.register('conceptoContable')}
        />
      </Field>

      <SheetFooter className="px-0">
        <Button
          type="button"
          variant="ghost"
          onClick={onClose}
          disabled={registrar.isPending}
        >
          Cancelar
        </Button>
        <Button type="submit" disabled={registrar.isPending}>
          {registrar.isPending ? 'Registrando…' : 'Registrar'}
        </Button>
      </SheetFooter>
    </form>
  );
}

function Field({
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
