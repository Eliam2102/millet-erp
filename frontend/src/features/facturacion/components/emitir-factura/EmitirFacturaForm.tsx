import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { ResumenImportes } from '@/components/erp/display/ResumenImportes';
import { Button } from '@/components/ui/button';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { EmbarqueCeResumen } from '@/features/comercio-exterior/components/EmbarqueCeResumen';
import {
  ComportamientoFiscal,
  type ClienteLookupItem,
  type EmisorDefaultsResponse,
  type EmitirFacturaVentaCommand,
  type EmitirFacturaVentaResponse,
} from '@/features/facturacion/api/types';
import { useEmisorDefaults, useEmitirFactura } from '@/features/facturacion/api/useFacturas';
import { compararProforma } from '@/features/facturacion/api/useProformas';
import { ErrorTimbradoAviso } from '@/features/facturacion/components/ErrorTimbradoAviso';
import { errorEmisionVentaDirecta } from '@/features/facturacion/lib/errores-venta-directa';
import { mostrarErrorReceptorFiscal } from '@/features/facturacion/lib/mostrar-error-receptor-fiscal';
import { notaAnticiposAplicados } from '@/features/facturacion/lib/nota-anticipos';
import {
  EmitirFacturaSchema,
  type EmitirFacturaValues,
} from '@/features/facturacion/schemas/emitir-factura';
import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
import { applyServerErrors, esApiError, useFormIdempotencyKey } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { cn } from '@/lib/utils';
import { zodResolver } from '@hookform/resolvers/zod';
import { Link, useNavigate } from '@tanstack/react-router';
import { Info } from 'lucide-react';
import { useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { toast } from 'sonner';
import { VistaPreviaButton } from '../vista-previa/VistaPreviaButton';
import type { EmitirFacturaPrefill } from './prefill';
import { DiferenciasProformaDialog, type ConfirmacionProforma } from './DiferenciasProformaDialog';
import { resultadoEmision } from './resultado-emision';
import { TabEncabezado } from './TabEncabezado';
import { TabPosiciones } from './TabPosiciones';
import { TabTotales, type AnticipoAmortizar } from './TabTotales';
import { nullIfEmpty, valoresIniciales } from './valores';

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
  /**
   * F6.1: abre la edición del pedido de origen (destrabe de
   * PEDIDO_MANUAL_SIN_CLASIFICAR / FAC_PEDIDO_ACTIVO_COMPORTAMIENTO).
   * Ausente si el usuario no puede editarlo.
   */
  onEditarPedido?: () => void;
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
      <div className="space-y-2 rounded-md border border-danger/40 bg-danger-bg/5 px-4 py-3 text-sm">
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
        <div className="h-9 w-72 animate-pulse rounded bg-surface-muted" />
        <div className="h-64 w-full animate-pulse rounded bg-surface-muted" />
      </div>
    );
  }
  return (
    <FormInner
      key={props.prefill?.pedidoFacturableId ?? 'manual'}
      {...props}
      emisor={defaults.data}
    />
  );
}

function FormInner({
  prefill,
  onSuccess,
  onCancel,
  onEditarPedido,
  emisor,
}: EmitirFacturaFormProps & { emisor: EmisorDefaultsResponse }) {
  const idempotencyKey = useFormIdempotencyKey();
  const navigate = useNavigate();
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
  const puedeGestionarSeries = useHasPermission(PermisosCanonicos.AdminSeriesGestionar);

  // F6.1: pedido manual clasificado (venta directa o emergencia) → serie
  // de Venta directa; subtipo Activo → comportamiento fijo + autorización.
  const esVentaDirecta = prefill?.claseManual != null;
  const esPedidoActivo = prefill?.subtipoVentaDirecta === 'Activo';
  const manualSinClase = prefill?.origen === 'Manual' && prefill?.claseManual == null;

  const form = useForm<EmitirFacturaValues>({
    resolver: zodResolver(EmitirFacturaSchema),
    defaultValues: valoresIniciales(prefill, emisor),
  });

  // Anticipos a amortizar (relación 07). Estado local — no es parte del
  // schema del form porque son referencias a comprobantes ya emitidos.
  const [anticipos, setAnticipos] = useState<AnticipoAmortizar[]>([]);

  // 86e3tfdw2: «Observaciones» se prellena con los anticipos aplicados
  // mientras el cajero no la edite a mano; una vez editada, no se pisa.
  const [observacionesEditadas, setObservacionesEditadas] = useState(false);
  const observaciones = useWatch({ control: form.control, name: 'observaciones' });

  function cambiarAnticipos(siguientes: AnticipoAmortizar[]) {
    setAnticipos(siguientes);
    if (observacionesEditadas) return;
    const monedaFactura = (form.getValues('moneda') || 'MXN').toUpperCase();
    const nota = notaAnticiposAplicados(
      siguientes
        .filter((a) => a.anticipoId.trim().length > 0 && a.folio)
        .map((a) => ({ folio: a.folio ?? '', importe: a.importe, moneda: monedaFactura })),
    );
    form.setValue('observaciones', nota === '' ? null : nota, {
      shouldDirty: true,
      shouldValidate: form.formState.isSubmitted,
    });
  }

  function editarObservaciones(texto: string) {
    setObservacionesEditadas(true);
    form.setValue('observaciones', texto, {
      shouldDirty: true,
      shouldValidate: form.formState.isSubmitted,
    });
  }

  // Autorización del Contador General — obligatoria en venta de activo
  // fijo; se elige con <AutorizacionPicker/> (FAC-UX-PR4).
  const [autorizacionId, setAutorizacionId] = useState('');

  // U1.8a: antes de timbrar un pedido se compara contra su última proforma.
  const [comparando, setComparando] = useState(false);
  const [pendiente, setPendiente] = useState<{
    confirmacion: ConfirmacionProforma;
    command: EmitirFacturaVentaCommand;
  } | null>(null);

  // Cliente de la factura (FAC-UX-PR4): del pedido en el flujo prellenado
  // o del <ClienteSelector/> en el manual. Habilita el picker de anticipos
  // y el autollenado fiscal del receptor.
  const [clienteId, setClienteId] = useState<string | null>(prefill?.clienteId ?? null);

  function onClienteChange(item: ClienteLookupItem | null) {
    setClienteId(item?.id ?? null);
    // Cliente distinto → los anticipos elegidos ya no aplican.
    cambiarAnticipos([]);
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
    name: ['receptorRfc', 'receptorNombre', 'receptorRegimenFiscal', 'receptorCodigoPostal'],
  });
  const receptorBloqueado = receptorFiscal.some((v) => (v ?? '').trim() === '');
  const comportamiento = useWatch({
    control: form.control,
    name: 'comportamientoFiscal',
  });
  const ce = prefill?.embarqueCe;
  const rtPorConfirmar =
    ce?.ligado &&
    ce.clavePedimento === 'RT' &&
    comportamiento === ComportamientoFiscal.ExportacionConCce;
  const motivoCe = rtPorConfirmar
    ? 'La exportación RT sin complemento requiere confirmación de Fiscal (VAL-CE-08).'
    : ce?.motivo;
  const ceBloqueado = Boolean(ce?.ligado && (!ce.listoParaFacturar || rtPorConfirmar));
  const esCce = ce?.ligado
    ? ce.clavePedimento === 'A1'
    : comportamiento === ComportamientoFiscal.ExportacionConCce;
  const esActivo = esPedidoActivo || comportamiento === ComportamientoFiscal.VentaActivoFijo;
  const errorVentaDirecta = errorEmisionVentaDirecta(emitir.error);
  // `valueAsNumber` da NaN al vaciar un input, y `NaN ?? 0` = NaN (?? solo
  // atrapa null/undefined) → el total mostraba "NaN". `finito` lo coerce a 0.
  const finito = (v: number | null | undefined) => (Number.isFinite(v) ? (v as number) : 0);
  const totales = (lineas ?? []).reduce(
    (acc, l) => {
      const base = Math.max(0, finito(l.cantidad) * finito(l.valorUnitario) - finito(l.descuento));
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
  const erroresEncabezado = CAMPOS_ENCABEZADO.filter((campo) => errores[campo] != null).length;
  const erroresPosiciones =
    errores.lineas == null
      ? 0
      : Array.isArray(errores.lineas)
        ? errores.lineas.filter(Boolean).length
        : 1;

  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(onCancel);
  function cancelar() {
    setDirty(form.formState.isDirty);
    solicitarCierre();
  }

  async function onSubmit(values: EmitirFacturaValues) {
    if (ceBloqueado || manualSinClase) return;
    if (esActivo && autorizacionId.trim() === '') {
      toast.error('La venta de activo fijo requiere el ID de autorización del Contador General.');
      return;
    }
    const command = construirComando(values);
    const pedidoId = prefill?.pedidoFacturableId;
    if (!pedidoId) {
      emitirComando(command);
      return;
    }
    setComparando(true);
    try {
      const comparacion = await compararProforma(pedidoId, command);
      if (comparacion.hayProforma && comparacion.diferencias.length > 0) {
        setPendiente({ confirmacion: { tipo: 'diferencias', comparacion }, command });
        return;
      }
      emitirComando(command);
    } catch {
      setPendiente({ confirmacion: { tipo: 'error' }, command });
    } finally {
      setComparando(false);
    }
  }

  function timbrarPendiente() {
    if (pendiente == null) return;
    const { command } = pendiente;
    setPendiente(null);
    emitirComando(command);
  }

  function construirComando(values: EmitirFacturaValues): EmitirFacturaVentaCommand {
    return {
          sucursalId: values.sucursalId,
          receptorRfc: values.receptorRfc.toUpperCase(),
          receptorNombre: values.receptorNombre,
          receptorRegimenFiscal: values.receptorRegimenFiscal,
          receptorCodigoPostal: values.receptorCodigoPostal,
          receptorUsoCfdi: values.receptorUsoCfdi,
          // Conserva el país del cliente extranjero aunque la operación no lleve CCE.
          receptorPais: (values.cceReceptorPaisResidencia?.trim() || 'MEX').toUpperCase(),
          rfcEmisor: values.rfcEmisor.toUpperCase(),
          regimenFiscalEmisor: values.regimenFiscalEmisor,
          metodoPago: values.metodoPago,
          formaPago: values.formaPago,
          moneda: values.moneda.toUpperCase(),
          tipoCambio: values.moneda.toUpperCase() === 'MXN' ? null : values.tipoCambio,
          canalVenta: values.canalVenta,
          comportamientoFiscal: values.comportamientoFiscal,
          obraId: null,
          obraNombre: nullIfEmpty(values.obraNombre),
          facturaAgrupada: false,
          clienteId,
          pedidoFacturableId: prefill?.pedidoFacturableId ?? null,
          anticipos:
            anticipos.filter((a) => a.anticipoId.trim().length > 0).length > 0
              ? anticipos
                  .filter((a) => a.anticipoId.trim().length > 0)
                  // El folio es solo del FE (arma la nota); no viaja al backend.
                  .map((a) => ({ anticipoId: a.anticipoId, importe: a.importe }))
              : null,
          observaciones: nullIfEmpty(values.observaciones),
          autorizacionId: esActivo ? autorizacionId.trim() : null,
          cce: esCce
            ? {
                tipoOperacion: values.cceTipoOperacion ?? '2',
                incoterm: ce?.ligado ? (ce.incoterm ?? '') : (values.cceIncoterm ?? ''),
                tcDof: values.cceTcDof ?? 0,
                receptorNumRegIdTrib: values.cceReceptorNumRegIdTrib ?? '',
                receptorPaisResidencia: values.cceReceptorPaisResidencia ?? '',
                claveDePedimento: ce?.ligado
                  ? ce.clavePedimento
                  : (nullIfEmpty(values.cceClaveDePedimento)?.toUpperCase() ?? null),
                certificadoOrigen: values.cceCertificadoOrigen,
                receptorDomicilioCalle: nullIfEmpty(values.cceReceptorDomicilioCalle),
                receptorDomicilioEstado: values.cceReceptorDomicilioEstado ?? '',
                receptorDomicilioCodigoPostal: values.cceReceptorDomicilioCodigoPostal ?? '',
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
    };
  }

  function emitirComando(command: EmitirFacturaVentaCommand) {
    emitir.mutate(
      { command, idempotencyKey },
      {
        onSuccess: (res) => {
          const aviso = resultadoEmision(res);
          toast[aviso.tipo](aviso.titulo, {
            description: aviso.descripcion,
            ...(aviso.reintentable
              ? {
                  duration: Infinity,
                  action: {
                    label: 'Reintentar timbrado',
                    onClick: () =>
                      void navigate({
                        to: '/facturacion/facturas/$id',
                        params: { id: res.id },
                      }),
                  },
                }
              : {}),
          });
          onSuccess(res);
        },
        onError: (error) => {
          // F6.1: serie de Venta directa / clase del pedido → callout con destrabe.
          if (errorEmisionVentaDirecta(error) != null) return;
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
          toast.error('Error inesperado al emitir la factura.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} noValidate className="space-y-4">
      {ce?.ligado && <EmbarqueCeResumen datos={ce} bloqueo={ceBloqueado ? motivoCe : null} />}
      {errorVentaDirecta != null ? (
        <AvisoEnLinea
          tono="error"
          titulo={errorVentaDirecta.titulo}
          accion={
            errorVentaDirecta.destino === 'series' && puedeGestionarSeries ? (
              <Link to="/admin/series" className="text-sm font-medium text-brand underline">
                Abrir Administración → Series
              </Link>
            ) : errorVentaDirecta.destino === 'editar-pedido' && onEditarPedido ? (
              <Button type="button" variant="secondary" size="sm" onClick={onEditarPedido}>
                Editar pedido
              </Button>
            ) : undefined
          }
        >
          {errorVentaDirecta.mensaje}
        </AvisoEnLinea>
      ) : (
        <ErrorTimbradoAviso error={emitir.error} />
      )}
      {manualSinClase && (
        <AvisoEnLinea
          tono="advertencia"
          titulo="Pedido manual sin clase"
          accion={
            onEditarPedido ? (
              <Button type="button" variant="secondary" size="sm" onClick={onEditarPedido}>
                Editar pedido
              </Button>
            ) : undefined
          }
        >
          Indica si el pedido es venta directa o emergencia antes de facturarlo.
        </AvisoEnLinea>
      )}
      {esVentaDirecta && (
        <p
          data-testid="leyenda-serie-venta-directa"
          className="flex items-start gap-1.5 rounded-md bg-info-bg px-3 py-2 text-sm text-info-fg"
        >
          <Info aria-hidden="true" className="mt-0.5 size-4 shrink-0" strokeWidth={1.6} />
          Se timbrará con la serie de Venta directa de la sucursal.
        </p>
      )}
      {prefill?.pedidoFacturableId && (
        <div className="rounded-md border border-brand/30 bg-brand/5 px-3 py-2 text-sm">
          Facturando un pedido — partidas, datos comerciales y fiscales vienen precargados desde el
          pedido y los catálogos. Revisa y completa lo que falte. Al timbrar, el pedido pasa a{' '}
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
            Partidas
            <BadgeErrores cuenta={erroresPosiciones} />
          </TabsTrigger>
          <TabsTrigger value="totales">
            Totales
            <BadgeErrores cuenta={errores.observaciones != null ? 1 : 0} />
          </TabsTrigger>
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
            embarqueCe={ce}
            esActivo={esActivo}
            comportamientoFijo={
              esPedidoActivo
                ? 'Fijo: el pedido es una venta directa de un activo.'
                : undefined
            }
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
            onAnticiposChange={cambiarAnticipos}
            observaciones={observaciones ?? ''}
            onObservacionesChange={editarObservaciones}
            errorObservaciones={errores.observaciones?.message}
          />
        </TabsContent>
      </Tabs>

      <ResumenImportes
        className="border-t border-line pt-4"
        moneda={moneda?.toUpperCase() ?? ''}
        subtotal={totales.subtotal}
        ivaTrasladado={totales.iva}
        otrasRetenciones={[{ etiqueta: 'Retenciones', monto: totales.ret }]}
        total={total}
        acciones={
          <>
            <Button type="button" variant="ghost" onClick={cancelar} disabled={emitir.isPending}>
              Cancelar
            </Button>
            <VistaPreviaButton
              motivo={
                !clienteId || receptorBloqueado
                  ? 'Selecciona un receptor con datos fiscales.'
                  : !lineas?.length
                    ? 'Agrega al menos una partida.'
                    : undefined
              }
              obtenerDatos={() => {
                const v = form.getValues();
                return {
                  tipo: 'Factura',
                  emisor: `${emisor.razonSocialEmisor} · ${v.rfcEmisor} · ${v.regimenFiscalEmisor}`,
                  receptor: `${v.receptorNombre} · ${v.receptorRfc} · ${v.receptorRegimenFiscal} · CP ${v.receptorCodigoPostal}`,
                  usoCfdi: v.receptorUsoCfdi,
                  metodoPago: v.metodoPago,
                  formaPago: v.formaPago,
                  moneda: v.moneda,
                  partidas: v.lineas.map((l) => ({
                    descripcion: l.descripcion,
                    cantidad: l.cantidad,
                    importe: Math.max(
                      0,
                      finito(l.cantidad) * finito(l.valorUnitario) - finito(l.descuento),
                    ),
                  })),
                  importes: [
                    { etiqueta: 'Subtotal', monto: totales.subtotal },
                    { etiqueta: 'IVA trasladado', monto: totales.iva },
                    { etiqueta: 'Retenciones', monto: -totales.ret },
                    { etiqueta: 'Total', monto: total },
                  ],
                };
              }}
            />
            <Button
              type="submit"
              disabled={
                emitir.isPending || comparando || receptorBloqueado || ceBloqueado || manualSinClase
              }
              title={
                manualSinClase
                  ? 'Clasifica el pedido (venta directa o emergencia) antes de facturarlo.'
                  : ceBloqueado
                    ? (motivoCe ?? 'Complete el embarque en Comercio Exterior.')
                    : receptorBloqueado
                      ? 'Receptor incompleto: complétalo en Datos Maestros → Clientes'
                      : undefined
              }
            >
              {emitir.isPending
                ? 'Emitiendo…'
                : comparando
                  ? 'Comparando con la proforma…'
                  : 'Emitir y timbrar'}
            </Button>
          </>
        }
      />
      <DiferenciasProformaDialog
        confirmacion={pendiente?.confirmacion ?? null}
        onRevisar={() => setPendiente(null)}
        onTimbrar={timbrarPendiente}
      />
      {dialogo}
    </form>
  );
}

function BadgeErrores({ cuenta }: { cuenta: number }) {
  if (cuenta === 0) return null;
  return (
    <span
      className={cn(
        'inline-flex h-4 min-w-4 items-center justify-center rounded-full',
        'bg-danger-bg px-1 text-[10px] font-semibold text-danger-fg-foreground',
      )}
      aria-label={`${cuenta} errores`}
    >
      {cuenta}
    </span>
  );
}
