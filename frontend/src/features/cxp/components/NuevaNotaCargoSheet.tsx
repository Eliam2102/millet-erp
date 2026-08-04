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
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
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
import { ProveedorSelector, SucursalSelector } from '@/components/erp';
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
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Crear nota de cargo</SheetTitle>
          <SheetDescription>
            Cargo interno contra el proveedor (devoluciones, garantías,
            fletes). Folio NCG asignado automáticamente. Requiere
            autorización Dirección antes de aplicarse.
          </SheetDescription>
        </SheetHeader>
        {open && <Form onClose={() => onOpenChange(false)} />}
      </SheetContent>
    </Sheet>
  );
}

function Form({ onClose }: { onClose: () => void }) {
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
  const proveedorIdSel = useWatch({ control: form.control, name: 'proveedorId' });

  function onSubmit(values: CrearNotaCargoValues) {
    crear.mutate(
      { command: values, idempotencyKey },
      {
        onSuccess: (res) => {
          toast.success(`Nota de cargo ${res.folio} creada (Borrador)`);
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
          toast.error('Error al crear nota de cargo.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
      <Campo
        label="Proveedor"
        error={form.formState.errors.proveedorId?.message}
        required
      >
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

      <Campo label="Sucursal (opcional)">
        <Controller
          control={form.control}
          name="sucursalId"
          render={({ field }) => (
            <SucursalSelector
              value={field.value ?? null}
              onChange={(id) => field.onChange(id)}
            />
          )}
        />
      </Campo>

      <Campo
        label="Concepto"
        error={form.formState.errors.concepto?.message}
        required
      >
        <Textarea
          rows={3}
          placeholder="Ej.: Reembolso por flete duplicado, OC 12345."
          {...form.register('concepto')}
        />
      </Campo>

      <div className="grid grid-cols-2 gap-3">
        <Campo
          label="Monto"
          error={form.formState.errors.monto?.message}
          required
        >
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register('monto', { valueAsNumber: true })}
          />
        </Campo>
        <Campo label="Moneda" required>
          <Input
            placeholder="MXN"
            maxLength={3}
            className="uppercase"
            {...form.register('moneda')}
          />
        </Campo>
      </div>

      <Campo label="Factura origen (opcional)">
        <Controller
          control={form.control}
          name="facturaOrigenId"
          render={({ field }) => (
            <FacturaPicker
              value={field.value ?? null}
              onChange={(id) => field.onChange(id)}
              proveedorId={proveedorIdSel || null}
              placeholder={
                proveedorIdSel
                  ? 'Selecciona factura del proveedor…'
                  : 'Elige primero el proveedor'
              }
              disabled={!proveedorIdSel}
            />
          )}
        />
      </Campo>

      <Campo label="Devolución a proveedor (UUID, opcional)">
        {/* PLATFORM-TODO(<DevolucionProveedorPicker>): el catálogo de
            devoluciones 8.B es de Almacén y CxP no tiene query expuesta;
            sigue como GUID crudo (normalmente lo llena el evento
            OcDevolucionRegistradaEvent, no un humano). */}
        <Input
          placeholder="00000000-…"
          {...form.register('devolucionAProveedorId')}
        />
      </Campo>

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
          {crear.isPending ? 'Creando…' : 'Crear nota de cargo'}
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
