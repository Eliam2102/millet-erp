import { hoyLocalISO } from '@/lib/datetime';
import { useEffect, useMemo, useState } from 'react';
import { Controller, useFieldArray, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigate } from '@tanstack/react-router';
import { Download, Plus, Trash2 } from 'lucide-react';
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
  CapturarFacturaSchema,
  type CapturarFacturaValues,
} from '@/features/cxp/schemas/factura';
import { useCapturarFacturaConOc } from '@/features/cxp/api/useFacturas';
import {
  EstadoPasivo,
  MotivoCancelacion,
  type CfdiListItem,
} from '@/features/cxp/api/types';
import { CfdiPorProcesarPicker } from '@/features/cxp/components/CfdiPorProcesarPicker';
import { useCfdiParseado } from '@/features/cxp/api/useCfdis';
import {
  abrirPdfCfdi,
  descargarXmlCfdi,
} from '@/features/cxp/lib/cfdi-archivos';
import {
  ArticuloSelector,
  LineaOcSelector,
  OrdenCompraSelector,
} from '@/components/erp';
import { mapById, useSucursales } from '@/features/catalogos/api';
import { resetLineaOcIds } from '@/features/cxp/lib/reset-linea-oc';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;CapturarFacturaSheet/&gt;</c> — slide-from-right para capturar
 * factura contra OC (FE-F2-PR1, doc 07 §FE-F2-PR1). Pantalla flagship
 * del módulo. Doc 05 §6.1 propone split izquierda XML / derecha campos;
 * en este MVP la columna izquierda queda con metadatos del CFDI (no
 * preview XML porque backend no expone <c>GET /cfdis/{id}/xml</c>).
 *
 * <para>Comportamiento sobre exceso de tolerancia: el backend devuelve
 * <c>201 Created</c> con <c>estado=Cancelada, motivoCancelacion=
 * RechazadaPorTolerancia</c>. El sheet detecta este caso y muestra
 * <c>toast.warning</c> + cierra; la bandeja se invalida igual.</para>
 *
 * <para>La OC se elige con <c>&lt;OrdenCompraSelector/&gt;</c>; al
 * seleccionarla se derivan proveedor y sucursal del resumen (la OC es
 * dueña de ambos) y se muestran read-only. El payload sigue enviando los
 * 3 ids (ahora derivados) → la validación <c>OC_PROVEEDOR_MISMATCH</c>
 * del backend queda intacta como red de seguridad.</para>
 *
 * <para>PLATFORM-TODO(&lt;CfdiXmlViewer&gt;): cuando el backend exponga
 * descarga de XML, agregar columna izquierda con el viewer + auto-fill
 * de campos desde el XML parseado (Subtotal, IVA, lineas, etc.).</para>
 */
export interface CapturarFacturaSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /**
   * CFDI pre-seleccionado (opcional). Si viene, el sheet inicializa los
   * campos relevantes desde su metadata.
   */
  cfdiPreseleccionado?: {
    id: string;
    uuidCfdi: string;
    folio: string | null;
    serie: string | null;
    fechaCfdi: string;
    total: number;
    moneda: string;
    // Importes de la ingesta (opcionales para compatibilidad): permiten
    // pre-llenar subtotal/IVA/retenciones/TC sin re-parsear el XML.
    subtotal?: number;
    impuestosTrasladados?: number;
    retenciones?: number;
    tipoCambio?: number | null;
  } | null;
}

function defaultLinea() {
  return {
    articuloId: null,
    descripcion: '',
    claveProdServ: null,
    cantidad: 1,
    claveUnidad: 'PZA',
    unidad: null,
    precioUnitario: 0,
    importe: 0,
    descuento: null,
    lineaOcId: null,
  };
}

export function CapturarFacturaSheet({
  open,
  onOpenChange,
  cfdiPreseleccionado,
}: CapturarFacturaSheetProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const capturar = useCapturarFacturaConOc();
  const navigate = useNavigate();

  const form = useForm<CapturarFacturaValues>({
    resolver: zodResolver(CapturarFacturaSchema),
    defaultValues: buildDefaults(cfdiPreseleccionado ?? null),
  });

  const { fields, append, remove, replace } = useFieldArray({
    control: form.control,
    name: 'lineas',
  });

  // Sucursal: catálogo eager (carga completa) → mapById robusto, sin cap.
  // Su etiqueta read-only no tiene el bug del proveedor.
  const sucursalesQuery = useSucursales();
  const sucursalesMap = useMemo(
    () => mapById(sucursalesQuery.data?.items),
    [sucursalesQuery.data],
  );

  // Nombre del proveedor de la OC elegida: lo trae el resumen
  // (proveedorNombre, resuelto server-side, ADR-0042) y lo guardamos del
  // onSelect. Ya NO se resuelve contra un catálogo capado a 500 (causa del
  // bug de GUID en proveedores fuera del tope).
  const [proveedorNombreSel, setProveedorNombreSel] = useState<string | null>(
    null,
  );

  // CFDI vinculado desde el picker interno (cuando el sheet se abre sin
  // cfdiPreseleccionado, i.e. desde la bandeja de Facturas). Guarda el item
  // completo para la metadata del aside; el prop tiene precedencia.
  const [cfdiSel, setCfdiSel] = useState<CfdiListItem | null>(null);
  const cfdiBase = cfdiPreseleccionado ?? cfdiSel;

  function handleOpenChange(nextOpen: boolean) {
    if (!nextOpen) setCfdiSel(null);
    onOpenChange(nextOpen);
  }

  function vincularCfdi(cfdi: CfdiListItem | null) {
    setCfdiSel(cfdi);
    form.setValue('cfdiRecibidoId', cfdi?.id ?? null);
    form.setValue('uuidCfdi', cfdi?.uuidCfdi ?? null);
    form.setValue('folioProveedor', cfdi?.folio ?? null);
    form.setValue('serieProveedor', cfdi?.serie ?? null);
    if (cfdi) {
      form.setValue('fechaDocumento', cfdi.fechaCfdi.slice(0, 10));
      form.setValue('moneda', cfdi.moneda);
      form.setValue('subtotal', cfdi.subtotal);
      form.setValue('impuestosTrasladados', cfdi.impuestosTrasladados);
      form.setValue('retenciones', cfdi.retenciones);
      form.setValue('tipoCambio', cfdi.tipoCambio ?? null);
      form.setValue('total', cfdi.total, { shouldValidate: true });
    }
  }

  // Detalle re-parseado del XML (líneas de cfdi:Concepto) para importar
  // partidas con un click en lugar de teclearlas.
  const cfdiParseadoQuery = useCfdiParseado(cfdiBase?.id ?? null);

  function importarLineasCfdi() {
    const lineas = cfdiParseadoQuery.data?.lineas;
    if (!lineas || lineas.length === 0) return;
    replace(
      lineas.map((l) => ({
        articuloId: null,
        descripcion: l.descripcion,
        claveProdServ: l.claveProdServ || null,
        cantidad: l.cantidad,
        claveUnidad: l.claveUnidad,
        unidad: l.unidad,
        precioUnitario: l.valorUnitario,
        importe: l.importe,
        descuento: l.descuento,
        // El vínculo a la línea de OC no viene en el CFDI: lo asigna el
        // usuario después de importar (three-way match).
        lineaOcId: null,
      })),
    );
    toast.success(`${lineas.length} línea(s) importadas del CFDI`);
  }

  // useWatch (no form.watch) para que el componente siga memoizable por el
  // React Compiler — form.watch dispara react-hooks/incompatible-library.
  const ordenCompraIdSel = useWatch({
    control: form.control,
    name: 'ordenCompraId',
  });
  const proveedorIdSel = useWatch({ control: form.control, name: 'proveedorId' });
  const sucursalIdSel = useWatch({ control: form.control, name: 'sucursalId' });
  const proveedorLabel = proveedorIdSel
    ? (proveedorNombreSel ?? proveedorIdSel)
    : '';
  const sucursalSel = sucursalIdSel ? sucursalesMap.get(sucursalIdSel) : undefined;
  const sucursalLabel = sucursalIdSel
    ? sucursalSel
      ? `${sucursalSel.clave} · ${sucursalSel.nombre}`
      : sucursalIdSel
    : '';

  // El CFDI del picker se limpia al CERRAR (handleOpenChange), no aquí:
  // setState directo en effect dispara react-hooks/set-state-in-effect.
  useEffect(() => {
    if (open) {
      form.reset(buildDefaults(cfdiPreseleccionado ?? null));
      // No reseteamos proveedorNombreSel aquí: la etiqueta está guardada por
      // proveedorIdSel (que queda en '' tras el reset) → un nombre viejo nunca
      // se muestra. El onSelect/onChange lo mantienen en sync con la OC.
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, cfdiPreseleccionado?.id]);

  function onSubmit(values: CapturarFacturaValues) {
    capturar.mutate(
      {
        command: {
          ...values,
          fechaDocumento: toIso(values.fechaDocumento),
          fechaContabilizacion: toIso(values.fechaContabilizacion),
          tipoCambio: values.tipoCambio ?? null,
          lineas: values.lineas.map((l) => ({
            articuloId: l.articuloId ?? null,
            claveProdServ: l.claveProdServ ?? null,
            descripcion: l.descripcion,
            cantidad: l.cantidad,
            claveUnidad: l.claveUnidad,
            unidad: l.unidad ?? null,
            precioUnitario: l.precioUnitario,
            importe: l.importe,
            descuento: l.descuento ?? null,
            lineaOcId: l.lineaOcId ?? null,
            // PLATFORM-TODO(<ConceptoContableCatalogo>): sin catálogo de
            // conceptos contables (Contabilidad) sigue en null.
            conceptoContableId: null,
          })),
        },
        idempotencyKey,
      },
      {
        onSuccess: (response) => {
          if (
            response.estado === EstadoPasivo.Cancelada &&
            response.motivoCancelacion === MotivoCancelacion.RechazadaPorTolerancia
          ) {
            toast.warning('Factura rechazada por exceder tolerancia', {
              description: `Diferencia contra OC: ${response.diferenciaContraOc.toFixed(2)}. La factura se cancela automáticamente.`,
            });
          } else {
            toast.success('Factura capturada');
          }
          handleOpenChange(false);
          navigate({
            to: '/cxp/facturas/$id',
            params: { id: response.id },
          });
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
              description: error.traceId
                ? `Código: ${error.traceId}`
                : undefined,
            });
            return;
          }
          toast.error('Error inesperado al capturar la factura.');
        },
      },
    );
  }

  const isPending = capturar.isPending;

  return (
    <Sheet open={open} onOpenChange={handleOpenChange}>
      <SheetContent side="right" className="w-full max-w-3xl sm:max-w-3xl">
        <SheetHeader>
          <SheetTitle>Capturar factura desde OC</SheetTitle>
          <SheetDescription>
            Concilia un CFDI recibido con una OC autorizada. El backend valida
            tolerancia del proveedor; si excede, la factura se cancela
            automáticamente.
          </SheetDescription>
        </SheetHeader>

        <form
          onSubmit={form.handleSubmit(onSubmit)}
          className="grid grid-cols-1 gap-4 px-4 lg:grid-cols-[260px_1fr]"
        >
          {/* Columna izquierda: metadata del CFDI seleccionado */}
          <aside className="space-y-2 rounded-md border bg-muted/30 p-3 text-xs">
            <p className="font-semibold uppercase tracking-wide text-muted-foreground">
              CFDI base
            </p>
            {!cfdiPreseleccionado && (
              <CfdiPorProcesarPicker
                value={cfdiSel?.id ?? null}
                onSelect={vincularCfdi}
              />
            )}
            {cfdiBase ? (
              <dl className="space-y-1">
                <Metadato label="UUID" valor={cfdiBase.uuidCfdi} mono />
                <Metadato
                  label="Folio"
                  valor={
                    cfdiBase.serie
                      ? `${cfdiBase.serie}-${cfdiBase.folio ?? ''}`
                      : (cfdiBase.folio ?? '—')
                  }
                />
                <Metadato
                  label="Fecha CFDI"
                  valor={cfdiBase.fechaCfdi.slice(0, 10)}
                />
                <Metadato
                  label="Total CFDI"
                  valor={`${cfdiBase.total.toFixed(2)} ${cfdiBase.moneda}`}
                />
              </dl>
            ) : (
              <p className="text-muted-foreground italic">
                Sin CFDI base. Vincula uno con el selector de arriba, captura
                manualmente, o inicia desde la bandeja de CFDIs con la acción
                "Capturar factura".
              </p>
            )}
            {cfdiBase && (
              <div className="flex flex-wrap gap-2 border-t pt-2">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() =>
                    descargarXmlCfdi(cfdiBase.id, cfdiBase.uuidCfdi).catch(
                      (error) =>
                        toast.error(
                          esApiError(error)
                            ? error.problem.title
                            : 'Error al descargar el XML.',
                        ),
                    )
                  }
                >
                  <Download className="mr-1 h-3 w-3" /> XML
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() =>
                    abrirPdfCfdi(cfdiBase.id).catch((error) =>
                      toast.error(
                        esApiError(error)
                          ? error.problem.title
                          : 'Error al abrir el PDF.',
                      ),
                    )
                  }
                >
                  Ver PDF
                </Button>
              </div>
            )}
          </aside>

          {/* Columna derecha: campos editables */}
          <div className="space-y-4">
            <section className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <Campo
                label="Orden de compra"
                error={form.formState.errors.ordenCompraId?.message}
                required
              >
                <Controller
                  control={form.control}
                  name="ordenCompraId"
                  render={({ field }) => (
                    <OrdenCompraSelector
                      value={field.value || null}
                      onChange={(id) => {
                        field.onChange(id ?? '');
                        // Al limpiar la OC, limpiamos también lo derivado.
                        if (!id) {
                          form.setValue('proveedorId', '', {
                            shouldValidate: true,
                          });
                          form.setValue('sucursalId', '', {
                            shouldValidate: true,
                          });
                          setProveedorNombreSel(null);
                          // Las líneas ya no tienen OC de la cual vincular.
                          resetLineaOcIds(form.getValues, form.setValue);
                        }
                      }}
                      onSelect={(oc) => {
                        // La OC es dueña de proveedor y sucursal: los
                        // derivamos del resumen. El usuario no los teclea.
                        form.setValue('proveedorId', oc.proveedorId, {
                          shouldValidate: true,
                        });
                        form.setValue('sucursalId', oc.sucursalId, {
                          shouldValidate: true,
                        });
                        // Nombre del proveedor para la etiqueta read-only, ya
                        // resuelto en el resumen (sin catálogo capado).
                        setProveedorNombreSel(oc.proveedorNombre);
                        // Cambió la OC: los lineaOcId previos quedan stale
                        // (apuntan a líneas de la OC vieja) y la guarda backend
                        // los rechazaría → resetear todos a null.
                        resetLineaOcIds(form.getValues, form.setValue);
                      }}
                    />
                  )}
                />
              </Campo>
              <Campo
                label="Proveedor (de la OC)"
                error={form.formState.errors.proveedorId?.message}
              >
                <Input
                  value={proveedorLabel}
                  placeholder="Se deriva de la OC"
                  readOnly
                  disabled
                  aria-label="Proveedor derivado de la orden de compra"
                />
              </Campo>
              <Campo
                label="Sucursal (de la OC)"
                error={form.formState.errors.sucursalId?.message}
              >
                <Input
                  value={sucursalLabel}
                  placeholder="Se deriva de la OC"
                  readOnly
                  disabled
                  aria-label="Sucursal derivada de la orden de compra"
                />
              </Campo>
              <Campo
                label="Moneda"
                error={form.formState.errors.moneda?.message}
                required
              >
                <Input
                  placeholder="MXN"
                  maxLength={3}
                  className="uppercase"
                  {...form.register('moneda', {
                    setValueAs: (v) => (typeof v === 'string' ? v.toUpperCase() : v),
                  })}
                />
              </Campo>
              <Campo
                label="Serie"
                error={form.formState.errors.serieProveedor?.message}
              >
                <Input placeholder="A" {...form.register('serieProveedor')} />
              </Campo>
              <Campo
                label="Folio"
                error={form.formState.errors.folioProveedor?.message}
              >
                <Input placeholder="12345" {...form.register('folioProveedor')} />
              </Campo>
              <Campo
                label="Fecha documento"
                error={form.formState.errors.fechaDocumento?.message}
                required
              >
                <Input type="date" {...form.register('fechaDocumento')} />
              </Campo>
              <Campo
                label="Fecha contabilización"
                error={form.formState.errors.fechaContabilizacion?.message}
                required
              >
                <Input
                  type="date"
                  {...form.register('fechaContabilizacion')}
                />
              </Campo>
              <Campo
                label="Fecha vencimiento"
                error={form.formState.errors.fechaVencimiento?.message}
                required
              >
                <Input type="date" {...form.register('fechaVencimiento')} />
              </Campo>
              <Campo
                label="Tipo de cambio"
                error={form.formState.errors.tipoCambio?.message}
              >
                <Input
                  type="number"
                  step="0.0001"
                  min="0"
                  placeholder="1.0000"
                  {...form.register('tipoCambio', { valueAsNumber: true })}
                />
              </Campo>
            </section>

            <section className="grid grid-cols-2 gap-3 sm:grid-cols-5">
              <Campo
                label="Subtotal"
                error={form.formState.errors.subtotal?.message}
                required
              >
                <Input
                  type="number"
                  step="0.01"
                  min="0"
                  {...form.register('subtotal', { valueAsNumber: true })}
                />
              </Campo>
              <Campo
                label="Descuentos"
                error={form.formState.errors.descuentos?.message}
              >
                <Input
                  type="number"
                  step="0.01"
                  min="0"
                  {...form.register('descuentos', { valueAsNumber: true })}
                />
              </Campo>
              <Campo
                label="IVA trasladado"
                error={form.formState.errors.impuestosTrasladados?.message}
              >
                <Input
                  type="number"
                  step="0.01"
                  min="0"
                  {...form.register('impuestosTrasladados', {
                    valueAsNumber: true,
                  })}
                />
              </Campo>
              <Campo
                label="Retenciones"
                error={form.formState.errors.retenciones?.message}
              >
                <Input
                  type="number"
                  step="0.01"
                  min="0"
                  {...form.register('retenciones', { valueAsNumber: true })}
                />
              </Campo>
              <Campo
                label="Total"
                error={form.formState.errors.total?.message}
                required
              >
                <Input
                  type="number"
                  step="0.01"
                  min="0"
                  {...form.register('total', { valueAsNumber: true })}
                />
              </Campo>
            </section>

            <section className="space-y-2">
              <header className="flex items-center justify-between">
                <h3 className="text-sm font-medium">
                  Líneas ({fields.length})
                </h3>
                <div className="flex items-center gap-1">
                  {(cfdiParseadoQuery.data?.lineas.length ?? 0) > 0 && (
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={importarLineasCfdi}
                    >
                      <Download className="mr-1 h-3 w-3" />
                      Importar {cfdiParseadoQuery.data!.lineas.length} línea(s)
                      del CFDI
                    </Button>
                  )}
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={() => append(defaultLinea())}
                  >
                    <Plus className="mr-1 h-3 w-3" /> Agregar línea
                  </Button>
                </div>
              </header>

              {fields.length === 0 && (
                <p className="rounded-md border border-dashed px-3 py-4 text-center text-xs text-muted-foreground">
                  Aún no agregas líneas. Mínimo una para capturar.
                </p>
              )}

              <div className="space-y-2">
                {fields.map((field, index) => (
                  <LineaInline
                    key={field.id}
                    index={index}
                    onRemove={() => remove(index)}
                    form={form}
                    ordenCompraId={ordenCompraIdSel || null}
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
                onClick={() => handleOpenChange(false)}
                disabled={isPending}
              >
                Cancelar
              </Button>
              <Button type="submit" disabled={isPending}>
                {isPending ? 'Capturando…' : 'Capturar factura'}
              </Button>
            </SheetFooter>
          </div>
        </form>
      </SheetContent>
    </Sheet>
  );
}

function buildDefaults(
  cfdi: CapturarFacturaSheetProps['cfdiPreseleccionado'] | null,
): CapturarFacturaValues {
  const hoy = hoyLocalISO();
  return {
    ordenCompraId: '',
    proveedorId: '',
    sucursalId: '',
    cfdiRecibidoId: cfdi?.id ?? null,
    uuidCfdi: cfdi?.uuidCfdi ?? null,
    folioProveedor: cfdi?.folio ?? null,
    serieProveedor: cfdi?.serie ?? null,
    fechaDocumento: cfdi?.fechaCfdi?.slice(0, 10) ?? hoy,
    fechaContabilizacion: hoy,
    fechaVencimiento: hoy,
    moneda: cfdi?.moneda ?? 'MXN',
    tipoCambio: cfdi?.tipoCambio ?? null,
    subtotal: cfdi?.subtotal ?? 0,
    descuentos: 0,
    impuestosTrasladados: cfdi?.impuestosTrasladados ?? 0,
    retenciones: cfdi?.retenciones ?? 0,
    total: cfdi?.total ?? 0,
    lineas: [defaultLinea()],
  };
}

function toIso(yyyymmdd: string): string {
  return `${yyyymmdd}T00:00:00Z`;
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

interface LineaInlineProps {
  index: number;
  onRemove: () => void;
  form: ReturnType<typeof useForm<CapturarFacturaValues>>;
  /** OC de cabecera; alimenta el LineaOcSelector dependiente. null → disabled. */
  ordenCompraId: string | null;
}

function LineaInline({ index, onRemove, form, ordenCompraId }: LineaInlineProps) {
  const lineaErrors = form.formState.errors.lineas?.[index];
  return (
    <div
      className={cn(
        'space-y-2 rounded-md border border-dashed p-3',
        lineaErrors && 'border-destructive/50',
      )}
    >
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-12">
        <div className="sm:col-span-6">
          <Label className="text-xs">Artículo (opcional)</Label>
          <Controller
            control={form.control}
            name={`lineas.${index}.articuloId` as const}
            render={({ field }) => (
              <ArticuloSelector
                value={field.value}
                onChange={(id) => field.onChange(id)}
                onSelect={(articulo) => {
                  // Heredar la descripción del catálogo solo si el campo
                  // sigue vacío — no pisar lo que el usuario ya tecleó.
                  const desc = form.getValues(
                    `lineas.${index}.descripcion` as const,
                  );
                  if (!desc) {
                    form.setValue(
                      `lineas.${index}.descripcion` as const,
                      articulo.nombre,
                      { shouldValidate: true },
                    );
                  }
                }}
                className="w-full"
              />
            )}
          />
        </div>
        <div className="sm:col-span-6">
          <Label className="text-xs">Descripción *</Label>
          <Input
            placeholder="Concepto del producto/servicio"
            {...form.register(`lineas.${index}.descripcion` as const)}
          />
          {lineaErrors?.descripcion && (
            <p className="text-xs text-destructive">
              {lineaErrors.descripcion.message}
            </p>
          )}
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">Cantidad *</Label>
          <Input
            type="number"
            step="0.0001"
            min="0"
            {...form.register(`lineas.${index}.cantidad` as const, {
              valueAsNumber: true,
            })}
          />
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">Unidad SAT *</Label>
          <Input
            placeholder="PZA / KGM"
            {...form.register(`lineas.${index}.claveUnidad` as const)}
          />
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">P. Unitario *</Label>
          <Input
            type="number"
            step="0.0001"
            min="0"
            {...form.register(`lineas.${index}.precioUnitario` as const, {
              valueAsNumber: true,
            })}
          />
        </div>
        <div className="sm:col-span-3">
          <Label className="text-xs">Importe</Label>
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register(`lineas.${index}.importe` as const, {
              valueAsNumber: true,
            })}
          />
        </div>
        <div className="sm:col-span-3">
          <Label className="text-xs">Descuento</Label>
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register(`lineas.${index}.descuento` as const, {
              valueAsNumber: true,
            })}
          />
        </div>
        <div className="sm:col-span-4">
          <Label className="text-xs">Línea OC (opcional)</Label>
          <Controller
            control={form.control}
            name={`lineas.${index}.lineaOcId` as const}
            render={({ field }) => (
              <LineaOcSelector
                ordenCompraId={ordenCompraId}
                value={field.value}
                onChange={(id) => field.onChange(id)}
                disabled={!ordenCompraId}
              />
            )}
          />
        </div>
        <div className="flex items-end sm:col-span-2">
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="text-destructive hover:bg-destructive/10"
            onClick={onRemove}
          >
            <Trash2 className="mr-1 h-3 w-3" /> Quitar
          </Button>
        </div>
      </div>
    </div>
  );
}

interface MetadatoProps {
  label: string;
  valor: string;
  mono?: boolean;
}

function Metadato({ label, valor, mono }: MetadatoProps) {
  return (
    <div>
      <dt className="text-muted-foreground">{label}</dt>
      <dd className={cn(mono && 'font-mono', 'break-all')}>{valor}</dd>
    </div>
  );
}
