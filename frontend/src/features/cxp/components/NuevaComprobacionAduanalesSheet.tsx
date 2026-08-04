import { hoyLocalISO } from '@/lib/datetime';
import { Controller, useFieldArray, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Plus, Trash2 } from 'lucide-react';
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
  CrearComprobacionAduanalesSchema,
  type CrearComprobacionAduanalesValues,
} from '@/features/cxp/schemas/comprobaciones';
import { useCrearComprobacionAduanales } from '@/features/cxp/api/useComprobaciones';
import {
  EmpleadoSelector,
  ProveedorSelector,
  SucursalSelector,
} from '@/components/erp';
import { FacturaPicker } from '@/features/cxp/components/FacturaPicker';

/**
 * <c>&lt;NuevaComprobacionAduanalesSheet/&gt;</c> — captura de gastos
 * aduanales agrupando facturas ya capturadas con OC. Requiere número
 * de pedimento + lista de UUIDs de facturas. Tras crear, requiere
 * doble firma (Comercio Exterior + Dirección de Finanzas).
 */
export interface NuevaComprobacionAduanalesSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function NuevaComprobacionAduanalesSheet({
  open,
  onOpenChange,
}: NuevaComprobacionAduanalesSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>Nueva comprobación — Aduanales</SheetTitle>
          <SheetDescription>
            Agrupa facturas ya capturadas de la agencia aduanal. Requerirá
            firma Nivel 1 (Comercio Exterior) y Nivel 2 (Dirección de
            Finanzas) antes de liberarse.
          </SheetDescription>
        </SheetHeader>
        {open && <Form onClose={() => onOpenChange(false)} />}
      </SheetContent>
    </Sheet>
  );
}

function Form({ onClose }: { onClose: () => void }) {
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearComprobacionAduanales();
  const hoy = hoyLocalISO();

  const form = useForm<CrearComprobacionAduanalesValues>({
    resolver: zodResolver(CrearComprobacionAduanalesSchema),
    defaultValues: {
      sucursalId: '',
      responsableId: '',
      proveedorId: '',
      numeroPedimento: '',
      fechaInicio: hoy,
      fechaFin: hoy,
      moneda: 'MXN',
      observaciones: null,
      facturaProveedorIds: [''],
    },
  });

  const { fields, append, remove } = useFieldArray({
    control: form.control,
    name: 'facturaProveedorIds' as never,
  });

  function onSubmit(values: CrearComprobacionAduanalesValues) {
    crear.mutate(
      { command: values, idempotencyKey },
      {
        onSuccess: (res) => {
          toast.success(
            `Comprobación aduanal creada — pedimento ${res.numeroPedimento}`,
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
          toast.error('Error al crear comprobación aduanal.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <Campo label="Sucursal" required error={form.formState.errors.sucursalId?.message}>
          <Controller
            control={form.control}
            name="sucursalId"
            render={({ field }) => (
              <SucursalSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
              />
            )}
          />
        </Campo>
        <Campo label="Responsable" required error={form.formState.errors.responsableId?.message}>
          <Controller
            control={form.control}
            name="responsableId"
            render={({ field }) => (
              <EmpleadoSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
              />
            )}
          />
        </Campo>
        <Campo label="Agencia aduanal (proveedor)" required error={form.formState.errors.proveedorId?.message}>
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
        <Campo label="Número de pedimento" required error={form.formState.errors.numeroPedimento?.message}>
          <Input placeholder="26 47 3807 6014923" {...form.register('numeroPedimento')} />
        </Campo>
        <Campo label="Fecha inicio" required>
          <Input type="date" {...form.register('fechaInicio')} />
        </Campo>
        <Campo label="Fecha fin" required>
          <Input type="date" {...form.register('fechaFin')} />
        </Campo>
        <Campo label="Moneda" required>
          <Input placeholder="MXN" maxLength={3} className="uppercase" {...form.register('moneda')} />
        </Campo>
      </div>

      <Campo label="Observaciones">
        <Textarea rows={2} {...form.register('observaciones')} />
      </Campo>

      <section className="space-y-2">
        <header className="flex items-center justify-between">
          <h3 className="text-sm font-medium">
            UUIDs de facturas ya capturadas ({fields.length})
          </h3>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => append('' as never)}
          >
            <Plus className="mr-1 h-3 w-3" />
            Agregar factura
          </Button>
        </header>

        <div className="space-y-2">
          {fields.map((field, index) => (
            <div key={field.id} className="flex items-center gap-2">
              {/* Sin filtro de proveedor: las facturas del pedimento pueden
                  ser de varios emisores (agencia, fletera, impuestos). */}
              <Controller
                control={form.control}
                name={`facturaProveedorIds.${index}` as const}
                render={({ field: f }) => (
                  <FacturaPicker
                    value={f.value || null}
                    onChange={(id) => f.onChange(id ?? '')}
                    className="flex-1"
                  />
                )}
              />
              {fields.length > 1 && (
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() => remove(index)}
                  className="shrink-0 text-destructive hover:bg-destructive/10"
                >
                  <Trash2 className="h-3 w-3" />
                </Button>
              )}
            </div>
          ))}
        </div>
        {form.formState.errors.facturaProveedorIds?.message && (
          <p className="text-xs text-destructive">
            {form.formState.errors.facturaProveedorIds.message}
          </p>
        )}
        <p className="text-xs text-muted-foreground">
          Las facturas deben estar en estado <strong>Capturada</strong> y tener
          OC asociada. Captúralas primero vía la bandeja de Facturas.
        </p>
      </section>

      <SheetFooter className="px-0">
        <Button type="button" variant="ghost" onClick={onClose} disabled={crear.isPending}>
          Cancelar
        </Button>
        <Button type="submit" disabled={crear.isPending}>
          {crear.isPending ? 'Creando…' : 'Crear comprobación aduanal'}
        </Button>
      </SheetFooter>
    </form>
  );
}

interface CampoProps {
  label: string;
  required?: boolean;
  error?: string;
  children: React.ReactNode;
}

function Campo({ label, required, error, children }: CampoProps) {
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
