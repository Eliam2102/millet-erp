import { SucursalSelector } from '@/components/erp';
import { ResumenImportes } from '@/components/erp/display/ResumenImportes';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import type { ClienteLookupItem, FacturaCobrablePpdItem } from '@/features/facturacion/api/types';
import { useEmitirRepp } from '@/features/facturacion/api/useRepp';
import { ErrorTimbradoAviso } from '@/features/facturacion/components/ErrorTimbradoAviso';
import { FacturaPpdPicker } from '@/features/facturacion/components/FacturaPpdPicker';
import { ClienteSelector } from '@/features/facturacion/components/selectors/ClienteSelector';
import { mostrarErrorReceptorFiscal } from '@/features/facturacion/lib/mostrar-error-receptor-fiscal';
import {
  EmitirReppSchema,
  type EmitirReppValues,
} from '@/features/facturacion/schemas/emitir-repp';
import { applyServerErrors, esApiError, useFormIdempotencyKey } from '@/lib/api';
import { hoyLocalISO } from '@/lib/datetime';
import { cn } from '@/lib/utils';
import { zodResolver } from '@hookform/resolvers/zod';
import { Plus, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Controller, useFieldArray, useForm, useWatch } from 'react-hook-form';
import { toast } from 'sonner';
import { useEmisorDefaults } from '../api/useFacturas';
import { VistaPreviaButton } from '../components/vista-previa/VistaPreviaButton';

/**
 * <c>&lt;NuevoRepp/&gt;</c> — emisión de complemento de pago (Pago 2.0,
 * FE-F6). Cabecera del pago + facturas que cubre (inline), elegidas con
 * <c>FacturaPpdPicker</c> (saldo por cobrar neto, [Decisión 13-K]; cierra
 * PLATFORM-TODO(&lt;FacturaPicker&gt;)). Primero se elige el cliente y su
 * RFC acota las facturas PPD del picker (regla <c>REPP_MULTIPLES_CLIENTES</c>,
 * tarea 86e3tfd8k); cambiarlo limpia las facturas elegidas. Al elegir factura
 * se propone su saldo como importe.
 */
export interface NuevoReppProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange?: (dirty: boolean) => void;
}

function defaultFactura() {
  return { facturaVentaId: '', importePagado: 0 };
}

/** RFC genéricos del SAT: varios receptores comparten el mismo RFC. */
const RFC_GENERICOS = new Set(['XAXX010101000', 'XEXX010101000']);

const VALORES_INICIALES: EmitirReppValues = {
  clienteId: '',
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
  const emisor = useEmisorDefaults();

  const form = useForm<EmitirReppValues>({
    resolver: zodResolver(EmitirReppSchema),
    defaultValues: VALORES_INICIALES,
  });

  const { fields, append, remove, replace } = useFieldArray({
    control: form.control,
    name: 'facturas',
  });

  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange?.(isDirty);
  }, [isDirty, onDirtyChange]);

  const monedaPago = useWatch({ control: form.control, name: 'monedaPago' });
  const facturasWatch = useWatch({ control: form.control, name: 'facturas' });
  const totalPago = (facturasWatch ?? []).reduce((acc, f) => acc + (f.importePagado ?? 0), 0);

  // Items elegidos en el picker, por id de factura (para saldo/cliente).
  const [elegidas, setElegidas] = useState<Record<string, FacturaCobrablePpdItem>>({});

  // Todas las facturas de un REPP son del mismo cliente: el elegido en la
  // cabecera. Su RFC acota los pickers (sin cliente no se consulta nada).
  const [cliente, setCliente] = useState<ClienteLookupItem | null>(null);
  const receptorRfc = (cliente?.rfc ?? '').trim().toUpperCase() || null;
  const rfcGenerico = receptorRfc != null && RFC_GENERICOS.has(receptorRfc);
  const idsElegidos = (facturasWatch ?? []).map((f) => f.facturaVentaId).filter(Boolean);

  function onCambiarCliente(item: ClienteLookupItem | null) {
    if ((item?.id ?? null) === (cliente?.id ?? null)) return;
    setCliente(item);
    form.setValue('clienteId', item?.id ?? '', { shouldDirty: true, shouldValidate: true });
    // Las facturas elegidas eran del cliente anterior: se descartan.
    replace([defaultFactura()]);
    setElegidas({});
  }

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
          if (mostrarErrorReceptorFiscal(error)) return;
          if (esApiError(error)) {
            if (
              applyServerErrors(form as unknown as Parameters<typeof applyServerErrors>[0], error)
            ) {
              return;
            }
            // El aviso en el formulario (ErrorTimbradoAviso) muestra el motivo en lenguaje claro; sin toast técnico duplicado.
            return;
          }
          toast.error('Error inesperado al emitir el REPP.');
        },
      },
    );
  }

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      className="flex min-h-0 flex-1 flex-col overflow-hidden"
    >
      <div className="min-h-0 flex-1 space-y-5 overflow-y-auto py-5">
        <ErrorTimbradoAviso error={emitir.error} />
        <section className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <div className="sm:col-span-2">
            <Campo
              label="Cliente"
              required
              error={form.formState.errors.clienteId?.message}
              hint={
                cliente != null && receptorRfc == null
                  ? 'El cliente no tiene RFC capturado; corrígelo en el catálogo de clientes.'
                  : 'Sólo se listan sus facturas PPD timbradas con saldo por cobrar.'
              }
            >
              <ClienteSelector value={cliente?.id ?? null} onChange={onCambiarCliente} />
            </Campo>
          </div>
          {rfcGenerico && (
            <div className="sm:col-span-2">
              <AvisoEnLinea tono="advertencia">
                RFC genérico: se mostrarán las PPD de todos los receptores con este RFC.
              </AvisoEnLinea>
            </div>
          )}
          <Campo label="Sucursal" required error={form.formState.errors.sucursalId?.message}>
            <Controller
              name="sucursalId"
              control={form.control}
              render={({ field }) => (
                <SucursalSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? '')}
                />
              )}
            />
          </Campo>
          <Campo label="Fecha de pago" required error={form.formState.errors.fechaPago?.message}>
            <Input type="date" {...form.register('fechaPago')} />
          </Campo>
          <Campo
            label="Forma de pago"
            required
            error={form.formState.errors.formaPagoReal?.message}
            hint="Clave SAT c_FormaPago (03 transferencia…)."
          >
            <Input maxLength={5} {...form.register('formaPagoReal')} />
          </Campo>
          <Campo label="Moneda del pago" required error={form.formState.errors.monedaPago?.message}>
            <Input className="uppercase" maxLength={3} {...form.register('monedaPago')} />
          </Campo>
          <Campo
            label="Tipo de cambio"
            error={form.formState.errors.tcPago?.message}
            hint="Solo si no es MXN."
          >
            <Input
              type="number"
              step="0.0001"
              min="0"
              {...form.register('tcPago', { setValueAs: numeroONull })}
            />
          </Campo>
          <Campo label="Referencia de pago" error={form.formState.errors.referenciaPago?.message}>
            <Input placeholder="Opcional" {...form.register('referenciaPago')} />
          </Campo>
          <Campo label="Cuenta ordenante" error={form.formState.errors.cuentaOrdenante?.message}>
            <Input placeholder="Opcional" {...form.register('cuentaOrdenante')} />
          </Campo>
          <Campo
            label="Cuenta beneficiaria"
            error={form.formState.errors.cuentaBeneficiaria?.message}
          >
            <Input placeholder="Opcional" {...form.register('cuentaBeneficiaria')} />
          </Campo>
        </section>

        <section className="space-y-2">
          <header className="flex items-center justify-between">
            <h3 className="text-sm font-medium">Facturas cubiertas ({fields.length})</h3>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => append(defaultFactura())}
              disabled={receptorRfc == null}
            >
              <Plus className="mr-1 h-3 w-3" />
              Agregar factura
            </Button>
          </header>
          {receptorRfc == null && (
            <p className="text-[11px] leading-tight text-ink-muted">
              Elige primero el cliente para ver sus facturas PPD con saldo.
            </p>
          )}
          <div className="space-y-2">
            {fields.map((field, index) => {
              const errs = form.formState.errors.facturas?.[index];
              const idActual = facturasWatch?.[index]?.facturaVentaId ?? '';
              const elegida = idActual !== '' ? elegidas[idActual] : undefined;
              return (
                <div
                  key={field.id}
                  className={cn(
                    'grid grid-cols-1 gap-2 rounded-md border border-dashed border-brand/40 bg-brand/5 p-3 sm:grid-cols-12',
                    errs && 'border-danger/50',
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
                      <p className="text-xs text-danger-fg">{errs.facturaVentaId.message}</p>
                    )}
                  </div>
                  <div className="sm:col-span-3">
                    <Label className="text-xs">Importe pagado *</Label>
                    <Input
                      aria-invalid={!!errs?.importePagado}
                      type="number"
                      step="0.01"
                      min="0"
                      {...form.register(`facturas.${index}.importePagado` as const, {
                        valueAsNumber: true,
                      })}
                    />
                    {elegida != null && (
                      <p className="text-[11px] leading-tight text-ink-muted">
                        Saldo por cobrar {elegida.saldo.toFixed(2)} {elegida.moneda} · parcialidad{' '}
                        {elegida.numParcialidadSiguiente}
                        {` · Saldo restante ${(elegida.saldo - (Number.isFinite(facturasWatch?.[index]?.importePagado) ? facturasWatch[index].importePagado : 0)).toFixed(2)} ${elegida.moneda}`}
                        {elegida.acreditadoNc > 0
                          ? ` · NC aplicadas −${elegida.acreditadoNc.toFixed(2)}`
                          : ''}
                      </p>
                    )}
                    {errs?.importePagado && (
                      <p className="text-xs text-danger-fg">{errs.importePagado.message}</p>
                    )}
                  </div>
                  <div className="flex items-end justify-end sm:col-span-2">
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      onClick={() => remove(index)}
                      disabled={fields.length <= 1}
                      className="text-danger-fg hover:bg-danger-bg/10"
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
            <p className="text-xs text-danger-fg">{form.formState.errors.facturas.message}</p>
          )}
        </section>
      </div>
      <div className="shrink-0 bg-surface-card py-4">
        <ResumenImportes
          moneda={monedaPago.toUpperCase()}
          total={totalPago}
          acciones={
            <>
              <div className="flex items-center gap-2">
                <Button
                  type="button"
                  variant="ghost"
                  onClick={() => onClose()}
                  disabled={emitir.isPending}
                >
                  Cancelar
                </Button>
                <VistaPreviaButton
                  motivo={
                    !receptorRfc
                      ? 'Elige el cliente del pago.'
                      : !idsElegidos.length
                        ? 'Agrega una factura al pago.'
                        : !emisor.data
                          ? 'Espera a que se carguen los datos del emisor.'
                          : undefined
                  }
                  obtenerDatos={() => {
                    const v = form.getValues();
                    return {
                      tipo: 'REP',
                      emisor: `${emisor.data?.razonSocialEmisor} · ${emisor.data?.rfcEmisor} · ${emisor.data?.regimenFiscalEmisor}`,
                      receptor: `${cliente?.razonSocial} · ${receptorRfc}`,
                      usoCfdi: 'CP01',
                      metodoPago: 'No aplica (complemento de pago)',
                      formaPago: v.formaPagoReal,
                      moneda: v.monedaPago,
                      partidas: v.facturas
                        .filter((f) => elegidas[f.facturaVentaId])
                        .map((f) => ({
                          descripcion: `${elegidas[f.facturaVentaId].folio} · Saldo anterior ${elegidas[f.facturaVentaId].saldo.toFixed(2)} · Saldo restante ${(elegidas[f.facturaVentaId].saldo - f.importePagado).toFixed(2)}`,
                          cantidad: 1,
                          importe: f.importePagado,
                        })),
                      importes: [{ etiqueta: 'Total del pago', monto: totalPago }],
                    };
                  }}
                />
                <Button type="submit" disabled={emitir.isPending}>
                  {emitir.isPending ? 'Emitiendo…' : 'Emitir y timbrar'}
                </Button>
              </div>
            </>
          }
        />
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
        {required && <span className="ml-1 text-danger-fg">*</span>}
      </Label>
      {children}
      {hint != null && <p className="text-[11px] leading-tight text-ink-muted">{hint}</p>}
      {error != null && <p className="text-xs text-danger-fg">{error}</p>}
    </div>
  );
}
