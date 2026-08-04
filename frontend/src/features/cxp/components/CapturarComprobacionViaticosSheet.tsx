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
import { Checkbox } from '@/components/ui/checkbox';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  CapturarComprobacionViaticosSchema,
  type CapturarComprobacionViaticosValues,
} from '@/features/cxp/schemas/viaticos';
import { useCapturarComprobacionViaticos } from '@/features/cxp/api/useViaticos';
import { fetchCfdiDetalle } from '@/features/cxp/api/useCfdis';
import type {
  CfdiListItem,
  SolicitudViaticosListItem,
} from '@/features/cxp/api/types';
import { CfdiPorProcesarPicker } from '@/features/cxp/components/CfdiPorProcesarPicker';
import { CargarCfdiSheet } from '@/features/cxp/components/CargarCfdiSheet';
import { useProveedor } from '@/features/catalogos/api';
import { ProveedorSelector } from '@/components/erp';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;CapturarComprobacionViaticosSheet/&gt;</c> — el empleado al
 * regreso captura sus gastos (CFDIs + tickets no fiscales). Cada línea
 * marca <c>esTicketNoFiscal</c> si no tiene CFDI (gasto pequeño tipo
 * caseta, propina, taxi).
 */
export interface CapturarComprobacionViaticosSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  solicitud: SolicitudViaticosListItem | null;
}

export function CapturarComprobacionViaticosSheet({
  open,
  onOpenChange,
  solicitud,
}: CapturarComprobacionViaticosSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-3xl">
        <SheetHeader>
          <SheetTitle>Comprobar viáticos</SheetTitle>
          <SheetDescription>
            {solicitud
              ? `${solicitud.destino} (${solicitud.fechaSalida} → ${solicitud.fechaRegreso}) · anticipo ${solicitud.montoSolicitado.toFixed(2)}`
              : 'Selecciona una solicitud.'}
          </SheetDescription>
        </SheetHeader>
        {open && solicitud && (
          <Form
            key={solicitud.id}
            solicitud={solicitud}
            onClose={() => onOpenChange(false)}
          />
        )}
      </SheetContent>
    </Sheet>
  );
}

function defaultLinea(moneda: string) {
  const hoy = hoyLocalISO();
  return {
    cfdiRecibidoId: null,
    uuidCfdi: null,
    proveedorId: null,
    folioProveedor: null,
    fechaGasto: hoy,
    subtotal: 0,
    impuestosTrasladados: 0,
    retenciones: 0,
    total: 0,
    moneda,
    concepto: '',
    esTicketNoFiscal: false,
  };
}

interface FormProps {
  solicitud: SolicitudViaticosListItem;
  onClose: () => void;
}

function Form({ solicitud, onClose }: FormProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const capturar = useCapturarComprobacionViaticos();

  const form = useForm<CapturarComprobacionViaticosValues>({
    resolver: zodResolver(CapturarComprobacionViaticosSchema),
    defaultValues: {
      lineas: [defaultLinea('MXN')],
    },
  });

  const { fields, append, remove } = useFieldArray({
    control: form.control,
    name: 'lineas',
  });

  const puedeLeerCfdis = useHasPermission(
    PermisosCanonicos.CuentasPorPagarCfdisLeer,
  );
  const puedeCargarCfdi = useHasPermission(
    PermisosCanonicos.CuentasPorPagarCfdisCargarManual,
  );
  const [cargaXmlLinea, setCargaXmlLinea] = useState<number | null>(null);

  /**
   * Vincula el CFDI recibido a la línea de gasto y prellena UUID, folio,
   * fecha, importes y moneda desde los metadatos persistidos (mismo
   * patrón que NuevaNotaCreditoSheet). <c>null</c> quita el vínculo sin
   * borrar los importes ya capturados. Sin permiso de CFDIs, el empleado
   * sigue capturando UUID y montos a mano como hasta ahora.
   */
  function vincularCfdiEnLinea(index: number, cfdi: CfdiListItem | null) {
    form.setValue(`lineas.${index}.cfdiRecibidoId`, cfdi?.id ?? null);
    form.setValue(`lineas.${index}.uuidCfdi`, cfdi?.uuidCfdi ?? null, {
      shouldValidate: true,
    });
    if (!cfdi) return;
    form.setValue(`lineas.${index}.esTicketNoFiscal`, false);
    form.setValue(`lineas.${index}.folioProveedor`, cfdi.folio);
    form.setValue(`lineas.${index}.fechaGasto`, cfdi.fechaCfdi.slice(0, 10));
    form.setValue(`lineas.${index}.subtotal`, cfdi.subtotal);
    form.setValue(
      `lineas.${index}.impuestosTrasladados`,
      cfdi.impuestosTrasladados,
    );
    form.setValue(`lineas.${index}.retenciones`, cfdi.retenciones);
    form.setValue(`lineas.${index}.total`, cfdi.total, {
      shouldValidate: true,
    });
    form.setValue(`lineas.${index}.moneda`, cfdi.moneda);
  }

  function onSubmit(values: CapturarComprobacionViaticosValues) {
    capturar.mutate(
      {
        id: solicitud.id,
        versionEsperada: solicitud.version,
        command: {
          lineas: values.lineas.map((l) => ({
            ...l,
            uuidCfdi: l.uuidCfdi || null,
            fechaGasto: `${l.fechaGasto}T00:00:00Z`,
          })),
        },
        idempotencyKey,
      },
      {
        onSuccess: (res) => {
          toast.success(
            `Comprobación capturada: ${res.numeroLineas} líneas, ${res.montoComprobado.toFixed(2)}.`,
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
          toast.error('Error al capturar comprobación.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
      <section className="space-y-2">
        <header className="flex items-center justify-between">
          <h3 className="text-sm font-medium">
            Líneas de gasto ({fields.length})
          </h3>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => append(defaultLinea('MXN'))}
          >
            <Plus className="mr-1 h-3 w-3" />
            Agregar línea
          </Button>
        </header>

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

        {form.formState.errors.lineas?.message && (
          <p className="text-xs text-destructive">
            {form.formState.errors.lineas.message}
          </p>
        )}
      </section>

      <SheetFooter className="px-0">
        <Button
          type="button"
          variant="ghost"
          onClick={onClose}
          disabled={capturar.isPending}
        >
          Cancelar
        </Button>
        <Button type="submit" disabled={capturar.isPending}>
          {capturar.isPending ? 'Capturando…' : 'Capturar comprobación'}
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
              form.setValue(`lineas.${index}.cfdiRecibidoId`, resp.id);
              form.setValue(`lineas.${index}.uuidCfdi`, resp.uuidCfdi, {
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
  form: ReturnType<typeof useForm<CapturarComprobacionViaticosValues>>;
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
  const lineaErrors = form.formState.errors.lineas?.[index];
  const cfdiRecibidoId = form.watch(`lineas.${index}.cfdiRecibidoId` as const);
  const esTicketNoFiscal =
    form.watch(`lineas.${index}.esTicketNoFiscal` as const) === true;
  const proveedorId = form.watch(`lineas.${index}.proveedorId` as const);
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
      {puedeLeerCfdis && !esTicketNoFiscal && (
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
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-12">
        <div className="sm:col-span-6">
          <Label className="text-xs">Concepto *</Label>
          <Input
            placeholder="Casetas Mty-CDMX / Cena con cliente / etc."
            {...form.register(`lineas.${index}.concepto` as const)}
          />
          {lineaErrors?.concepto && (
            <p className="text-xs text-destructive">
              {lineaErrors.concepto.message}
            </p>
          )}
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">Fecha *</Label>
          <Input
            type="date"
            {...form.register(`lineas.${index}.fechaGasto` as const)}
          />
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">Subtotal</Label>
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register(`lineas.${index}.subtotal` as const, {
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
            {...form.register(`lineas.${index}.impuestosTrasladados` as const, {
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
            {...form.register(`lineas.${index}.retenciones` as const, {
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
            {...form.register(`lineas.${index}.total` as const, {
              valueAsNumber: true,
            })}
          />
          {lineaErrors?.total && (
            <p className="text-xs text-destructive">
              {lineaErrors.total.message}
            </p>
          )}
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">Moneda</Label>
          <Input
            placeholder="MXN"
            maxLength={3}
            className="uppercase"
            {...form.register(`lineas.${index}.moneda` as const)}
          />
        </div>
        <div className="sm:col-span-3">
          <Label className="text-xs">UUID CFDI (opcional)</Label>
          <Input
            placeholder="00000000-…"
            readOnly={!!cfdiRecibidoId}
            className={cn('font-mono text-xs', cfdiRecibidoId && 'bg-muted')}
            {...form.register(`lineas.${index}.uuidCfdi` as const)}
          />
        </div>
        <div className="sm:col-span-3">
          <Label className="text-xs">Proveedor (opcional)</Label>
          <Controller
            control={form.control}
            name={`lineas.${index}.proveedorId` as const}
            render={({ field }) => (
              <ProveedorSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id)}
                className="w-full"
              />
            )}
          />
        </div>
        <div className="flex items-end gap-3 sm:col-span-2">
          <div className="flex items-center gap-2">
            <Checkbox
              id={`ticketNoFiscal-${index}`}
              checked={
                form.watch(`lineas.${index}.esTicketNoFiscal`) === true
              }
              onCheckedChange={(v) => {
                form.setValue(
                  `lineas.${index}.esTicketNoFiscal` as const,
                  v === true,
                );
                // Un ticket no fiscal no puede referenciar CFDI (regla
                // del backend); al marcarlo se quita el vínculo.
                if (v === true) {
                  form.setValue(
                    `lineas.${index}.cfdiRecibidoId` as const,
                    null,
                  );
                  form.setValue(`lineas.${index}.uuidCfdi` as const, null);
                }
              }}
            />
            <Label
              htmlFor={`ticketNoFiscal-${index}`}
              className="text-xs"
            >
              No fiscal
            </Label>
          </div>
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
