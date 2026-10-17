import { useEffect } from 'react';
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
  Field,
  ProveedorSelector,
  SucursalSelector,
} from '@/components/erp';
import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
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
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(() =>
    onOpenChange(false),
  );
  return (
    <Sheet
      open={open}
      onOpenChange={(next) => (next ? onOpenChange(next) : solicitarCierre())}
    >
      <SheetContent side="right" size="form">
        <SheetHeader>
          <SheetTitle>Nueva comprobación — Aduanales</SheetTitle>
          <SheetDescription>
            Agrupa facturas ya capturadas de la agencia aduanal. Requerirá
            firma Nivel 1 (Comercio Exterior) y Nivel 2 (Dirección de
            Finanzas) antes de liberarse.
          </SheetDescription>
        </SheetHeader>
        {open && (
          <Form
            onCancel={() => solicitarCierre()}
            onCreated={() => solicitarCierre(true)}
            onDirtyChange={setDirty}
          />
        )}
      </SheetContent>
      {dialogo}
    </Sheet>
  );
}

function Form({
  onCancel,
  onCreated,
  onDirtyChange,
}: {
  onCancel: () => void;
  onCreated: () => void;
  onDirtyChange: (dirty: boolean) => void;
}) {
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

  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  function onSubmit(values: CrearComprobacionAduanalesValues) {
    crear.mutate(
      { command: values, idempotencyKey },
      {
        onSuccess: (res) => {
          toast.success(
            `Comprobación aduanal creada — pedimento ${res.numeroPedimento}`,
          );
          onCreated();
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
          {(control) => (
            <Controller
              control={form.control}
              name="sucursalId"
              render={({ field }) => (
                <SucursalSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? '')}
                  triggerProps={control}
                />
              )}
            />
          )}
        </Campo>
        <Campo label="Responsable" required error={form.formState.errors.responsableId?.message}>
          {(control) => (
            <Controller
              control={form.control}
              name="responsableId"
              render={({ field }) => (
                <EmpleadoSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? '')}
                  triggerProps={control}
                />
              )}
            />
          )}
        </Campo>
        <Campo label="Agencia aduanal (proveedor)" required error={form.formState.errors.proveedorId?.message}>
          {(control) => (
            <Controller
              control={form.control}
              name="proveedorId"
              render={({ field }) => (
                <ProveedorSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? '')}
                  triggerProps={control}
                />
              )}
            />
          )}
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

      <Campo label="Observaciones" optional>
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
            <Plus className="mr-1 size-3.5" />
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
                    triggerProps={{ 'aria-label': `Factura ${index + 1}` }}
                  />
                )}
              />
              {fields.length > 1 && (
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() => remove(index)}
                  aria-label={`Quitar factura ${index + 1}`}
                  className="shrink-0 text-danger-fg hover:bg-danger-bg"
                >
                  <Trash2 className="h-3 w-3" aria-hidden="true" />
                </Button>
              )}
            </div>
          ))}
        </div>
        {form.formState.errors.facturaProveedorIds?.message && (
          <p role="alert" className="text-xs text-danger-fg">
            {form.formState.errors.facturaProveedorIds.message}
          </p>
        )}
        <p className="text-xs text-ink-muted">
          Las facturas deben estar en estado <strong>Capturada</strong> y tener
          OC asociada. Captúralas primero vía la bandeja de Facturas.
        </p>
      </section>

      <SheetFooter className="px-0">
        <Button type="button" variant="ghost" onClick={onCancel} disabled={crear.isPending}>
          Cancelar
        </Button>
        <Button type="submit" disabled={crear.isPending}>
          {crear.isPending ? 'Creando…' : 'Crear comprobación aduanal'}
        </Button>
      </SheetFooter>
    </form>
  );
}

/** Alias local: `Field` accesible (label asociado, aria-*). */
const Campo = Field;
