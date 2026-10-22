import { FormaPagoSelector, SucursalSelector, UsoCfdiSelector } from '@/components/erp';
import { ResumenImportes } from '@/components/erp/display/ResumenImportes';
import { MonedaSelector } from '@/components/erp/selectors/MonedaSelector';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  METODOS_PAGO,
  TipoAnticipo,
  type EmisorDefaultsResponse,
} from '@/features/facturacion/api/types';
import { useEmitirAnticipo } from '@/features/facturacion/api/useAnticipos';
import { useSesionActual } from '@/features/facturacion/api/useCajaSesiones';
import { useEmisorDefaults } from '@/features/facturacion/api/useFacturas';
import { EmisorInfoBar } from '@/features/facturacion/components/EmisorInfoBar';
import { ErrorTimbradoAviso } from '@/features/facturacion/components/ErrorTimbradoAviso';
import {
  ReceptorFiscalInfo,
  ReceptorIncompletoBanner,
} from '@/features/facturacion/components/ReceptorFiscalInfo';
import { ClienteSelector } from '@/features/facturacion/components/selectors/ClienteSelector';
import { mostrarErrorReceptorFiscal } from '@/features/facturacion/lib/mostrar-error-receptor-fiscal';
import {
  AnticipoSchema,
  desglosarTotalAnticipo,
  type AnticipoValues,
} from '@/features/facturacion/schemas/anticipo';
import { applyServerErrors, esApiError, useFormIdempotencyKey } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { zodResolver } from '@hookform/resolvers/zod';
import { useEffect } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { toast } from 'sonner';
import { VistaPreviaButton } from '../components/vista-previa/VistaPreviaButton';

/**
 * <c>&lt;NuevoAnticipo/&gt;</c> — emisión de factura de anticipo (serie
 * FANT, FE-F4). El anticipo es nominal (no admite genérico).
 *
 * <para>Emisor y receptor son SIEMPRE de solo lectura (mismo patrón que
 * la emisión de factura): el emisor fijo de la empresa
 * (<c>&lt;EmisorInfoBar/&gt;</c>) y el receptor fijo del master de
 * clientes (<c>&lt;ReceptorFiscalInfo/&gt;</c>, autollenado por el
 * <c>&lt;ClienteSelector/&gt;</c>). Cliente incompleto (gap G12) →
 * bloqueo con CTA al catálogo gated por permiso.</para>
 */
export interface NuevoAnticipoProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange?: (dirty: boolean) => void;
}

const VALORES_INICIALES: AnticipoValues = {
  sucursalId: '',
  clienteId: '',
  receptorRfc: '',
  receptorNombre: '',
  receptorRegimenFiscal: '',
  receptorCodigoPostal: '',
  receptorUsoCfdi: 'G03',
  receptorPais: 'MEX',
  rfcEmisor: '',
  regimenFiscalEmisor: '',
  metodoPago: 'PUE',
  formaPago: '01',
  moneda: 'MXN',
  tipoCambio: null,
  tipoAnticipo: TipoAnticipo.ClientesMxp,
  total: 0,
  tasaIvaTraslado: 0.16,
  descripcion: null,
  obraNombre: null,
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

/** Caja abierta del cajero de la que se toma la sucursal (86e3tfd80). */
interface CajaAbierta {
  sucursalId: string;
  cajaNombre: string;
}

export function NuevoAnticipo(props: NuevoAnticipoProps) {
  // Gate: los defaults del emisor y la sesión de caja deben existir ANTES
  // de inicializar el useForm (defaultValues se evalúa una sola vez por montaje).
  const defaults = useEmisorDefaults();
  // Sin permiso de caja no se consulta la sesión (el endpoint respondería 403).
  const operaCaja = useHasPermission(PermisosCanonicos.FacturacionCajaOperar);
  const sesionActual = useSesionActual({ enabled: operaCaja });

  if (defaults.isError) {
    return (
      <div className="space-y-2 rounded-md border border-danger/40 bg-danger-bg/5 px-4 py-3 text-sm">
        <p>No se pudieron cargar los datos del emisor.</p>
        <Button type="button" variant="outline" size="sm" onClick={() => defaults.refetch()}>
          Reintentar
        </Button>
      </div>
    );
  }
  if (defaults.isLoading || defaults.data == null || sesionActual.isLoading) {
    return (
      <div className="space-y-3">
        <div className="h-9 w-72 animate-pulse rounded bg-surface-muted" />
        <div className="h-64 w-full animate-pulse rounded bg-surface-muted" />
      </div>
    );
  }
  // La sucursal se toma de la caja solo si está abierta y operable (mismo
  // criterio que el cobro en caja); si no, o si falla la consulta, se deja elegir.
  const sesion = operaCaja ? sesionActual.data?.sesion : null;
  const cajaAbierta: CajaAbierta | null =
    sesion != null && sesion.estado === 'Abierta' && !sesionActual.data?.diaAnteriorPendiente
      ? { sucursalId: sesion.sucursalId, cajaNombre: sesion.cajaNombre }
      : null;
  return <FormInner {...props} emisor={defaults.data} cajaAbierta={cajaAbierta} />;
}

function FormInner({
  onClose,
  onDirtyChange,
  emisor,
  cajaAbierta,
}: NuevoAnticipoProps & { emisor: EmisorDefaultsResponse; cajaAbierta: CajaAbierta | null }) {
  const idempotencyKey = useFormIdempotencyKey();
  const emitir = useEmitirAnticipo();
  // El receptor es de solo lectura; el permiso solo controla el CTA al
  // catálogo de clientes (mismo patrón que la emisión de factura).
  const puedeCorregirEnCatalogo = useHasPermission(
    PermisosCanonicos.FacturacionFacturasEditarReceptor,
  );

  const form = useForm<AnticipoValues>({
    resolver: zodResolver(AnticipoSchema),
    defaultValues: {
      ...VALORES_INICIALES,
      rfcEmisor: emisor.rfcEmisor,
      regimenFiscalEmisor: emisor.regimenFiscalEmisor,
      sucursalId: cajaAbierta?.sucursalId ?? emisor.sucursalIdDefault ?? '',
      tasaIvaTraslado: emisor.tasaIvaDefault ?? VALORES_INICIALES.tasaIvaTraslado,
    },
  });

  const [totalActual, tasaActual, monedaActual] = useWatch({
    control: form.control,
    name: ['total', 'tasaIvaTraslado', 'moneda'],
  });
  // 86e3tfd7j: se captura el total con IVA; subtotal e IVA se derivan.
  const { base, iva } = desglosarTotalAnticipo(totalActual, tasaActual);
  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange?.(isDirty);
  }, [isDirty, onDirtyChange]);

  const [clienteId, receptorRfc, receptorNombre, receptorRegimen, receptorCp] = useWatch({
    control: form.control,
    name: [
      'clienteId',
      'receptorRfc',
      'receptorNombre',
      'receptorRegimenFiscal',
      'receptorCodigoPostal',
    ],
  });
  // Receptor incompleto en el master → se corrige en Datos Maestros, no
  // aquí: la emisión del anticipo queda bloqueada.
  const receptorBloqueado = [receptorRfc, receptorNombre, receptorRegimen, receptorCp].some(
    (v) => (v ?? '').trim() === '',
  );

  function onSubmit(values: AnticipoValues) {
    emitir.mutate(
      {
        command: {
          sucursalId: values.sucursalId,
          clienteId: values.clienteId,
          receptorRfc: values.receptorRfc.toUpperCase(),
          receptorNombre: values.receptorNombre,
          receptorRegimenFiscal: values.receptorRegimenFiscal,
          receptorCodigoPostal: values.receptorCodigoPostal,
          receptorUsoCfdi: values.receptorUsoCfdi,
          // Sin campo en el form: el anticipo es nominal nacional.
          receptorPais: 'MEX',
          rfcEmisor: values.rfcEmisor.toUpperCase(),
          regimenFiscalEmisor: values.regimenFiscalEmisor,
          metodoPago: values.metodoPago,
          formaPago: values.formaPago,
          moneda: values.moneda.toUpperCase(),
          tipoCambio: values.tipoCambio,
          tipoAnticipo: values.tipoAnticipo,
          // 86e3tfd7j: se manda el total; el backend desglosa (excluyente con montoBase).
          montoBase: 0,
          total: values.total,
          tasaIvaTraslado: values.tasaIvaTraslado,
          descripcion: nullIfEmpty(values.descripcion),
          pedidoFacturableId: null,
          pedidoOrigenRef: null,
          obraId: null,
          obraNombre: nullIfEmpty(values.obraNombre),
        },
        idempotencyKey,
      },
      {
        onSuccess: (res) => {
          toast.success(
            res.uuid
              ? `Anticipo ${res.folio} timbrado · saldo ${res.saldo.toFixed(2)}`
              : `Anticipo ${res.folio} emitido (${res.estado})`,
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
          toast.error('Error inesperado al emitir el anticipo.');
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
        {/* Emisor fijo (datos de la empresa) — solo informativo. */}
        <EmisorInfoBar emisor={emisor} />

        <Seccion titulo="Receptor (nominal)">
          <Campo label="Cliente" required error={form.formState.errors.clienteId?.message}>
            <Controller
              name="clienteId"
              control={form.control}
              render={({ field }) => (
                <ClienteSelector
                  value={field.value || null}
                  onChange={(item) => {
                    field.onChange(item?.id ?? '');
                    if (item == null) return;
                    const opts = { shouldDirty: true } as const;
                    form.setValue('receptorNombre', item.razonSocial, opts);
                    form.setValue('receptorRfc', item.rfc ?? '', opts);
                    form.setValue('receptorRegimenFiscal', item.regimenFiscal ?? '', opts);
                    form.setValue('receptorCodigoPostal', item.codigoPostalFiscal ?? '', opts);
                    if (item.usoCfdiDefault)
                      form.setValue('receptorUsoCfdi', item.usoCfdiDefault, opts);
                    if (item.metodoPagoDefault)
                      form.setValue('metodoPago', item.metodoPagoDefault, opts);
                    if (item.formaPagoDefault)
                      form.setValue('formaPago', item.formaPagoDefault, opts);
                  }}
                />
              )}
            />
          </Campo>
          <div className="sm:col-span-2">
            <ReceptorFiscalInfo
              valores={{
                rfc: receptorRfc,
                nombre: receptorNombre,
                regimenFiscal: receptorRegimen,
                codigoPostal: receptorCp,
              }}
              clienteId={clienteId || null}
              puedeCorregirEnCatalogo={puedeCorregirEnCatalogo}
            />
          </div>
          {receptorBloqueado && (
            <ReceptorIncompletoBanner
              clienteId={clienteId || null}
              puedeCorregirEnCatalogo={puedeCorregirEnCatalogo}
              className="sm:col-span-2"
            />
          )}
          <Campo label="Uso CFDI" required error={form.formState.errors.receptorUsoCfdi?.message}>
            <Controller
              name="receptorUsoCfdi"
              control={form.control}
              render={({ field }) => (
                <UsoCfdiSelector
                  value={field.value || null}
                  onChange={(value) => field.onChange(value ?? '')}
                />
              )}
            />
          </Campo>
        </Seccion>

        <Seccion titulo="Anticipo y pago">
          <Campo
            label="Sucursal"
            required
            error={form.formState.errors.sucursalId?.message}
            hint={
              cajaAbierta
                ? `Tomada de tu caja abierta: ${cajaAbierta.cajaNombre}.`
                : 'No tienes caja abierta; elige la sucursal.'
            }
          >
            <Controller
              name="sucursalId"
              control={form.control}
              render={({ field }) => (
                <SucursalSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? '')}
                  disabled={cajaAbierta != null}
                />
              )}
            />
          </Campo>
          <Campo label="Tipo de anticipo" required>
            <select
              className="h-9 w-full rounded-md border border-line-control bg-transparent px-3 text-sm shadow-sm"
              {...form.register('tipoAnticipo', { valueAsNumber: true })}
            >
              <option value={TipoAnticipo.ClientesMxp}>Clientes MXP</option>
              <option value={TipoAnticipo.ClientesUsd}>Clientes USD</option>
            </select>
          </Campo>
          <Campo
            label="Total (con IVA)"
            required
            error={form.formState.errors.total?.message}
            hint="El subtotal y el IVA se calculan del total."
          >
            <Input
              type="number"
              step="0.01"
              min="0"
              {...form.register('total', { valueAsNumber: true })}
            />
          </Campo>
          <Campo label="Tasa IVA" error={form.formState.errors.tasaIvaTraslado?.message}>
            <Input
              type="number"
              step="0.01"
              min="0"
              max="1"
              placeholder="0.16"
              {...form.register('tasaIvaTraslado', { setValueAs: numeroONull })}
            />
          </Campo>
          <Campo label="Método de pago" required>
            <select
              className="h-9 w-full rounded-md border border-line-control bg-transparent px-3 text-sm shadow-sm"
              {...form.register('metodoPago')}
            >
              {METODOS_PAGO.map((m) => (
                <option key={m.value} value={m.value}>
                  {m.label}
                </option>
              ))}
            </select>
          </Campo>
          <Campo label="Forma de pago" required error={form.formState.errors.formaPago?.message}>
            <Controller
              name="formaPago"
              control={form.control}
              render={({ field }) => (
                <FormaPagoSelector
                  value={field.value || null}
                  onChange={(value) => field.onChange(value ?? '')}
                />
              )}
            />
          </Campo>
          <Campo label="Moneda" required error={form.formState.errors.moneda?.message}>
            <Controller
              name="moneda"
              control={form.control}
              render={({ field }) => (
                <MonedaSelector
                  value={field.value || null}
                  onChange={(value) => field.onChange(value ?? '')}
                />
              )}
            />
          </Campo>
          <Campo
            label="Tipo de cambio"
            error={form.formState.errors.tipoCambio?.message}
            hint="Solo si no es MXN."
          >
            <Input
              type="number"
              step="0.0001"
              min="0"
              {...form.register('tipoCambio', { setValueAs: numeroONull })}
            />
          </Campo>
          <div className="sm:col-span-2">
            <Campo label="Descripción" error={form.formState.errors.descripcion?.message}>
              <Input placeholder="Opcional" maxLength={1000} {...form.register('descripcion')} />
            </Campo>
          </div>
        </Seccion>
      </div>
      <div className="shrink-0 bg-surface-card py-4">
        <ResumenImportes
          moneda={monedaActual}
          subtotal={base}
          ivaTrasladado={iva}
          total={base + iva}
          acciones={
            <>
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
                  !clienteId || receptorBloqueado
                    ? 'Selecciona un receptor con datos fiscales.'
                    : base <= 0
                      ? 'Captura el total del anticipo.'
                      : undefined
                }
                obtenerDatos={() => {
                  const v = form.getValues();
                  return {
                    tipo: 'Anticipo',
                    emisor: `${emisor.razonSocialEmisor} · ${v.rfcEmisor} · ${v.regimenFiscalEmisor}`,
                    receptor: `${v.receptorNombre} · ${v.receptorRfc} · ${v.receptorRegimenFiscal} · CP ${v.receptorCodigoPostal}`,
                    usoCfdi: v.receptorUsoCfdi,
                    metodoPago: v.metodoPago,
                    formaPago: v.formaPago,
                    moneda: v.moneda,
                    partidas: [
                      { descripcion: v.descripcion || 'Anticipo', cantidad: 1, importe: base },
                    ],
                    importes: [
                      { etiqueta: 'Subtotal', monto: base },
                      { etiqueta: 'IVA trasladado', monto: iva },
                      { etiqueta: 'Total', monto: base + iva },
                    ],
                  };
                }}
              />
              <Button
                type="submit"
                disabled={emitir.isPending || receptorBloqueado}
                title={
                  receptorBloqueado
                    ? 'Receptor incompleto: complétalo en Datos Maestros → Clientes'
                    : undefined
                }
              >
                {emitir.isPending ? 'Emitiendo…' : 'Emitir y timbrar'}
              </Button>
            </>
          }
        />
      </div>
    </form>
  );
}

function Seccion({ titulo, children }: { titulo: string; children: React.ReactNode }) {
  return (
    <section className="space-y-2">
      <h3 className="text-sm font-medium">{titulo}</h3>
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">{children}</div>
    </section>
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
