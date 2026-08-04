import { hoyLocalISO } from '@/lib/datetime';
import { useState } from 'react';
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
import { Textarea } from '@/components/ui/textarea';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  MovimientoTcConCfdiSchema,
  MovimientoTcSinCfdiSchema,
  type MovimientoTcConCfdiValues,
  type MovimientoTcSinCfdiValues,
} from '@/features/cxp/schemas/tarjetas-credito';
import {
  useRegistrarMovimientoTcConCfdi,
  useRegistrarMovimientoTcSinCfdi,
} from '@/features/cxp/api/useTarjetasCredito';
import { ProveedorSelector, UsuarioSelector } from '@/components/erp';
import { CfdiPorProcesarPicker } from '@/features/cxp/components/CfdiPorProcesarPicker';
import { TarjetaSelector } from '@/features/cxp/components/TarjetaSelector';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;NuevoMovimientoTcSheet/&gt;</c> — captura de movimiento TC.
 * Toggle entre Flujo A (con CFDI a nombre de Millet → genera
 * FacturaProveedor automáticamente) y Flujo B (sin CFDI → solo
 * movimiento sin factura).
 */
export interface NuevoMovimientoTcSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

type Flujo = 'A' | 'B';

export function NuevoMovimientoTcSheet({
  open,
  onOpenChange,
}: NuevoMovimientoTcSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-2xl">
        <SheetHeader>
          <SheetTitle>Nuevo movimiento TC</SheetTitle>
          <SheetDescription>
            Selecciona el flujo según si tienes CFDI a nombre de Millet
            (genera factura) o solo ticket del comercio (solo movimiento).
          </SheetDescription>
        </SheetHeader>
        {open && <FormContainer onClose={() => onOpenChange(false)} />}
      </SheetContent>
    </Sheet>
  );
}

function FormContainer({ onClose }: { onClose: () => void }) {
  const [flujo, setFlujo] = useState<Flujo>('B');
  return (
    <div className="space-y-4 px-4">
      <div className="flex gap-2">
        <Button
          type="button"
          variant={flujo === 'A' ? 'default' : 'outline'}
          size="sm"
          onClick={() => setFlujo('A')}
        >
          Flujo A — con CFDI
        </Button>
        <Button
          type="button"
          variant={flujo === 'B' ? 'default' : 'outline'}
          size="sm"
          onClick={() => setFlujo('B')}
        >
          Flujo B — sin CFDI
        </Button>
      </div>

      <div
        className={cn(
          'rounded-md px-3 py-2 text-xs',
          flujo === 'A'
            ? 'bg-blue-50 text-blue-900'
            : 'bg-amber-50 text-amber-900',
        )}
      >
        {flujo === 'A'
          ? 'Backend generará la FacturaProveedor en estado Pagada al registrar.'
          : 'Solo se registra el movimiento; ningún pasivo se crea (gasto sin CFDI).'}
      </div>

      {flujo === 'A' ? (
        <FormConCfdi onClose={onClose} />
      ) : (
        <FormSinCfdi onClose={onClose} />
      )}
    </div>
  );
}

function FormSinCfdi({ onClose }: { onClose: () => void }) {
  const idempotencyKey = useFormIdempotencyKey();
  const registrar = useRegistrarMovimientoTcSinCfdi();
  const hoy = hoyLocalISO();

  const form = useForm<MovimientoTcSinCfdiValues>({
    resolver: zodResolver(MovimientoTcSinCfdiSchema),
    defaultValues: {
      tarjetaId: '',
      usuarioQueUsoId: '',
      fechaMovimiento: hoy,
      montoOriginal: 0,
      monedaOriginal: 'MXN',
      tipoCambioCaptura: null,
      merchantRaw: '',
      descripcionLibre: null,
      conceptoContable: '',
      ticketBlobRef: null,
    },
  });

  function onSubmit(values: MovimientoTcSinCfdiValues) {
    registrar.mutate(
      { command: values, idempotencyKey },
      {
        onSuccess: () => {
          toast.success('Movimiento sin CFDI registrado');
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
          toast.error('Error al registrar.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <Campo label="Tarjeta" required error={form.formState.errors.tarjetaId?.message}>
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
        </Campo>
        <Campo label="Usuario que usó" required error={form.formState.errors.usuarioQueUsoId?.message}>
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
        </Campo>
        <Campo label="Fecha" required>
          <Input type="date" {...form.register('fechaMovimiento')} />
        </Campo>
        <Campo label="Moneda" required>
          <Input
            placeholder="MXN"
            maxLength={3}
            className="uppercase"
            {...form.register('monedaOriginal')}
          />
        </Campo>
        <Campo label="Monto" required error={form.formState.errors.montoOriginal?.message}>
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register('montoOriginal', { valueAsNumber: true })}
          />
        </Campo>
        <Campo label="Tipo cambio (si no MXN)">
          <Input
            type="number"
            step="0.0001"
            min="0"
            {...form.register('tipoCambioCaptura', { valueAsNumber: true })}
          />
        </Campo>
        <Campo label="Merchant (literal)" required error={form.formState.errors.merchantRaw?.message}>
          <Input
            placeholder="Como aparece en el ticket"
            {...form.register('merchantRaw')}
          />
        </Campo>
        <Campo label="Concepto contable" required error={form.formState.errors.conceptoContable?.message}>
          <Input placeholder="Alimentos clientes" {...form.register('conceptoContable')} />
        </Campo>
      </div>
      <Campo label="Descripción libre">
        <Textarea rows={2} {...form.register('descripcionLibre')} />
      </Campo>

      <SheetFooter className="px-0">
        <Button type="button" variant="ghost" onClick={onClose} disabled={registrar.isPending}>
          Cancelar
        </Button>
        <Button type="submit" disabled={registrar.isPending}>
          {registrar.isPending ? 'Registrando…' : 'Registrar (sin CFDI)'}
        </Button>
      </SheetFooter>
    </form>
  );
}

function FormConCfdi({ onClose }: { onClose: () => void }) {
  const idempotencyKey = useFormIdempotencyKey();
  const registrar = useRegistrarMovimientoTcConCfdi();
  const hoy = hoyLocalISO();

  const form = useForm<MovimientoTcConCfdiValues>({
    resolver: zodResolver(MovimientoTcConCfdiSchema),
    defaultValues: {
      tarjetaId: '',
      usuarioQueUsoId: '',
      fechaMovimiento: hoy,
      montoOriginal: 0,
      monedaOriginal: 'MXN',
      tipoCambioCaptura: null,
      merchantRaw: '',
      descripcionLibre: null,
      conceptoContable: '',
      cfdiRecibidoId: '',
      proveedorId: '',
      uuidCfdi: null,
      folioProveedor: null,
      serieProveedor: null,
      fechaCfdi: hoy,
      subtotal: 0,
      descuentos: 0,
      impuestosTrasladados: 0,
      retenciones: 0,
      totalFactura: 0,
      fechaVencimiento: hoy,
    },
  });

  function onSubmit(values: MovimientoTcConCfdiValues) {
    registrar.mutate(
      {
        command: { ...values, fechaCfdi: `${values.fechaCfdi}T00:00:00Z` },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Movimiento con CFDI registrado (FacturaProveedor generada)');
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
          toast.error('Error al registrar.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4">
      <section className="space-y-3">
        <h3 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
          Movimiento
        </h3>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Campo label="Tarjeta" required error={form.formState.errors.tarjetaId?.message}>
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
          </Campo>
          <Campo label="Usuario que usó" required error={form.formState.errors.usuarioQueUsoId?.message}>
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
          </Campo>
          <Campo label="Fecha movimiento" required>
            <Input type="date" {...form.register('fechaMovimiento')} />
          </Campo>
          <Campo label="Moneda" required>
            <Input
              placeholder="MXN"
              maxLength={3}
              className="uppercase"
              {...form.register('monedaOriginal')}
            />
          </Campo>
          <Campo label="Monto" required>
            <Input
              type="number"
              step="0.01"
              min="0"
              {...form.register('montoOriginal', { valueAsNumber: true })}
            />
          </Campo>
          <Campo label="Tipo cambio">
            <Input
              type="number"
              step="0.0001"
              min="0"
              {...form.register('tipoCambioCaptura', { valueAsNumber: true })}
            />
          </Campo>
          <Campo label="Merchant" required>
            <Input {...form.register('merchantRaw')} />
          </Campo>
          <Campo label="Concepto contable" required>
            <Input {...form.register('conceptoContable')} />
          </Campo>
        </div>
      </section>

      <section className="space-y-3">
        <h3 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
          CFDI ligado (debe estar ya capturado en bandeja CFDIs)
        </h3>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Campo label="CFDI recibido" required error={form.formState.errors.cfdiRecibidoId?.message}>
            <Controller
              control={form.control}
              name="cfdiRecibidoId"
              render={({ field }) => (
                <CfdiPorProcesarPicker
                  value={field.value || null}
                  onSelect={(cfdi) => {
                    field.onChange(cfdi?.id ?? '');
                    if (!cfdi) return;
                    // Heredar la metadata fiscal del CFDI elegido; el
                    // usuario solo completa lo del movimiento (monto TC,
                    // merchant, concepto).
                    form.setValue('uuidCfdi', cfdi.uuidCfdi);
                    form.setValue('serieProveedor', cfdi.serie);
                    form.setValue('folioProveedor', cfdi.folio);
                    form.setValue('fechaCfdi', cfdi.fechaCfdi.slice(0, 10));
                    form.setValue('subtotal', cfdi.subtotal);
                    form.setValue('impuestosTrasladados', cfdi.impuestosTrasladados);
                    form.setValue('retenciones', cfdi.retenciones);
                    form.setValue('totalFactura', cfdi.total, {
                      shouldValidate: true,
                    });
                  }}
                />
              )}
            />
          </Campo>
          <Campo label="Proveedor" required error={form.formState.errors.proveedorId?.message}>
            <Controller
              control={form.control}
              name="proveedorId"
              render={({ field }) => (
                <ProveedorSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? '')}
                />
              )}
            />
          </Campo>
          <Campo label="Serie">
            <Input {...form.register('serieProveedor')} />
          </Campo>
          <Campo label="Folio">
            <Input {...form.register('folioProveedor')} />
          </Campo>
          <Campo label="Fecha CFDI" required>
            <Input type="date" {...form.register('fechaCfdi')} />
          </Campo>
          <Campo label="Fecha vencimiento" required>
            <Input type="date" {...form.register('fechaVencimiento')} />
          </Campo>
          <Campo label="Subtotal">
            <Input
              type="number"
              step="0.01"
              min="0"
              {...form.register('subtotal', { valueAsNumber: true })}
            />
          </Campo>
          <Campo label="IVA trasladado">
            <Input
              type="number"
              step="0.01"
              min="0"
              {...form.register('impuestosTrasladados', { valueAsNumber: true })}
            />
          </Campo>
          <Campo label="Total factura" required>
            <Input
              type="number"
              step="0.01"
              min="0"
              {...form.register('totalFactura', { valueAsNumber: true })}
            />
          </Campo>
        </div>
      </section>

      <SheetFooter className="px-0">
        <Button type="button" variant="ghost" onClick={onClose} disabled={registrar.isPending}>
          Cancelar
        </Button>
        <Button type="submit" disabled={registrar.isPending}>
          {registrar.isPending ? 'Registrando…' : 'Registrar (con CFDI)'}
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
