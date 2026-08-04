import { hoyLocalISO } from '@/lib/datetime';
import { useState } from 'react';
import { Controller, useFieldArray, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Plus, Trash2, Upload } from 'lucide-react';
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
  CrearComprobacionCajaChicaSchema,
  type CrearComprobacionCajaChicaValues,
} from '@/features/cxp/schemas/comprobaciones';
import { useCrearComprobacionCajaChica } from '@/features/cxp/api/useComprobaciones';
import { fetchCfdiDetalle } from '@/features/cxp/api/useCfdis';
import type { CfdiListItem } from '@/features/cxp/api/types';
import { CfdiPorProcesarPicker } from '@/features/cxp/components/CfdiPorProcesarPicker';
import { CargarCfdiSheet } from '@/features/cxp/components/CargarCfdiSheet';
import { useProveedor } from '@/features/catalogos/api';
import {
  EmpleadoSelector,
  ProveedorSelector,
  SucursalSelector,
} from '@/components/erp';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;NuevaComprobacionCajaChicaSheet/&gt;</c> — captura de
 * comprobación Caja Chica con N CFDIs/tickets. Cada CFDI se persiste
 * como FacturaProveedor sin OC y se liga vía LineaComprobacionGastos.
 *
 * <para>Patrón "remount on open" para evitar setState en useEffect.</para>
 */
export interface NuevaComprobacionCajaChicaSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function NuevaComprobacionCajaChicaSheet({
  open,
  onOpenChange,
}: NuevaComprobacionCajaChicaSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-3xl">
        <SheetHeader>
          <SheetTitle>Nueva comprobación — Caja chica</SheetTitle>
          <SheetDescription>
            Captura los CFDIs de los gastos pagados con caja chica. Cada
            renglón se vuelve una factura sin OC para que afecte gasto/IVA/DIOT
            individualmente.
          </SheetDescription>
        </SheetHeader>
        {open && <Form onClose={() => onOpenChange(false)} />}
      </SheetContent>
    </Sheet>
  );
}

function defaultLinea() {
  const hoy = hoyLocalISO();
  return {
    cfdiRecibidoId: null,
    uuidCfdi: null,
    proveedorId: '',
    folioProveedor: null,
    serieProveedor: null,
    fechaCfdi: hoy,
    subtotal: 0,
    descuentos: 0,
    impuestosTrasladados: 0,
    retenciones: 0,
    total: 0,
    fechaVencimiento: hoy,
    concepto: null,
  };
}

function Form({ onClose }: { onClose: () => void }) {
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearComprobacionCajaChica();
  const hoy = hoyLocalISO();

  const form = useForm<CrearComprobacionCajaChicaValues>({
    resolver: zodResolver(CrearComprobacionCajaChicaSchema),
    defaultValues: {
      sucursalId: '',
      responsableId: '',
      fechaInicio: hoy,
      fechaFin: hoy,
      moneda: 'MXN',
      observaciones: null,
      destinoReposicion: 1,
      cfdis: [defaultLinea()],
    },
  });

  const { fields, append, remove } = useFieldArray({
    control: form.control,
    name: 'cfdis',
  });

  const puedeLeerCfdis = useHasPermission(
    PermisosCanonicos.CuentasPorPagarCfdisLeer,
  );
  const puedeCargarCfdi = useHasPermission(
    PermisosCanonicos.CuentasPorPagarCfdisCargarManual,
  );
  const [cargaXmlLinea, setCargaXmlLinea] = useState<number | null>(null);

  /**
   * Vincula el CFDI recibido a la línea y prellena UUID, serie/folio,
   * fecha e importes desde los metadatos persistidos (mismo patrón que
   * NuevaNotaCreditoSheet). <c>null</c> quita el vínculo sin borrar los
   * importes ya capturados.
   */
  function vincularCfdiEnLinea(index: number, cfdi: CfdiListItem | null) {
    form.setValue(`cfdis.${index}.cfdiRecibidoId`, cfdi?.id ?? null);
    form.setValue(`cfdis.${index}.uuidCfdi`, cfdi?.uuidCfdi ?? null, {
      shouldValidate: true,
    });
    if (!cfdi) return;
    form.setValue(`cfdis.${index}.serieProveedor`, cfdi.serie);
    form.setValue(`cfdis.${index}.folioProveedor`, cfdi.folio);
    form.setValue(`cfdis.${index}.fechaCfdi`, cfdi.fechaCfdi.slice(0, 10));
    form.setValue(`cfdis.${index}.subtotal`, cfdi.subtotal);
    form.setValue(
      `cfdis.${index}.impuestosTrasladados`,
      cfdi.impuestosTrasladados,
    );
    form.setValue(`cfdis.${index}.retenciones`, cfdi.retenciones);
    form.setValue(`cfdis.${index}.total`, cfdi.total, {
      shouldValidate: true,
    });
    const monedaComprobacion = (form.getValues('moneda') || '').toUpperCase();
    if (monedaComprobacion && cfdi.moneda.toUpperCase() !== monedaComprobacion) {
      toast.warning(
        `El CFDI está en ${cfdi.moneda} y la comprobación en ${monedaComprobacion}.`,
      );
    }
  }

  function onSubmit(values: CrearComprobacionCajaChicaValues) {
    crear.mutate(
      {
        command: {
          ...values,
          cfdis: values.cfdis.map((l) => ({
            ...l,
            fechaCfdi: `${l.fechaCfdi}T00:00:00Z`,
          })),
        },
        idempotencyKey,
      },
      {
        onSuccess: (res) => {
          toast.success(
            `Comprobación creada con ${res.numeroLineas} CFDIs (${res.montoTotal.toFixed(2)})`,
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
          toast.error('Error al crear comprobación.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
      <section className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <Campo
          label="Sucursal"
          required
          error={form.formState.errors.sucursalId?.message}
        >
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
        <Campo
          label="Responsable"
          required
          error={form.formState.errors.responsableId?.message}
        >
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
        <Campo label="Fecha inicio" required>
          <Input type="date" {...form.register('fechaInicio')} />
        </Campo>
        <Campo label="Fecha fin" required>
          <Input type="date" {...form.register('fechaFin')} />
        </Campo>
        <Campo label="Moneda" required>
          <Input
            placeholder="MXN"
            maxLength={3}
            className="uppercase"
            {...form.register('moneda')}
          />
        </Campo>
        <Campo
          label="Reponer a"
          required
          error={form.formState.errors.destinoReposicion?.message}
        >
          <Controller
            control={form.control}
            name="destinoReposicion"
            render={({ field }) => (
              <Select
                value={String(field.value)}
                onValueChange={(v) => field.onChange(Number(v))}
              >
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="1">Cuenta de la sucursal</SelectItem>
                  <SelectItem value="2">Responsable de la caja</SelectItem>
                </SelectContent>
              </Select>
            )}
          />
        </Campo>
        <Campo label="Observaciones">
          <Input placeholder="Opcional" {...form.register('observaciones')} />
        </Campo>
      </section>

      <section className="space-y-2">
        <header className="flex items-center justify-between">
          <h3 className="text-sm font-medium">CFDIs ({fields.length})</h3>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => append(defaultLinea())}
          >
            <Plus className="mr-1 h-3 w-3" />
            Agregar CFDI
          </Button>
        </header>

        {fields.length === 0 && (
          <p className="rounded-md border border-dashed px-3 py-4 text-center text-xs text-muted-foreground">
            Sin CFDIs. Agrega al menos uno.
          </p>
        )}

        <div className="space-y-2">
          {fields.map((field, index) => (
            <LineaInline
              key={field.id}
              index={index}
              form={form}
              onRemove={() => remove(index)}
              onVincular={(cfdi) => vincularCfdiEnLinea(index, cfdi)}
              onCargarXml={() => setCargaXmlLinea(index)}
              puedeLeerCfdis={puedeLeerCfdis}
              puedeCargarCfdi={puedeCargarCfdi}
            />
          ))}
        </div>

        {form.formState.errors.cfdis?.message && (
          <p className="text-xs text-destructive">
            {form.formState.errors.cfdis.message}
          </p>
        )}
      </section>

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
          {crear.isPending ? 'Creando…' : 'Crear comprobación'}
        </Button>
      </SheetFooter>

      {/* Sheet apilado: carga del XML (canal CargaManual) para la línea
          activa; al cargar se vincula y prellena igual que desde el picker. */}
      <CargarCfdiSheet
        open={cargaXmlLinea !== null}
        onOpenChange={(o) => {
          if (!o) setCargaXmlLinea(null);
        }}
        onCargado={(resp) => {
          const index = cargaXmlLinea;
          if (index === null) return;
          fetchCfdiDetalle(resp.id)
            .then((det) => vincularCfdiEnLinea(index, det))
            .catch(() => {
              // Fallback mínimo: vínculo + UUID; el resto a mano.
              form.setValue(`cfdis.${index}.cfdiRecibidoId`, resp.id);
              form.setValue(`cfdis.${index}.uuidCfdi`, resp.uuidCfdi, {
                shouldValidate: true,
              });
            });
        }}
      />
    </form>
  );
}

interface LineaInlineProps {
  index: number;
  form: ReturnType<typeof useForm<CrearComprobacionCajaChicaValues>>;
  onRemove: () => void;
  onVincular: (cfdi: CfdiListItem | null) => void;
  onCargarXml: () => void;
  puedeLeerCfdis: boolean;
  puedeCargarCfdi: boolean;
}

function LineaInline({
  index,
  form,
  onRemove,
  onVincular,
  onCargarXml,
  puedeLeerCfdis,
  puedeCargarCfdi,
}: LineaInlineProps) {
  const lineaErrors = form.formState.errors.cfdis?.[index];
  const cfdiRecibidoId = form.watch(`cfdis.${index}.cfdiRecibidoId` as const);
  const uuidCfdi = form.watch(`cfdis.${index}.uuidCfdi` as const);
  const proveedorId = form.watch(`cfdis.${index}.proveedorId` as const);
  // RFC del proveedor de la línea para acotar el picker a sus CFDIs
  // (mismo uso que la recepción variante A de Almacén).
  const proveedorQuery = useProveedor(proveedorId || null);
  return (
    <div
      className={cn(
        'space-y-2 rounded-md border border-dashed p-3',
        lineaErrors && 'border-destructive/50',
      )}
    >
      {puedeLeerCfdis && (
        <div className="flex gap-2">
          <CfdiPorProcesarPicker
            value={cfdiRecibidoId ?? null}
            onSelect={onVincular}
            rfcEmisor={proveedorQuery.data?.rfc}
            placeholder="Vincular CFDI recibido…"
            className="flex-1"
          />
          {puedeCargarCfdi && (
            <Button type="button" variant="outline" onClick={onCargarXml}>
              <Upload className="mr-1 h-4 w-4" aria-hidden="true" />
              Cargar XML
            </Button>
          )}
        </div>
      )}
      {uuidCfdi && (
        <p className="font-mono text-xs text-muted-foreground">
          UUID: {uuidCfdi}
        </p>
      )}
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-12">
        <div className="sm:col-span-4">
          <Label className="text-xs">Proveedor *</Label>
          <Controller
            control={form.control}
            name={`cfdis.${index}.proveedorId` as const}
            render={({ field }) => (
              <ProveedorSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
                className="w-full"
              />
            )}
          />
          {lineaErrors?.proveedorId && (
            <p className="text-xs text-destructive">
              {lineaErrors.proveedorId.message}
            </p>
          )}
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">Serie</Label>
          <Input
            placeholder="A"
            {...form.register(`cfdis.${index}.serieProveedor` as const)}
          />
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">Folio</Label>
          <Input
            placeholder="123"
            {...form.register(`cfdis.${index}.folioProveedor` as const)}
          />
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">Fecha *</Label>
          <Input
            type="date"
            {...form.register(`cfdis.${index}.fechaCfdi` as const)}
          />
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">Vence</Label>
          <Input
            type="date"
            {...form.register(`cfdis.${index}.fechaVencimiento` as const)}
          />
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">Subtotal</Label>
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register(`cfdis.${index}.subtotal` as const, {
              valueAsNumber: true,
            })}
          />
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">IVA</Label>
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register(`cfdis.${index}.impuestosTrasladados` as const, {
              valueAsNumber: true,
            })}
          />
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">Retenciones</Label>
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register(`cfdis.${index}.retenciones` as const, {
              valueAsNumber: true,
            })}
          />
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">Descuentos</Label>
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register(`cfdis.${index}.descuentos` as const, {
              valueAsNumber: true,
            })}
          />
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">Total *</Label>
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register(`cfdis.${index}.total` as const, {
              valueAsNumber: true,
            })}
          />
          {lineaErrors?.total && (
            <p className="text-xs text-destructive">
              {lineaErrors.total.message}
            </p>
          )}
        </div>
        <div className="sm:col-span-8">
          <Label className="text-xs">Concepto</Label>
          <Input
            placeholder="Detalle del gasto"
            {...form.register(`cfdis.${index}.concepto` as const)}
          />
        </div>
        <div className="flex items-end sm:col-span-2">
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={onRemove}
            className="text-destructive hover:bg-destructive/10"
          >
            <Trash2 className="mr-1 h-3 w-3" />
            Quitar
          </Button>
        </div>
      </div>
    </div>
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
