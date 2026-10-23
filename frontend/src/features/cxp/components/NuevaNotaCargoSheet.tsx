import { useEffect } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
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
import { Textarea } from '@/components/ui/textarea';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  CrearNotaCargoSchema,
  type CrearNotaCargoValues,
} from '@/features/cxp/schemas/notas-y-anticipos';
import { useCrearNotaCargo } from '@/features/cxp/api/useNotasYAnticipos';
import { Field, MoneyField, ProveedorSelector, SucursalSelector } from '@/components/erp';
import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
import { FacturaPicker } from '@/features/cxp/components/FacturaPicker';

/**
 * <c>&lt;NuevaNotaCargoSheet/&gt;</c> — creación de nota de cargo en
 * estado Borrador (luego va a Autorizada por Dirección). El folio NCG
 * se asigna atómicamente en backend.
 */
export interface NuevaNotaCargoSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function NuevaNotaCargoSheet({
  open,
  onOpenChange,
}: NuevaNotaCargoSheetProps) {
  // Confirmación al cerrar con cambios; el alta exitosa cierra con force.
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
          <SheetTitle>Crear nota de cargo</SheetTitle>
          <SheetDescription>
            Cargo interno contra el proveedor (devoluciones, garantías,
            fletes). Folio NCG asignado automáticamente. Requiere
            autorización Dirección antes de aplicarse.
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
  const crear = useCrearNotaCargo();

  const form = useForm<CrearNotaCargoValues>({
    resolver: zodResolver(CrearNotaCargoSchema),
    defaultValues: {
      proveedorId: '',
      sucursalId: null,
      concepto: '',
      conceptoContableId: null,
      monto: 0,
      moneda: 'MXN',
      tipoCambio: null,
      facturaOrigenId: null,
      devolucionAProveedorId: null,
    },
  });

  // La factura origen es del mismo proveedor: el picker se filtra con el
  // proveedor elegido arriba y se habilita hasta entonces.
  const monto = useWatch({ control: form.control, name: 'monto' });
  const moneda = useWatch({ control: form.control, name: 'moneda' });
  const proveedorIdSel = useWatch({ control: form.control, name: 'proveedorId' });
  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  function onSubmit(values: CrearNotaCargoValues) {
    crear.mutate(
      { command: values, idempotencyKey },
      {
        onSuccess: (res) => {
          toast.success(`Nota de cargo ${res.folio} creada (Borrador)`);
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
          toast.error('Error al crear nota de cargo.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
      <Field
        label="Proveedor"
        error={form.formState.errors.proveedorId?.message}
        required
      >
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
      </Field>

      <Field label="Sucursal" optional>
        {(control) => (
          <Controller
            control={form.control}
            name="sucursalId"
            render={({ field }) => (
              <SucursalSelector
                value={field.value ?? null}
                onChange={(id) => field.onChange(id)}
                triggerProps={control}
              />
            )}
          />
        )}
      </Field>

      <Field
        label="Concepto"
        error={form.formState.errors.concepto?.message}
        required
      >
        <Textarea
          rows={3}
          placeholder="Ej.: Reembolso por flete duplicado, OC 12345."
          {...form.register('concepto')}
        />
      </Field>

      <Field
        label="Monto y moneda"
        error={
          form.formState.errors.monto?.message ??
          form.formState.errors.moneda?.message
        }
        required
      >
        {(control) => (
          <MoneyField
            value={monto > 0 ? { amount: monto, currency: moneda } : null}
            defaultCurrency={moneda}
            onChange={(m) => {
              form.setValue('monto', m?.amount ?? 0, {
                shouldDirty: true,
                shouldValidate: form.formState.isSubmitted,
              });
              if (m) form.setValue('moneda', m.currency, { shouldDirty: true });
            }}
            inputProps={{ ...control, min: 0 }}
          />
        )}
      </Field>

      <Field label="Factura origen" optional>
        {(control) => (
          <Controller
            control={form.control}
            name="facturaOrigenId"
            render={({ field }) => (
              <FacturaPicker
                soloConSaldo
                value={field.value ?? null}
                onChange={(id) => field.onChange(id)}
                proveedorId={proveedorIdSel || null}
                placeholder={
                  proveedorIdSel
                    ? 'Selecciona factura del proveedor…'
                    : 'Elige primero el proveedor'
                }
                disabled={!proveedorIdSel}
                triggerProps={control}
              />
            )}
          />
        )}
      </Field>

      {/* PLATFORM-TODO(<DevolucionProveedorPicker>): el catálogo de
          devoluciones 8.B es de Almacén y CxP no tiene query expuesta. El
          campo `devolucionAProveedorId` se oculta en la captura manual (CXP-17):
          un humano no puede capturar un GUID; lo llena el evento
          OcDevolucionRegistradaEvent. Volver a mostrarlo con un picker. */}

      <SheetFooter className="px-0">
        <Button
          type="button"
          variant="ghost"
          onClick={onCancel}
          disabled={crear.isPending}
        >
          Cancelar
        </Button>
        <Button type="submit" disabled={crear.isPending}>
          {crear.isPending ? 'Creando…' : 'Crear nota de cargo'}
        </Button>
      </SheetFooter>
    </form>
  );
}
