import { hoyLocalISO } from '@/lib/datetime';
import { useEffect, useState } from 'react';
import { Controller, useFieldArray, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Plus, Trash2 } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { SucursalSelector } from '@/components/erp';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import { EmitirReppSchema, type EmitirReppValues } from '@/features/facturacion/schemas/emitir-repp';
import { useEmitirRepp } from '@/features/facturacion/api/useRepp';
import { FacturaPpdPicker } from '@/features/facturacion/components/FacturaPpdPicker';
import type { FacturaCobrablePpdItem } from '@/features/facturacion/api/types';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;NuevoRepp/&gt;</c> — emisión de complemento de pago (Pago 2.0,
 * FE-F6). Cabecera del pago + facturas que cubre (inline), elegidas con
 * <c>FacturaPpdPicker</c> (saldo por cobrar neto, [Decisión 13-K]; cierra
 * PLATFORM-TODO(&lt;FacturaPicker&gt;)). Al elegir factura se propone su
 * saldo como importe; las siguientes se restringen al mismo cliente
 * (regla <c>REPP_MULTIPLES_CLIENTES</c>).
 */
export interface NuevoReppProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange?: (dirty: boolean) => void;
}

function defaultFactura() {
  return { facturaVentaId: '', importePagado: 0 };
}

const VALORES_INICIALES: EmitirReppValues = {
  sucursalId: '',
  fechaPago: hoyLocalISO(),
  monedaPago: 'MXN',
  tcPago: null,
  formaPagoReal: '03',
  cuentaOrdenante: null,
  cuentaBeneficiaria: null,
  referenciaPago: null,
  facturas: [defaultFactura()],
};

function nullIfEmpty(v: string | null | undefined): string | null {
  const s = (v ?? '').trim();
  return s.length > 0 ? s : null;
}

const numeroONull = (v: unknown): number | null => {
  if (v === '' || v == null) return null;
  const n = Number(v);
  return Number.isNaN(n) ? null : n;
};

export function NuevoRepp({ onClose, onDirtyChange }: NuevoReppProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const emitir = useEmitirRepp();

  const form = useForm<EmitirReppValues>({
    resolver: zodResolver(EmitirReppSchema),
    defaultValues: VALORES_INICIALES,
  });

  const { fields, append, remove } = useFieldArray({
    control: form.control,
    name: 'facturas',
  });

  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange?.(isDirty);
  }, [isDirty, onDirtyChange]);

  const facturasWatch = useWatch({ control: form.control, name: 'facturas' });
  const totalPago = (facturasWatch ?? []).reduce(
    (acc, f) => acc + (f.importePagado ?? 0),
    0,
  );

  // Items elegidos en el picker, por id de factura (para saldo/cliente).
  const [elegidas, setElegidas] = useState<Record<string, FacturaCobrablePpdItem>>({});

  // Todas las facturas de un REPP son del mismo cliente: el RFC de la
  // primera elegida restringe los pickers restantes.
  const idsElegidos = (facturasWatch ?? []).map((f) => f.facturaVentaId).filter(Boolean);
  const receptorRfc = idsElegidos.map((id) => elegidas[id]?.receptorRfc).find(Boolean) ?? null;

  function onElegirFactura(index: number, item: FacturaCobrablePpdItem | null) {
    form.setValue(`facturas.${index}.facturaVentaId`, item?.facturaVentaId ?? '', {
      shouldDirty: true,
      shouldValidate: true,
    });
    // Propone el saldo por cobrar como importe (editable para parcialidades).
    form.setValue(`facturas.${index}.importePagado`, item?.saldo ?? 0, {
      shouldDirty: true,
      shouldValidate: true,
    });
    if (item != null) setElegidas((prev) => ({ ...prev, [item.facturaVentaId]: item }));
  }

  function onSubmit(values: EmitirReppValues) {
    emitir.mutate(
      {
        command: {
          sucursalId: values.sucursalId,
          fechaPago: `${values.fechaPago}T12:00:00Z`,
          monedaPago: values.monedaPago.toUpperCase(),
          tcPago: values.tcPago,
          formaPagoReal: values.formaPagoReal,
          cuentaOrdenante: nullIfEmpty(values.cuentaOrdenante),
          cuentaBeneficiaria: nullIfEmpty(values.cuentaBeneficiaria),
          referenciaPago: nullIfEmpty(values.referenciaPago),
          facturas: values.facturas,
        },
        idempotencyKey,
      },
      {
        onSuccess: (res) => {
          toast.success(
            res.uuid
              ? `REPP ${res.folio} timbrado · pago ${res.importeTotalPago.toFixed(2)}`
              : `REPP ${res.folio} emitido (${res.estado})`,
          );
          onClose({ force: true });
        },
        onError: (error) => {
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
              description:
                error.problem.detail ??
                (error.traceId ? `Código: ${error.traceId}` : undefined),
            });
            return;
          }
          toast.error('Error inesperado al emitir el REPP.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} noValidate className="space-y-5">
      <section className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <Campo label="Sucursal" required error={form.formState.errors.sucursalId?.message}>
          <Controller
            name="sucursalId"
            control={form.control}
            render={({ field }) => (
              <SucursalSelector value={field.value || null} onChange={(id) => field.onChange(id ?? '')} />
            )}
          />
        </Campo>
        <Campo label="Fecha de pago" required error={form.formState.errors.fechaPago?.message}>
          <Input type="date" {...form.register('fechaPago')} />
        </Campo>
        <Campo label="Forma de pago" required error={form.formState.errors.formaPagoReal?.message} hint="Clave SAT c_FormaPago (03 transferencia…).">
          <Input maxLength={5} {...form.register('formaPagoReal')} />
        </Campo>
        <Campo label="Moneda del pago" required error={form.formState.errors.monedaPago?.message}>
          <Input className="uppercase" maxLength={3} {...form.register('monedaPago')} />
        </Campo>
        <Campo label="Tipo de cambio" error={form.formState.errors.tcPago?.message} hint="Solo si no es MXN.">
          <Input type="number" step="0.0001" min="0" {...form.register('tcPago', { setValueAs: numeroONull })} />
        </Campo>
        <Campo label="Referencia de pago">
          <Input placeholder="Opcional" {...form.register('referenciaPago')} />
        </Campo>
        <Campo label="Cuenta ordenante">
          <Input placeholder="Opcional" {...form.register('cuentaOrdenante')} />
        </Campo>
        <Campo label="Cuenta beneficiaria">
          <Input placeholder="Opcional" {...form.register('cuentaBeneficiaria')} />
        </Campo>
      </section>

      <section className="space-y-2">
        <header className="flex items-center justify-between">
          <h3 className="text-sm font-medium">Facturas cubiertas ({fields.length})</h3>
          <Button type="button" variant="ghost" size="sm" onClick={() => append(defaultFactura())}>
            <Plus className="mr-1 h-3 w-3" />
            Agregar factura
          </Button>
        </header>
        <div className="space-y-2">
          {fields.map((field, index) => {
            const errs = form.formState.errors.facturas?.[index];
            const idActual = facturasWatch?.[index]?.facturaVentaId ?? '';
            const elegida = idActual !== '' ? elegidas[idActual] : undefined;
            return (
              <div
                key={field.id}
                className={cn(
                  'grid grid-cols-1 gap-2 rounded-md border border-dashed border-primary/40 bg-primary/5 p-3 sm:grid-cols-12',
                  errs && 'border-destructive/50',
                )}
              >
                <div className="sm:col-span-7">
                  <Label className="text-xs">Factura *</Label>
                  <Controller
                    name={`facturas.${index}.facturaVentaId` as const}
                    control={form.control}
                    render={({ field: f }) => (
                      <FacturaPpdPicker
                        receptorRfc={receptorRfc}
                        excluirIds={idsElegidos.filter((id) => id !== f.value)}
                        value={f.value === '' ? null : f.value}
                        onChange={(item) => onElegirFactura(index, item)}
                      />
                    )}
                  />
                  {errs?.facturaVentaId && (
                    <p className="text-xs text-destructive">
                      {errs.facturaVentaId.message}
                    </p>
                  )}
                </div>
                <div className="sm:col-span-3">
                  <Label className="text-xs">Importe pagado *</Label>
                  <Input
                    type="number"
                    step="0.01"
                    min="0"
                    {...form.register(`facturas.${index}.importePagado` as const, {
                      valueAsNumber: true,
                    })}
                  />
                  {elegida != null && (
                    <p className="text-[11px] leading-tight text-muted-foreground">
                      Saldo por cobrar {elegida.saldo.toFixed(2)} {elegida.moneda} · parcialidad{' '}
                      {elegida.numParcialidadSiguiente}
                      {elegida.acreditadoNc > 0
                        ? ` · NC aplicadas −${elegida.acreditadoNc.toFixed(2)}`
                        : ''}
                    </p>
                  )}
                  {errs?.importePagado && (
                    <p className="text-xs text-destructive">
                      {errs.importePagado.message}
                    </p>
                  )}
                </div>
                <div className="flex items-end justify-end sm:col-span-2">
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={() => remove(index)}
                    disabled={fields.length <= 1}
                    className="text-destructive hover:bg-destructive/10"
                  >
                    <Trash2 className="mr-1 h-3 w-3" />
                    Quitar
                  </Button>
                </div>
              </div>
            );
          })}
        </div>
        {form.formState.errors.facturas?.message && (
          <p className="text-xs text-destructive">
            {form.formState.errors.facturas.message}
          </p>
        )}
      </section>

      <div className="flex items-center justify-between border-t pt-4">
        <p className="text-sm">
          Total del pago:{' '}
          <span className="font-semibold tabular-nums">
            {totalPago.toFixed(2)}
          </span>
        </p>
        <div className="flex items-center gap-2">
          <Button type="button" variant="ghost" onClick={() => onClose()} disabled={emitir.isPending}>
            Cancelar
          </Button>
          <Button type="submit" disabled={emitir.isPending}>
            {emitir.isPending ? 'Emitiendo…' : 'Emitir REPP'}
          </Button>
        </div>
      </div>
    </form>
  );
}

function Campo({
  label,
  required,
  error,
  hint,
  children,
}: {
  label: string;
  required?: boolean;
  error?: string;
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1">
      <Label className="text-xs">
        {label}
        {required && <span className="ml-1 text-destructive">*</span>}
      </Label>
      {children}
      {hint != null && <p className="text-[11px] leading-tight text-muted-foreground">{hint}</p>}
      {error != null && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
}
