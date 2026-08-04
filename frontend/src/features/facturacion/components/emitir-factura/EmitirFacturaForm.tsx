import { useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '@/components/ui/tabs';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  EmitirFacturaSchema,
  type EmitirFacturaValues,
} from '@/features/facturacion/schemas/emitir-factura';
import {
  useEmisorDefaults,
  useEmitirFactura,
} from '@/features/facturacion/api/useFacturas';
import {
  ComportamientoFiscal,
  type ClienteLookupItem,
  type EmisorDefaultsResponse,
  type EmitirFacturaVentaResponse,
} from '@/features/facturacion/api/types';
import { cn } from '@/lib/utils';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import type { EmitirFacturaPrefill } from './prefill';
import { nullIfEmpty, valoresIniciales } from './valores';
import { TabEncabezado } from './TabEncabezado';
import { TabPosiciones } from './TabPosiciones';
import { TabTotales, type AnticipoAmortizar } from './TabTotales';

/**
 * <c>&lt;EmitirFacturaForm/&gt;</c> — form de emisión de CFDI 4.0 con
 * pestañas Encabezado / Posiciones / Totales (FAC-UX-PR2). Un solo
 * componente para ambos flujos: in-place desde el detalle del pedido
 * (con `prefill`, liga la factura vía `pedidoFacturableId`) y la página
 * de factura manual (`/facturacion/facturas/nueva`).
 *
 * <para>Las pestañas montan SIEMPRE su contenido (`forceMount`; Radix lo
 * oculta con `hidden`) para que el único estado de react-hook-form
 * conserve los inputs y los errores de zod de pestañas no visibles; el
 * trigger de cada pestaña muestra un badge con su conteo de errores al
 * fallar el submit.</para>
 *
 * <para>FAC-UX-PR3: los datos del emisor (RFC/régimen/sucursal) se
 * prellenan desde <c>useEmisorDefaults()</c> y los del receptor + claves
 * SAT/tasas por línea llegan en el prefill del pedido (masters de
 * Datos Maestros). Los fallbacks fiscales solo aplican cuando el master
 * no aporta el dato; el banner G12 avisa cuando el cliente está
 * incompleto.</para>
 *
 * <para>FAC-UX-PR4: cero GUIDs capturados a mano — receptor con
 * <c>&lt;ClienteSelector&gt;</c>, conceptos con
 * <c>&lt;ProductoAwSelector&gt;</c>, anticipos con
 * <c>&lt;AnticipoPicker&gt;</c> (folio + saldo) y autorización de activo
 * con <c>&lt;AutorizacionPicker&gt;</c>.</para>
 */
export interface EmitirFacturaFormProps {
  /** Precarga desde un pedido. Liga la factura al pedido (B2). */
  prefill?: EmitirFacturaPrefill;
  onSuccess: (res: EmitirFacturaVentaResponse) => void;
  /** Se invoca ya confirmado el descarte (el confirm-si-dirty vive aquí). */
  onCancel: () => void;
}

/** Campos del schema que viven en la pestaña Encabezado (badge de errores). */
const CAMPOS_ENCABEZADO = [
  'sucursalId',
  'rfcEmisor',
  'regimenFiscalEmisor',
  'receptorRfc',
  'receptorNombre',
  'receptorRegimenFiscal',
  'receptorCodigoPostal',
  'receptorUsoCfdi',
  'metodoPago',
  'formaPago',
  'moneda',
  'tipoCambio',
  'canalVenta',
  'comportamientoFiscal',
  'obraNombre',
  'cceTipoOperacion',
  'cceIncoterm',
  'cceTcDof',
  'cceReceptorNumRegIdTrib',
  'cceReceptorPaisResidencia',
  'cceClaveDePedimento',
  'cceCertificadoOrigen',
  'cceReceptorDomicilioCalle',
  'cceReceptorDomicilioEstado',
  'cceReceptorDomicilioCodigoPostal',
] as const;

export function EmitirFacturaForm(props: EmitirFacturaFormProps) {
  // Gate: los defaults del emisor deben existir ANTES de inicializar el
  // useForm (defaultValues se evalúa una sola vez por montaje).
  const defaults = useEmisorDefaults();

  if (defaults.isError) {
    return (
      <div className="space-y-2 rounded-md border border-destructive/40 bg-destructive/5 px-4 py-3 text-sm">
        <p>No se pudieron cargar los datos del emisor.</p>
        <Button type="button" variant="outline" size="sm" onClick={() => defaults.refetch()}>
          Reintentar
        </Button>
      </div>
    );
  }
  if (defaults.isLoading || defaults.data == null) {
    return (
      <div className="space-y-3">
        <div className="h-9 w-72 animate-pulse rounded bg-muted" />
        <div className="h-64 w-full animate-pulse rounded bg-muted" />
      </div>
    );
  }
  return <FormInner {...props} emisor={defaults.data} />;
}

function FormInner({
  prefill,
  onSuccess,
  onCancel,
  emisor,
}: EmitirFacturaFormProps & { emisor: EmisorDefaultsResponse }) {
  const idempotencyKey = useFormIdempotencyKey();
  const emitir = useEmitirFactura();
  // Detallado pt. 2: el receptor es SIEMPRE de solo lectura (fijo del
  // master de clientes); este permiso solo controla el CTA al catálogo
  // para corregirlo ahí.
  const puedeCorregirEnCatalogo = useHasPermission(
    PermisosCanonicos.FacturacionFacturasEditarReceptor,
  );
  // Detallado pt. 3: ídem para los datos fiscales del artículo por
  // posición — permiso independiente, CTA a Datos Maestros → Productos.
  const puedeCorregirArticulo = useHasPermission(
    PermisosCanonicos.FacturacionFacturasEditarArticulo,
  );

  const form = useForm<EmitirFacturaValues>({
    resolver: zodResolver(EmitirFacturaSchema),
    defaultValues: valoresIniciales(prefill, emisor),
  });

  // Anticipos a amortizar (relación 07). Estado local — no es parte del
  // schema del form porque son referencias a comprobantes ya emitidos.
  const [anticipos, setAnticipos] = useState<AnticipoAmortizar[]>([]);

  // Autorización del Contador General — obligatoria en venta de activo
  // fijo; se elige con <AutorizacionPicker/> (FAC-UX-PR4).
  const [autorizacionId, setAutorizacionId] = useState('');

  // Cliente de la factura (FAC-UX-PR4): del pedido en el flujo prellenado
  // o del <ClienteSelector/> en el manual. Habilita el picker de anticipos
  // y el autollenado fiscal del receptor.
  const [clienteId, setClienteId] = useState<string | null>(
    prefill?.clienteId ?? null,
  );

  function onClienteChange(item: ClienteLookupItem | null) {
    setClienteId(item?.id ?? null);
    // Cliente distinto → los anticipos elegidos ya no aplican.
    setAnticipos([]);
    if (item == null) return;
    const opts = { shouldDirty: true } as const;
    form.setValue('receptorNombre', item.razonSocial, opts);
    form.setValue('receptorRfc', item.rfc ?? '', opts);
    form.setValue('receptorRegimenFiscal', item.regimenFiscal ?? '', opts);
    form.setValue('receptorCodigoPostal', item.codigoPostalFiscal ?? '', opts);
    if (item.usoCfdiDefault) form.setValue('receptorUsoCfdi', item.usoCfdiDefault, opts);
    if (item.metodoPagoDefault) form.setValue('metodoPago', item.metodoPagoDefault, opts);
    if (item.formaPagoDefault) form.setValue('formaPago', item.formaPagoDefault, opts);
    // Receptor extranjero (CCE, Fase 2b): el encabezado de exportación hereda
    // tax id / país / domicilio del cliente — cero captura a mano. Solo aplican
    // si el comportamiento es ExportacionConCce.
    form.setValue('cceReceptorNumRegIdTrib', item.numRegIdTrib, opts);
    form.setValue('cceReceptorPaisResidencia', item.paisResidencia, opts);
    form.setValue('cceReceptorDomicilioCalle', item.domicilioExtranjeroCalle, opts);
    form.setValue('cceReceptorDomicilioEstado', item.domicilioExtranjeroEstado, opts);
    form.setValue('cceReceptorDomicilioCodigoPostal', item.domicilioExtranjeroCodigoPostal, opts);
  }

  const lineas = useWatch({ control: form.control, name: 'lineas' });
  const moneda = useWatch({ control: form.control, name: 'moneda' });
  // Receptor incompleto (cliente sin datos fiscales en el master, gap
  // G12) bloquea la emisión para todos: se corrige en Datos Maestros.
  const receptorFiscal = useWatch({
    control: form.control,
    name: [
      'receptorRfc',
      'receptorNombre',
      'receptorRegimenFiscal',
      'receptorCodigoPostal',
    ],
  });
  const receptorBloqueado = receptorFiscal.some(
    (v) => (v ?? '').trim() === '',
  );
  const comportamiento = useWatch({
    control: form.control,
    name: 'comportamientoFiscal',
  });
  const esCce = comportamiento === ComportamientoFiscal.ExportacionConCce;
  const esActivo = comportamiento === ComportamientoFiscal.VentaActivoFijo;
  // `valueAsNumber` da NaN al vaciar un input, y `NaN ?? 0` = NaN (?? solo
  // atrapa null/undefined) → el total mostraba "NaN". `finito` lo coerce a 0.
  const finito = (v: number | null | undefined) =>
    Number.isFinite(v) ? (v as number) : 0;
  const totales = (lineas ?? []).reduce(
    (acc, l) => {
      const base = Math.max(
        0,
        finito(l.cantidad) * finito(l.valorUnitario) - finito(l.descuento),
      );
      const iva = base * finito(l.tasaIvaTraslado);
      const retIva = base * finito(l.tasaRetencionIva);
      const retIsr = base * finito(l.tasaRetencionIsr);
      acc.subtotal += base;
      acc.iva += iva;
      acc.ret += retIva + retIsr;
      return acc;
    },
    { subtotal: 0, iva: 0, ret: 0 },
  );
  const total = totales.subtotal + totales.iva - totales.ret;

  const errores = form.formState.errors;
  const erroresEncabezado = CAMPOS_ENCABEZADO.filter(
    (campo) => errores[campo] != null,
  ).length;
  const erroresPosiciones =
    errores.lineas == null
      ? 0
      : Array.isArray(errores.lineas)
        ? errores.lineas.filter(Boolean).length
        : 1;

  function cancelar() {
    if (form.formState.isDirty) {
      const confirmar = window.confirm(
        'Tienes una emisión sin completar. ¿Descartar y cerrar?',
      );
      if (!confirmar) return;
    }
    onCancel();
  }

  function onSubmit(values: EmitirFacturaValues) {
    if (esActivo && autorizacionId.trim() === '') {
      toast.error(
        'La venta de activo fijo requiere el ID de autorización del Contador General.',
      );
      return;
    }
    emitir.mutate(
      {
        command: {
          sucursalId: values.sucursalId,
          receptorRfc: values.receptorRfc.toUpperCase(),
          receptorNombre: values.receptorNombre,
          receptorRegimenFiscal: values.receptorRegimenFiscal,
          receptorCodigoPostal: values.receptorCodigoPostal,
          receptorUsoCfdi: values.receptorUsoCfdi,
          // País derivado (sin campo en el form): MEX salvo exportación
          // CCE, donde manda el país de residencia del receptor.
          receptorPais: (esCce
            ? values.cceReceptorPaisResidencia?.trim() || 'MEX'
            : 'MEX'
          ).toUpperCase(),
          rfcEmisor: values.rfcEmisor.toUpperCase(),
          regimenFiscalEmisor: values.regimenFiscalEmisor,
          metodoPago: values.metodoPago,
          formaPago: values.formaPago,
          moneda: values.moneda.toUpperCase(),
          tipoCambio:
            values.moneda.toUpperCase() === 'MXN' ? null : values.tipoCambio,
          canalVenta: values.canalVenta,
          comportamientoFiscal: values.comportamientoFiscal,
          obraId: null,
          obraNombre: nullIfEmpty(values.obraNombre),
          facturaAgrupada: false,
          pedidoFacturableId: prefill?.pedidoFacturableId ?? null,
          anticipos:
            anticipos.filter((a) => a.anticipoId.trim().length > 0).length > 0
              ? anticipos.filter((a) => a.anticipoId.trim().length > 0)
              : null,
          autorizacionId: esActivo ? autorizacionId.trim() : null,
          cce: esCce
            ? {
                tipoOperacion: values.cceTipoOperacion ?? '2',
                incoterm: values.cceIncoterm ?? '',
                tcDof: values.cceTcDof ?? 0,
                receptorNumRegIdTrib: values.cceReceptorNumRegIdTrib ?? '',
                receptorPaisResidencia: values.cceReceptorPaisResidencia ?? '',
                claveDePedimento: nullIfEmpty(
                  values.cceClaveDePedimento,
                )?.toUpperCase() ?? null,
                certificadoOrigen: values.cceCertificadoOrigen,
                receptorDomicilioCalle: nullIfEmpty(
                  values.cceReceptorDomicilioCalle,
                ),
                receptorDomicilioEstado: values.cceReceptorDomicilioEstado ?? '',
                receptorDomicilioCodigoPostal:
                  values.cceReceptorDomicilioCodigoPostal ?? '',
                lineas: values.lineas.map((l) => ({
                  fraccionArancelaria: l.fraccionArancelaria ?? '',
                  unidadAduana: l.unidadAduana ?? '',
                  cantidadAduana: l.cantidadAduana ?? 0,
                  valorUnitarioAduana: l.valorUnitarioAduana ?? 0,
                  valorDolares: l.valorDolares ?? 0,
                  aplicaIva0: l.aplicaIva0,
                })),
              }
            : null,
          lineas: values.lineas.map((l) => ({
            productoId: nullIfEmpty(l.productoId),
            claveProdServSat: l.claveProdServSat,
            descripcion: l.descripcion,
            claveUnidadSat: l.claveUnidadSat,
            cantidad: l.cantidad,
            valorUnitario: l.valorUnitario,
            descuento: l.descuento,
            objetoImp: l.objetoImp,
            tasaIvaTraslado: l.tasaIvaTraslado,
            tasaRetencionIva: l.tasaRetencionIva,
            tasaRetencionIsr: l.tasaRetencionIsr,
            requierePedimento: l.requierePedimento,
          })),
        },
        idempotencyKey,
      },
      {
        onSuccess: (res) => {
          toast.success(
            res.uuid
              ? `Factura ${res.folio} timbrada · UUID ${res.uuid.slice(0, 8)}…`
              : `Factura ${res.folio} emitida (${res.estado})`,
          );
          onSuccess(res);
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
              description:
                error.problem.detail ??
                (error.traceId ? `Código: ${error.traceId}` : undefined),
            });
            return;
          }
          toast.error('Error inesperado al emitir la factura.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} noValidate className="space-y-4">
      {prefill?.pedidoFacturableId && (
        <div className="rounded-md border border-primary/30 bg-primary/5 px-3 py-2 text-sm">
          Facturando un pedido — líneas, datos comerciales y fiscales vienen
          precargados desde el pedido y los catálogos. Revisa y completa lo
          que falte. Al timbrar, el pedido pasa a{' '}
          <span className="font-medium">Facturado</span>.
        </div>
      )}

      <Tabs defaultValue="encabezado">
        <TabsList>
          <TabsTrigger value="encabezado">
            Encabezado
            <BadgeErrores cuenta={erroresEncabezado} />
          </TabsTrigger>
          <TabsTrigger value="posiciones">
            Posiciones
            <BadgeErrores cuenta={erroresPosiciones} />
          </TabsTrigger>
          <TabsTrigger value="totales">Totales</TabsTrigger>
        </TabsList>

        {/* forceMount: el form es uno solo — los inputs de pestañas
            inactivas siguen montados (Radix los oculta con `hidden`)
            para que RHF conserve valores y muestre errores de zod. */}
        <TabsContent value="encabezado" forceMount>
          <TabEncabezado
            form={form}
            emisor={emisor}
            puedeCorregirEnCatalogo={puedeCorregirEnCatalogo}
            receptorBloqueado={receptorBloqueado}
            esCce={esCce}
            esActivo={esActivo}
            autorizacionId={autorizacionId}
            onAutorizacionIdChange={setAutorizacionId}
            clienteId={clienteId}
            onClienteChange={onClienteChange}
            clienteLabel={prefill?.receptorNombre}
            clienteFijo={Boolean(prefill?.pedidoFacturableId && prefill?.clienteId)}
            canalVentaNombre={prefill?.canalVentaNombre}
          />
        </TabsContent>
        <TabsContent value="posiciones" forceMount>
          <TabPosiciones
            form={form}
            esCce={esCce}
            tasaIvaDefault={emisor.tasaIvaDefault}
            desdePedido={Boolean(prefill?.lineas?.length)}
            puedeCorregirArticulo={puedeCorregirArticulo}
          />
        </TabsContent>
        <TabsContent value="totales" forceMount>
          <TabTotales
            totales={totales}
            total={total}
            moneda={moneda}
            clienteId={clienteId}
            anticipos={anticipos}
            onAnticiposChange={setAnticipos}
          />
        </TabsContent>
      </Tabs>

      <div className="flex flex-wrap items-center justify-between gap-3 border-t pt-4">
        <dl className="flex gap-4 text-sm tabular-nums">
          <div>
            <dt className="text-xs text-muted-foreground">Subtotal</dt>
            <dd className="font-medium">{totales.subtotal.toFixed(2)}</dd>
          </div>
          <div>
            <dt className="text-xs text-muted-foreground">IVA</dt>
            <dd className="font-medium">{totales.iva.toFixed(2)}</dd>
          </div>
          <div>
            <dt className="text-xs text-muted-foreground">Retenciones</dt>
            <dd className="font-medium">−{totales.ret.toFixed(2)}</dd>
          </div>
          <div>
            <dt className="text-xs text-muted-foreground">Total</dt>
            <dd className="font-semibold">
              {total.toFixed(2)} {moneda?.toUpperCase()}
            </dd>
          </div>
        </dl>
        <div className="flex items-center gap-2">
          <Button
            type="button"
            variant="ghost"
            onClick={cancelar}
            disabled={emitir.isPending}
          >
            Cancelar
          </Button>
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
        </div>
      </div>
    </form>
  );
}

function BadgeErrores({ cuenta }: { cuenta: number }) {
  if (cuenta === 0) return null;
  return (
    <span
      className={cn(
        'inline-flex h-4 min-w-4 items-center justify-center rounded-full',
        'bg-destructive px-1 text-[10px] font-semibold text-destructive-foreground',
      )}
      aria-label={`${cuenta} errores`}
    >
      {cuenta}
    </span>
  );
}
