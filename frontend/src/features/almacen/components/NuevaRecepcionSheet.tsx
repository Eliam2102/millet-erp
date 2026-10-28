import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import type { ImportacionRecepcionValues } from '../schemas/recepcion';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { IndicadorAlmacenBadge } from './IndicadorAlmacenBadge';
import { OrdenCompraRecepcionSelector } from './OrdenCompraRecepcionSelector';
import { FormGrid, FormCampo } from '@/components/erp/forms/FormGrid';
import { SheetBody } from '@/components/ui/sheet';
import { hoyLocalISO } from '@/lib/datetime';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Controller, useForm, useWatch, type Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigate } from '@tanstack/react-router';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { useSubAlmacenesEnRevision } from '@/features/almacen/api/useAlmacenes';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { ConfirmarAccionDialog } from '@/components/erp/feedback/ConfirmarAccionDialog';
import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
import { formatCantidad } from '@/lib/format-cantidad';
import {
  AvisoUnidadNoResoluble,
  decimalesDeFila,
  DECIMALES_FALLBACK,
  evaluarDecimalesFila,
  MENSAJE_DECIMALES_UNIDAD,
  stepParaDecimales,
  TextAreaField,
  useDecimalesUnidad,
} from '@/components/erp';
import { applyServerErrors, esApiError, useFormIdempotencyKey, codigoSoporte } from '@/lib/api';
import {
  RegistrarRecepcionFacturaSchema,
  RegistrarRecepcionPackingListSchema,
  type FilaRecepcionValues,
  type RegistrarRecepcionFacturaValues,
  type RegistrarRecepcionPackingListValues,
} from '@/features/almacen/schemas/recepcion';
import {
  useRegistrarRecepcionFactura,
  useRegistrarRecepcionPackingList,
} from '@/features/almacen/api';
import { useCfdisParaRecepcion, useLineasRecibibles } from '@/features/almacen/api/useRecepciones';
import { PackingListUpload } from '@/features/almacen/components/PackingListUpload';
import { UbicacionBinSelector } from '@/components/erp/selectors/UbicacionBinSelector';
import { AsignarUbicacionEnLinea } from '@/features/almacen/components/AsignarUbicacionEnLinea';
import { UbicacionSelector } from '@/components/erp/selectors/UbicacionSelector';
import { useUbicacionesAsignadasPorArticulo } from '@/features/almacen/api/useAsignaciones';
import { useUbicaciones } from '@/features/almacen/api/useUbicaciones';
import { EstatusCatalogo } from '@/features/almacen/api/types';
import {
  construirFilas,
  contarLineasEnOtraUbicacion,
  destinoPorFila,
  detectarConflictoSubAlmacen,
  indicesParaAutoAsignar,
} from '@/features/almacen/components/nueva-recepcion-helpers';
import { useOrdenCompra } from '@/features/compras/ordenes/api/useOrdenCompra';
import { CfdiPorProcesarPicker } from '@/features/cxp/components/CfdiPorProcesarPicker';
import { CargarCfdiSheet } from '@/features/cxp/components/CargarCfdiSheet';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { cn } from '@/lib/utils';
import { Field } from '@/features/almacen/components/internal/Field';
import { etiquetaMedidaMm } from '@/features/almacen/lib/medida-hoja';
import { hojasPendientes } from '@/features/almacen/components/nueva-recepcion-helpers';

/**
 * <c>&lt;NuevaRecepcionSheet/&gt;</c> — slide-from-right para registrar
 * una recepción contra una OC autorizada. Soporta las dos variantes
 * definidas en §5.4-5.5 del 00-levantamiento de Almacén.
 *
 * <para>UX <b>checklist</b> (no captura libre): al seleccionar la OC el
 * sistema fetchea su detalle vía
 * <c>useOrdenCompra(ordenCompraId)</c> y popula las líneas
 * automáticamente. El usuario marca cuáles llegaron y, opcionalmente,
 * ajusta la cantidad recibida (default = pendiente). Líneas ya completas
 * (CantidadRecibida ≥ Cantidad) se ocultan. No es posible recibir
 * artículos fuera de la OC — si llega un excedente físico, el comprador
 * modifica la OC y se rehace la recepción.</para>
 */
export interface NuevaRecepcionSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Siembra la OC al abrir (p. ej. al capturar la recepción correcta tras cancelar una). */
  ordenCompraIdInicial?: string;
}

type Variante = 'A' | 'B';

interface PackingListAdjunto {
  blobRef: string;
  nombreArchivo: string;
  tamanoBytes: number;
  contentType: string;
}

export function NuevaRecepcionSheet({
  open,
  onOpenChange,
  ordenCompraIdInicial,
}: NuevaRecepcionSheetProps) {
  // Los gestos de cerrar (Esc, clic fuera, X y Cancelar) piden confirmación si
  // hay cambios (useConfirmarDescarte). El registro exitoso cierra con force.
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(() => onOpenChange(false));
  const cerrarTrasRegistro = () => solicitarCierre(true);

  return (
    <Sheet open={open} onOpenChange={(next) => (next ? onOpenChange(next) : solicitarCierre())}>
      <SheetContent side="right" size="lg">
        <SheetHeader>
          <SheetTitle>Nueva recepción</SheetTitle>
          <SheetDescription>
            Selecciona la OC autorizada y marca las partidas que llegaron. El costo se toma de la
            OC; la cantidad no puede superar lo pendiente de la OC.
          </SheetDescription>
        </SheetHeader>

        {open && (
          <NuevaRecepcionFormBody
            ordenCompraIdInicial={ordenCompraIdInicial}
            onSuccess={cerrarTrasRegistro}
            onCancel={() => solicitarCierre()}
            onDirtyChange={setDirty}
          />
        )}
      </SheetContent>
      {dialogo}
    </Sheet>
  );
}

// Códigos de negocio del registro → campo del form o aviso persistente sobre las líneas.
const MENSAJE_SIN_CFDI = 'Vincula el CFDI del proveedor o captura su folio fiscal (UUID).';
const CODIGOS_RECEPCION: Record<string, { campo: string; mensaje?: string } | 'callout'> = {
  RECEPCION_CFDI_NO_ENCONTRADO: { campo: 'cfdiRecibidoId' },
  RECEPCION_CFDI_NO_VINCULABLE: { campo: 'cfdiRecibidoId' },
  RECEPCION_CFDI_PROVEEDOR_DISTINTO: { campo: 'cfdiRecibidoId' },
  RECEPCION_SIN_CFDI: { campo: 'cfdiRecibidoId', mensaje: MENSAJE_SIN_CFDI },
  RECEPCION_OC_NO_AUTORIZADA: {
    campo: 'ordenCompraId',
    mensaje: 'La OC no está en estado que acepte recepción.',
  },
  RECEPCION_EXCEDE_PENDIENTE: 'callout',
  ENTRADA_SIN_ASIGNACION: 'callout',
  RECEPCION_MULTI_SUBALMACEN: 'callout',
  RECEPCION_SUCURSAL_DISTINTA: 'callout',
};
const TITULOS_CALLOUT: Record<string, string> = {
  RECEPCION_EXCEDE_PENDIENTE: 'Una cantidad excede lo pendiente',
  ENTRADA_SIN_ASIGNACION: 'Un artículo no está asignado al rack elegido',
  RECEPCION_MULTI_SUBALMACEN: 'Las partidas van a sub-almacenes distintos',
  RECEPCION_SUCURSAL_DISTINTA: 'La orden de compra es de otra sucursal',
};

function NuevaRecepcionFormBody({
  ordenCompraIdInicial,
  onSuccess,
  onCancel,
  onDirtyChange,
}: {
  ordenCompraIdInicial?: string;
  onSuccess: () => void;
  onCancel: () => void;
  onDirtyChange: (dirty: boolean) => void;
}) {
  const navigate = useNavigate();
  const idempotencyKey = useFormIdempotencyKey();
  const [variante, setVariante] = useState<Variante>('A');
  const [packingListAdjunto, setPackingListAdjunto] = useState<PackingListAdjunto | null>(null);
  // Variante A — vínculo fiscal: sheet apilado de carga de XML + modo
  // "folio fiscal a mano" (cuando el CFDI aún no está en el ERP).
  const [cargarCfdiOpen, setCargarCfdiOpen] = useState(false);
  const [capturaUuidManual, setCapturaUuidManual] = useState(false);

  const registrarA = useRegistrarRecepcionFactura();
  const registrarB = useRegistrarRecepcionPackingList();

  const hoyIso = useMemo(() => hoyLocalISO(), []);

  const resolver: Resolver<RecepcionFormValues> = useMemo(() => {
    const schema =
      variante === 'A' ? RegistrarRecepcionFacturaSchema : RegistrarRecepcionPackingListSchema;
    return zodResolver(schema) as Resolver<RecepcionFormValues>;
  }, [variante]);

  const form = useForm<RecepcionFormValues>({
    resolver,
    defaultValues: defaultValuesParaVariante('A', hoyIso, ordenCompraIdInicial),
  });

  // El packing list adjunto vive fuera de RHF: cuenta como cambio sin guardar.
  const isDirty = form.formState.isDirty || packingListAdjunto != null;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);
  // Al desmontar (cierre o cambio de visibilidad) ya no hay cambios pendientes.
  useEffect(() => () => onDirtyChange(false), [onDirtyChange]);

  // Cambiar de variante reinicia el form: con datos capturados pide confirmación.
  const [varianteSolicitada, setVarianteSolicitada] = useState<Variante | null>(null);
  const [avisoServidor, setAvisoServidor] = useState<{
    titulo: string;
    detalle: string;
  } | null>(null);

  function solicitarVariante(siguiente: Variante) {
    if (siguiente === variante) return;
    if (isDirty) setVarianteSolicitada(siguiente);
    else cambiarVariante(siguiente);
  }

  function cambiarVariante(siguiente: Variante) {
    setVarianteSolicitada(null);
    setAvisoServidor(null);
    setVariante(siguiente);
    setPackingListAdjunto(null);
    setCapturaUuidManual(false);
    form.reset(defaultValuesParaVariante(siguiente, hoyIso, ordenCompraIdInicial));
  }

  // ── Helper de cabecera nivel 4 (bin por defecto) ─────────────────────────
  // Sin sub-almacén de cabecera (se deriva del bin), el helper lista los racks
  // asignados a los artículos de la OC vía <UbicacionSelector soloIds>.
  /**
   * Aplica el helper: guarda la elección en la cabecera y auto-asigna el bin a
   * las líneas sin ubicación cuyo artículo está asignado a ese rack. Las que ya
   * tienen otra se respetan (el aviso de divergencia sale sobre las líneas).
   */
  function aplicarUbicacionHelper(ubicacionId: string | null) {
    form.setValue('ubicacionHelperId', ubicacionId, { shouldDirty: true });
    if (!ubicacionId) return;
    // Solo se rellenan las líneas cuyo artículo tiene asignado ese rack; el
    // resto se avisa sobre la lista de líneas y se elige a mano.
    const filas = form.getValues('filas') ?? [];
    const destinos = destinoPorFila(filas, ubicacionId, ubicInfo, asignadas.porArticulo, subsEnRevision.ids);
    for (const index of indicesParaAutoAsignar(filas, destinos)) {
      form.setValue(`filas.${index}.ubicacionId`, destinos[index], {
        shouldDirty: true,
        shouldValidate: true,
      });
    }
  }

  const subsEnRevision = useSubAlmacenesEnRevision();
  const lookup = useDecimalesUnidad();
  const ubicacionesQuery = useUbicaciones({ limit: 500 });
  const ubicInfo = useMemo(
    () => new Map((ubicacionesQuery.data?.items ?? []).map((u) => [u.id, u])),
    [ubicacionesQuery.data],
  );

  // Vínculo fiscal (variante A): el picker de CFDIs se acota al RFC del
  // proveedor de la OC. La query de OC se dedupe con la de LineasDesdeOc
  // (misma queryKey); el RFC sale del catálogo de proveedores.
  const ordenCompraId = useWatch({
    control: form.control,
    name: 'ordenCompraId',
  });
  const filasWatch = useWatch({ control: form.control, name: 'filas' });
  const articuloIds = useMemo(
    () => [...new Set((filasWatch ?? []).map((f) => f.articuloId))],
    [filasWatch],
  );
  const asignadas = useUbicacionesAsignadasPorArticulo(articuloIds);
  // Opciones del helper: racks asignados a los artículos de la OC que no son de A+W + almacenes generales (sin Material en revisión).
  const opcionesHelper = useMemo(() => {
    if (!asignadas.listo || !subsEnRevision.listo) return undefined;
    const union = new Set<string>();
    for (const f of filasWatch ?? [])
      if (!f.medida) asignadas.porArticulo.get(f.articuloId)?.forEach((id) => union.add(id));
    ubicInfo.forEach((u, id) => {
      if (u.esDefault && u.estatus === EstatusCatalogo.Activo && !subsEnRevision.ids.has(u.subAlmacenId))
        union.add(id);
    });
    return union;
  }, [asignadas.listo, asignadas.porArticulo, filasWatch, ubicInfo, subsEnRevision]);
  const queryClient = useQueryClient();
  const puedeLeerCfdis = useHasPermission(PermisosCanonicos.CuentasPorPagarCfdisLeer);
  const puedeCargarCfdi = useHasPermission(PermisosCanonicos.CuentasPorPagarCfdisCargarManual);
  const cfdisRecepcion = useCfdisParaRecepcion(
    ordenCompraId || null,
    puedeLeerCfdis && !!ordenCompraId,
  );
  const rfcProveedor = cfdisRecepcion.data?.rfcEmisor ?? undefined;

  // Al cambiar de OC, el CFDI seleccionado deja de corresponder al
  // proveedor — se descarta (no aplica al primer render).
  const ocPrevia = useRef<string>(ordenCompraId ?? '');
  useEffect(() => {
    if (ocPrevia.current === (ordenCompraId ?? '')) return;
    ocPrevia.current = ordenCompraId ?? '';
    // La ubicación por defecto es de la OC anterior (si no, la cabecera mostraría un GUID).
    form.setValue('ubicacionHelperId', null);
    if (variante === 'A') {
      form.setValue('cfdiRecibidoId', null);
    }
  }, [ordenCompraId, variante, form]);

  function onSubmit(values: RecepcionFormValues) {
    setAvisoServidor(null);
    // Un solo sub-almacén por recepción: el conflicto ya se ve por línea.
    const subs = new Map(
      (ubicacionesQuery.data?.items ?? []).map((u) => [
        u.id,
        { id: u.subAlmacenId, clave: u.subAlmacenClave },
      ]),
    );
    if (detectarConflictoSubAlmacen(values.filas, subs).conflictos.size > 0) return;
    // Advisory: decimales por unidad (solo filas incluidas; backend autoritativo).
    let decimalesMal = false;
    values.filas.forEach((f, i) => {
      if (!f.incluida || f.medida) return; // por medida: hojas enteras, el schema y el servidor validan
      // Solo 'invalidos' bloquea; 'no-resoluble' avisa (sin bloquear) en la fila.
      if (
        evaluarDecimalesFila(lookup, {
          unidadMedida: f.unidadCapturada?.trim() || f.unidadMedida,
          cantidad: f.cantidad,
        }) === 'invalidos'
      ) {
        form.setError(`filas.${i}.cantidad` as Parameters<typeof form.setError>[0], {
          type: 'decimales',
          message: MENSAJE_DECIMALES_UNIDAD,
        });
        decimalesMal = true;
      }
    });
    if (decimalesMal) return;
    const lineasParaBackend = values.filas
      .filter((f) => f.incluida)
      .map((f) => {
        const base = {
          fraccionArancelaria: f.fraccionArancelaria ?? null,
          articuloId: f.articuloId,
          lineaOcId: f.lineaOcId,
          ubicacionReferencia: f.ubicacionReferencia ?? null,
          ubicacionId: f.ubicacionId ?? null,
          comentario: f.comentario ?? null,
        };
        // F1-MP-01 (E1): con medida, la cantidad y las dañadas son hojas; la medida la toma el servidor de la línea de OC.
        return {
          ...base,
          cantidad: f.cantidad,
          unidadCapturada: f.medida ? 'HOJA' : f.unidadCapturada?.trim() || null,
          cantidadDanada: f.cantidadDanada ?? 0,
          motivoDano: (f.cantidadDanada ?? 0) > 0 ? f.motivoDano?.trim() || null : null,
          // E3: el lote del proveedor viaja en ambas variantes; lo dañado a MAT-REV lleva el mismo lote.
          lote: f.lote?.trim() || null,
        };
      });

    if (variante === 'A') {
      const v = values as RegistrarRecepcionFacturaValues;
      registrarA.mutate(
        {
          command: {
            importacion: v.importacion ?? null,
            ordenCompraId: v.ordenCompraId,
            fechaMovimiento: v.fechaMovimiento,
            cfdiRecibidoId: v.cfdiRecibidoId ?? null,
            // Mayúsculas como el VO UuidCfdi de CxP (el backend re-normaliza).
            cfdiUuidFiscal: v.cfdiUuidFiscal?.trim().toUpperCase() ?? null,
            observaciones: v.observaciones ?? null,
            // PR4: null = el almacenista no usó el helper de cabecera.
            ubicacionHelperId: v.ubicacionHelperId ?? null,
            lineas: lineasParaBackend,
          },
          idempotencyKey,
        },
        {
          onSuccess: (resp) => {
            toast.success(`Recepción ${resp.folio} registrada`);
            onSuccess();
            navigate({
              to: '/almacen/recepciones/$id',
              params: { id: resp.recepcionId },
            });
          },
          onError: (error) => manejarError(error),
        },
      );
      return;
    }

    if (packingListAdjunto == null) {
      form.setError('packingListBlobRef' as keyof RecepcionFormValues, {
        type: 'required',
        message: 'Sube el archivo del packing list antes de registrar.',
      });
      return;
    }
    const v = values as RegistrarRecepcionPackingListValues;
    registrarB.mutate(
      {
        command: {
          importacion: v.importacion ?? null,
          ordenCompraId: v.ordenCompraId,
          fechaMovimiento: v.fechaMovimiento,
          packingListBlobRef: packingListAdjunto.blobRef,
          observaciones: v.observaciones ?? null,
          // PR4: null = el almacenista no usó el helper de cabecera.
          ubicacionHelperId: v.ubicacionHelperId ?? null,
          lineas: lineasParaBackend,
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Recepción ${resp.folio} registrada`);
          onSuccess();
          navigate({
            to: '/almacen/recepciones/$id',
            params: { id: resp.recepcionId },
          });
        },
        onError: (error) => manejarError(error),
      },
    );
  }

  function manejarError(error: unknown) {
    if (!esApiError(error)) {
      toast.error('Error inesperado al registrar la recepción.');
      return;
    }
    const resultado = applyServerErrors(
      form as unknown as Parameters<typeof applyServerErrors>[0],
      error,
      { codigos: CODIGOS_RECEPCION },
    );
    if (resultado === 'callout') {
      // El servidor no indica la línea: se deja el aviso persistente sobre las líneas.
      setAvisoServidor({
        titulo: TITULOS_CALLOUT[error.code ?? ''] ?? 'No se pudo registrar',
        detalle: error.problem.detail ?? error.problem.title,
      });
      return;
    }
    if (resultado) return;
    toast.error(error.problem.title, {
      description: codigoSoporte(error),
    });
  }

  const isPending = registrarA.isPending || registrarB.isPending;

  return (
    <>
      <SheetBody>
        <form
          id="nueva-recepcion-form"
          onSubmit={form.handleSubmit(onSubmit, () =>
            toast.error('Revisa los campos marcados antes de registrar la recepción.'),
          )}
          noValidate
          className="space-y-4"
        >
          <fieldset className="space-y-2">
            <legend className="text-sm font-medium">Tipo de recepción</legend>
            <div className="flex gap-2">
              <VarianteOption
                value="A"
                active={variante === 'A'}
                onSelect={solicitarVariante}
                titulo="Con factura/CFDI"
                descripcion="Insumos y refacciones — el proveedor llega con factura."
              />
              <VarianteOption
                value="B"
                active={variante === 'B'}
                onSelect={solicitarVariante}
                titulo="Con packing list"
                descripcion="Materiales directos no-vidrio — factura llega después por CxP."
              />
            </div>
          </fieldset>

          <FormSeccion numero={1} titulo="Datos de recepción">
            <FormCampo span={6}>
              <Field
                label="Orden de compra"
                required
                error={form.formState.errors.ordenCompraId?.message}
              >
                <Controller
                  name="ordenCompraId"
                  control={form.control}
                  render={({ field }) => (
                    <OrdenCompraRecepcionSelector
                      value={field.value || null}
                      onChange={(id) => field.onChange(id ?? '')}
                      soloConPendienteRecepcion
                    />
                  )}
                />
              </Field>
            </FormCampo>

            {/* PR4: helper de cabecera nivel 4. Opcional — al elegirlo, auto-aplica
              el bin a las líneas que aún no tienen ubicación. Las que ya tienen
              otra NO se tocan (el aviso de divergencia sale sobre las líneas).
              Se persiste para reportería; null = el almacenista no lo usó. */}
            <FormCampo span={6}>
              <Field
                label="Ubicación por defecto (opcional)"
                hint="Aplica a los consumibles y refacciones; los artículos de A+W eligen su rack en cada partida."
                error={form.formState.errors.ubicacionHelperId?.message}
              >
                <Controller
                  name="ubicacionHelperId"
                  control={form.control}
                  render={({ field }) => (
                    <UbicacionSelector
                      soloIds={opcionesHelper}
                      value={field.value || null}
                      onChange={(id) => aplicarUbicacionHelper(id)}
                      placeholder="Sin ubicación por defecto"
                    />
                  )}
                />
              </Field>
            </FormCampo>

            <FormCampo span={6}>
              <Field
                label="Fecha de movimiento"
                required
                error={form.formState.errors.fechaMovimiento?.message}
              >
                <Controller
                  name="fechaMovimiento"
                  control={form.control}
                  render={({ field }) => <Input {...field} type="date" value={field.value ?? ''} />}
                />
              </Field>
            </FormCampo>

            <FormCampo span={12}>
              {variante === 'A' && (
                <div className="space-y-1.5 md:col-span-2">
                  <label className="flex items-center gap-1 text-sm font-medium">
                    CFDI del proveedor
                    <span aria-hidden="true" className="text-danger-fg">
                      *
                    </span>
                  </label>

                  {!ordenCompraId ? (
                    <p className="rounded-md border border-dashed border-line-dashed bg-surface-subtle p-3 text-xs text-ink-muted">
                      Selecciona la orden de compra para vincular el CFDI del proveedor.
                    </p>
                  ) : (
                    <>
                      {puedeLeerCfdis && !capturaUuidManual && (
                        <div className="flex gap-2">
                          <Controller
                            name="cfdiRecibidoId"
                            control={form.control}
                            render={({ field }) => (
                              <CfdiPorProcesarPicker
                                value={field.value ?? null}
                                onSelect={(cfdi) => {
                                  field.onChange(cfdi?.id ?? null);
                                  if (cfdi) {
                                    form.setValue('cfdiUuidFiscal', null);
                                    form.clearErrors('cfdiRecibidoId');
                                  }
                                }}
                                rfcEmisor={rfcProveedor}
                                ordenCompraRecepcionId={ordenCompraId}
                                placeholder={
                                  rfcProveedor
                                    ? `CFDIs por procesar de ${rfcProveedor}…`
                                    : 'Vincular CFDI recibido…'
                                }
                                className="flex-1"
                              />
                            )}
                          />
                          {puedeCargarCfdi && (
                            <Button
                              type="button"
                              variant="outline"
                              onClick={() => setCargarCfdiOpen(true)}
                            >
                              Cargar XML
                            </Button>
                          )}
                        </div>
                      )}
                      {puedeLeerCfdis && !capturaUuidManual && cfdisRecepcion.data?.aviso && (
                        <p className="text-xs text-ink-muted" role="status">
                          {cfdisRecepcion.data.aviso}
                        </p>
                      )}

                      {capturaUuidManual || !puedeLeerCfdis ? (
                        <div className="space-y-1.5 rounded-md border border-dashed border-line-dashed p-3">
                          <label htmlFor="cfdi-uuid-fiscal" className="text-sm font-medium">
                            Folio fiscal (UUID del impreso)
                          </label>
                          <Controller
                            name="cfdiUuidFiscal"
                            control={form.control}
                            render={({ field }) => (
                              <Input
                                {...field}
                                id="cfdi-uuid-fiscal"
                                value={field.value ?? ''}
                                onChange={(e) =>
                                  field.onChange(e.target.value === '' ? null : e.target.value)
                                }
                                placeholder="AD662D33-6934-459C-A128-BDF0393E0062"
                                spellCheck={false}
                                className="font-mono uppercase"
                              />
                            )}
                          />
                          {form.formState.errors.cfdiUuidFiscal?.message && (
                            <p role="alert" className="text-xs text-danger-fg">
                              {form.formState.errors.cfdiUuidFiscal.message}
                            </p>
                          )}
                          <p className="text-xs text-ink-muted">
                            Viene impreso en la representación del CFDI (y en su QR). CxP enlazará
                            el CFDI cuando el XML llegue por su canal.
                          </p>
                          {puedeLeerCfdis && (
                            <button
                              type="button"
                              className="text-xs text-brand underline-offset-2 hover:underline"
                              onClick={() => {
                                setCapturaUuidManual(false);
                                form.setValue('cfdiUuidFiscal', null);
                              }}
                            >
                              Mejor vincular un CFDI del sistema
                            </button>
                          )}
                        </div>
                      ) : (
                        <button
                          type="button"
                          className="text-xs text-brand underline-offset-2 hover:underline"
                          onClick={() => {
                            setCapturaUuidManual(true);
                            form.setValue('cfdiRecibidoId', null);
                          }}
                        >
                          ¿El CFDI no está en el sistema? Captura el folio fiscal del impreso
                        </button>
                      )}

                      {form.formState.errors.cfdiRecibidoId?.message && (
                        <p role="alert" className="text-xs text-danger-fg">
                          {form.formState.errors.cfdiRecibidoId.message}
                        </p>
                      )}
                    </>
                  )}
                </div>
              )}
            </FormCampo>
          </FormSeccion>

          {variante === 'B' && (
            <Field
              label="Packing list"
              required
              error={form.formState.errors.packingListBlobRef?.message}
            >
              <PackingListUpload
                value={packingListAdjunto}
                onChange={(v) => {
                  setPackingListAdjunto(v);
                  // El schema valida packingListBlobRef en el resolver, así
                  // que el blobRef debe entrar al form al subir el archivo —
                  // no solo en onSubmit (ahí el resolver ya rechazó).
                  form.setValue(
                    'packingListBlobRef' as keyof RecepcionFormValues,
                    (v?.blobRef ?? '') as never,
                  );
                  if (v) form.clearErrors('packingListBlobRef');
                }}
              />
            </Field>
          )}

          <Field
            label="Observaciones (opcional)"
            error={form.formState.errors.observaciones?.message}
          >
            <Controller
              name="observaciones"
              control={form.control}
              render={({ field }) => (
                <TextAreaField
                  value={field.value ?? null}
                  onChange={(v) => field.onChange(v)}
                  maxLength={500}
                  minRows={2}
                />
              )}
            />
          </Field>

          {avisoServidor && (
            <AvisoEnLinea tono="error" titulo={avisoServidor.titulo}>
              {avisoServidor.detalle} Revisa las cantidades y los racks de las partidas marcadas.
            </AvisoEnLinea>
          )}

          <LineasDesdeOc form={form} asignadas={asignadas.porArticulo} />
        </form>
      </SheetBody>

      <SheetFooter>
        <Button type="button" variant="ghost" onClick={onCancel} disabled={isPending}>
          Cancelar
        </Button>
        <Button type="submit" form="nueva-recepcion-form" disabled={isPending}>
          {isPending ? 'Registrando…' : 'Registrar recepción'}
        </Button>
      </SheetFooter>

      <ConfirmarAccionDialog
        open={varianteSolicitada != null}
        onOpenChange={(o) => !o && setVarianteSolicitada(null)}
        titulo="¿Cambiar el tipo de recepción?"
        resumen="Se perderán la OC, las partidas y el CFDI o packing list capturados."
        verbo="Cambiar tipo"
        tono="peligro"
        onConfirmar={() => varianteSolicitada && cambiarVariante(varianteSolicitada)}
      />

      {/* Sheet apilado: carga del XML del proveedor (canal CargaManual de
          CxP). Al cargar, el CFDI recién ingresado queda vinculado. */}
      <CargarCfdiSheet
        open={cargarCfdiOpen}
        onOpenChange={setCargarCfdiOpen}
        onCargado={(resp) => {
          form.setValue('cfdiRecibidoId', resp.id, { shouldValidate: true });
          form.setValue('cfdiUuidFiscal', null);
          setCapturaUuidManual(false);
          queryClient.invalidateQueries({ queryKey: ['almacen', 'recepcion-cfdis'] });
        }}
      />
    </>
  );
}

// ─── Líneas desde OC ───────────────────────────────────────────────────────

function LineasDesdeOc({
  form,
  asignadas,
}: {
  form: ReturnType<typeof useForm<RecepcionFormValues>>;
  /** articuloId → racks asignados (para avisar al helper de cabecera). */
  asignadas: ReadonlyMap<string, ReadonlySet<string>>;
}) {
  const ordenCompraId = useWatch({
    control: form.control,
    name: 'ordenCompraId',
  });
  const ocQuery = useOrdenCompra(ordenCompraId || null);
  const recibiblesQuery = useLineasRecibibles(ordenCompraId || null);
  const ubicacionesQuery = useUbicaciones({ limit: 500 });
  const ocId = ordenCompraId || null;
  const ultimaOcCargada = useRef<string | null>(null);

  useEffect(() => {
    if (!ocId) {
      if (ultimaOcCargada.current !== null) {
        ultimaOcCargada.current = null;
        form.setValue('filas', []);
        form.setValue('importacion', null);
      }
      return;
    }
    if (ocQuery.data == null || recibiblesQuery.data == null) return;
    if (ultimaOcCargada.current === ocId) return;
    ultimaOcCargada.current = ocId;
    form.setValue('filas', construirFilas(ocQuery.data.lineas, recibiblesQuery.data));
    form.setValue('importacion', ocQuery.data.esImportacion ? {
      numeroPedimento: ocQuery.data.infoImportNumeroPedimento ?? '',
      numeroBl: ocQuery.data.infoImportNumeroBl ?? '',
      numeroContenedor: ocQuery.data.infoImportNumeroContenedor ?? '',
      aduana: '', clavePedimento: '',
    } : null);
  }, [ocId, ocQuery.data, recibiblesQuery.data, form]);

  const filas = useWatch({ control: form.control, name: 'filas' }) ?? [];
  const ubicacionHelperId = useWatch({
    control: form.control,
    name: 'ubicacionHelperId',
  });
  const subsEnRevision = useSubAlmacenesEnRevision();

  if (!ocId) {
    return (
      <section className="rounded-md border border-dashed border-line-dashed bg-surface-subtle p-4 text-sm text-ink-muted">
        Selecciona una orden de compra para cargar las partidas pendientes de recepción.
      </section>
    );
  }

  if (ocQuery.isLoading || recibiblesQuery.isLoading) {
    return (
      <section className="rounded-md border border-dashed border-line-dashed bg-surface-subtle p-4 text-sm text-ink-muted">
        Cargando partidas de la OC…
      </section>
    );
  }

  if (ocQuery.isError || recibiblesQuery.isError) {
    return (
      <AvisoEnLinea tono="error">
        No se pudo cargar la OC. Verifica el id o vuelve a intentar.
      </AvisoEnLinea>
    );
  }

  if (filas.length === 0) {
    // CA2.10: una OC solo de servicios no tiene nada que recibir en Almacén.
    const soloServicios =
      (ocQuery.data?.lineas.length ?? 0) > 0 && ocQuery.data?.lineas.every((l) => l.esServicio);
    return (
      <section className="rounded-md border border-dashed border-line-dashed bg-surface-subtle p-4 text-sm text-ink-muted">
        {soloServicios
          ? 'Esta OC solo tiene servicios — los servicios no se reciben en Almacén ni generan existencia.'
          : 'Esta OC no tiene partidas pendientes de recepción — todas ya fueron recibidas en su totalidad.'}
      </section>
    );
  }

  // PR4: divergencias vs el helper de cabecera (solo líneas incluidas que ya
  // traían otra ubicación — las vacías las auto-asignó el helper).
  const ubicInfo = new Map((ubicacionesQuery.data?.items ?? []).map((u) => [u.id, u]));
  const destinos = destinoPorFila(filas, ubicacionHelperId, ubicInfo, asignadas, subsEnRevision.ids);
  const lineasEnOtraUbicacion = contarLineasEnOtraUbicacion(filas, destinos);
  // Partida que no es de A+W a la que el helper (un rack) no pudo dar destino (su sub-almacén no admite general).
  const sinRackDelHelper =
    ubicacionHelperId && !ubicInfo.get(ubicacionHelperId)?.esDefault
      ? filas.filter(
          (f, i) => f.incluida && !f.medida && !f.ubicacionId && destinos[i] == null,
        ).length
      : 0;
  // Un movimiento = un sub-almacén (RECEPCION_MULTI_SUBALMACEN), avisado por línea.
  const subPorUbicacion = new Map(
    (ubicacionesQuery.data?.items ?? []).map((u) => [
      u.id,
      { id: u.subAlmacenId, clave: u.subAlmacenClave },
    ]),
  );
  const { subBaseClave, conflictos } = detectarConflictoSubAlmacen(filas, subPorUbicacion);

  const errorArrayLevel = form.formState.errors.filas;
  const errorMessage =
    errorArrayLevel && !Array.isArray(errorArrayLevel) ? errorArrayLevel.message : undefined;

  return (
    <section className="space-y-2">
      {ocQuery.data?.esImportacion && <DatosImportacionRecepcion form={form} />}
      <h3 className="text-sm font-semibold">Partidas de la OC ({filas.length} pendientes)</h3>
      {ocQuery.data?.esImportacion && filas.map((fila, index) => fila.incluida && (
        <Field key={fila.lineaOcId} label={`Fracción arancelaria de la partida ${fila.posicion}`} error={form.formState.errors.filas?.[index]?.fraccionArancelaria?.message}>
          <Input aria-label={`Fracción arancelaria de la partida ${fila.posicion}`} {...form.register(`filas.${index}.fraccionArancelaria`)} />
        </Field>
      ))}

      {errorMessage && (
        <p role="alert" className="text-xs text-danger-fg">
          {errorMessage}
        </p>
      )}

      {conflictos.size > 0 && (
        <AvisoEnLinea tono="error" titulo="Racks de sub-almacenes distintos">
          Una recepción va a un solo sub-almacén ({subBaseClave}). Corrige el rack de las partidas
          marcadas.
        </AvisoEnLinea>
      )}

      {sinRackDelHelper > 0 && (
        <AvisoEnLinea tono="advertencia">
          {sinRackDelHelper === 1
            ? '1 partida no tiene asignado el rack por defecto: elige su rack.'
            : `${sinRackDelHelper} partidas no tienen asignado el rack por defecto: elige su rack.`}
        </AvisoEnLinea>
      )}

      {/* PR4: aviso informativo (no bloquea) cuando el helper de cabecera
          diverge de líneas que ya tenían otra ubicación capturada. */}
      {lineasEnOtraUbicacion > 0 && (
        <AvisoEnLinea tono="advertencia">
          {lineasEnOtraUbicacion === 1
            ? '1 partida va a otra ubicación distinta de la predeterminada.'
            : `${lineasEnOtraUbicacion} partidas van a otra ubicación distinta de la predeterminada.`}
        </AvisoEnLinea>
      )}

      <TableContainer className="rounded-md border border-line-row">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-8">
                <span className="sr-only">Incluir</span>
              </TableHead>
              <TableHead>Artículo</TableHead>
              <TableHead className="text-right">Pendiente</TableHead>
              <TableHead className="w-40">Cantidad a recibir</TableHead>
              <TableHead className="min-w-56">Ubicación</TableHead>
              <TableHead>
                <span className="sr-only">Detalles</span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {filas.map((fila, index) => (
              <FilaLineaOc
                key={fila.lineaOcId}
                index={index}
                fila={fila}
                control={form.control}
                coincideConHelper={destinos[index] != null && fila.ubicacionId === destinos[index]}
                conflictoSub={
                  conflictos.has(index) && subBaseClave
                    ? `Este rack es de otro sub-almacén; la recepción va a ${subBaseClave}.`
                    : undefined
                }
                errors={
                  Array.isArray(form.formState.errors.filas)
                    ? (form.formState.errors.filas[index] as FilaErrors | undefined)
                    : undefined
                }
              />
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    </section>
  );
}

interface FilaErrors {
  cantidad?: { message?: string };
  ubicacionReferencia?: { message?: string };
  ubicacionId?: { message?: string };
  comentario?: { message?: string };
  cantidadDanada?: { message?: string };
  motivoDano?: { message?: string };
  lote?: { message?: string };
}

function FilaLineaOc({
  index,
  fila,
  control,
  coincideConHelper,
  conflictoSub,
  errors,
}: {
  index: number;
  fila: FilaRecepcionValues;
  control: ReturnType<typeof useForm<RecepcionFormValues>>['control'];
  /** PR4: la ubicación de la fila es la misma que el helper de cabecera. */
  coincideConHelper: boolean;
  /** Mensaje de conflicto de sub-almacén (RECEPCION_MULTI_SUBALMACEN) para la fila. */
  conflictoSub: string | undefined;
  errors: FilaErrors | undefined;
}) {
  const incluida = useWatch({
    control,
    name: `filas.${index}.incluida` as const,
  });
  const [detalles, setDetalles] = useState(false);
  // Con hojas dañadas el motivo es obligatorio: los detalles se abren solos para que se vea.
  const danadas = useWatch({ control, name: `filas.${index}.cantidadDanada` as const }) ?? 0;
  const motivoRequerido = incluida && danadas > 0;
  const detallesAbiertos = detalles || motivoRequerido;
  const lookupFila = useDecimalesUnidad();
  const decResuelto = decimalesDeFila(lookupFila, {
    unidadMedida: fila.unidadMedida,
  });
  const decimalesFila = decResuelto ?? DECIMALES_FALLBACK;
  const unidadNoResoluble = decResuelto == null;
  const errorCantidad = errors?.cantidad?.message;
  const errorRack = errors?.ubicacionId?.message ?? conflictoSub;
  const hojasPend = fila.medida ? hojasPendientes(fila.pendiente, fila.medida.areaHojaM2) : 0;
  const textoHojas = hojasPend === 1 ? 'hoja' : 'hojas';
  // Vidrio sin medida en la OC: se avisa de entrada, antes que cualquier error de decimales.
  const sinMedidaEnOc = !!fila.requiereMedida && !fila.medida;
  const idCantidad = `fila-${index}-cantidad`;
  const idLote = `fila-${index}-lote`;
  const errorLote = errors?.lote?.message;
  const idRack = `fila-${index}-rack`;

  return (
    <>
      <TableRow className={cn(incluida && 'bg-surface-selected')}>
        <TableCell className="align-top">
          <Controller
            name={`filas.${index}.incluida` as const}
            control={control}
            render={({ field }) => (
              <input
                type="checkbox"
                className="mt-1 size-4 cursor-pointer accent-brand"
                checked={field.value}
                disabled={sinMedidaEnOc}
                onChange={(e) => field.onChange(e.target.checked)}
                aria-label={`Incluir partida ${fila.posicion} en la recepción`}
              />
            )}
          />
        </TableCell>

        <TableCell className="align-top">
          <span className="text-ink-muted">#{fila.posicion} </span>
          {fila.articuloClave ? (
            <>
              <code className="font-mono text-xs">{fila.articuloClave}</code>
              {fila.articuloNombre && (
                <div className="max-w-72 truncate" title={fila.articuloNombre}>
                  {fila.articuloNombre}
                </div>
              )}
            </>
          ) : (
            <code className="font-mono text-xs" title={fila.articuloId}>
              {fila.articuloId.slice(0, 8)}…
            </code>
          )}
        </TableCell>

        <TableCell className="whitespace-nowrap text-right align-top tabular-nums">
          {fila.medida ? hojasPend : formatCantidad(fila.pendiente, decResuelto)}{' '}
          <span className="text-xs text-ink-muted">{fila.medida ? textoHojas : fila.unidadMedida}</span>
        </TableCell>

        <TableCell className="align-top">
          {sinMedidaEnOc && (
            <p className="text-xs text-danger-fg" role="alert">
              La línea de la OC no tiene medida; corrígela en Compras.
            </p>
          )}
          {fila.medida && (
            <p className="mb-1 text-xs text-ink">
              {etiquetaMedidaMm(fila.medida.anchoMm, fila.medida.altoMm)} · pendiente {hojasPend} {textoHojas}
            </p>
          )}
              <Controller
                name={`filas.${index}.cantidad` as const}
                control={control}
                render={({ field }) => (
                  <Input
                    id={idCantidad}
                    type="number"
                    inputMode={fila.medida ? 'numeric' : 'decimal'}
                    step={fila.medida ? 1 : stepParaDecimales(decimalesFila)}
                    min="0"
                    max={fila.medida ? hojasPend : fila.maximo}
                    disabled={!incluida}
                    className="min-w-24 text-right tabular-nums"
                    value={field.value ?? ''}
                    onChange={(e) => field.onChange(e.target.value === '' ? 0 : Number(e.target.value))}
                    aria-label={`${fila.medida ? 'Hojas' : 'Cantidad'} a recibir, partida ${fila.posicion}`}
                    aria-required={incluida || undefined}
                    aria-invalid={errorCantidad ? true : undefined}
                    aria-describedby={`${idCantidad}-ayuda`}
                  />
                )}
              />
              <p
                id={`${idCantidad}-ayuda`}
                className={cn('mt-1 text-2xs', errorCantidad ? 'text-danger-fg' : 'text-ink-muted')}
                role={errorCantidad ? 'alert' : undefined}
              >
                {errorCantidad ??
                  (fila.medida
                    ? 'Hojas enteras'
                    : `Pendiente ${formatCantidad(fila.pendiente, decResuelto)}`)}
              </p>
              <AvisoUnidadNoResoluble visible={unidadNoResoluble && !fila.medida} />
              {!fila.esActivoFijo && (
                <div className="mt-2">
                  <Controller
                    name={`filas.${index}.lote` as const}
                    control={control}
                    render={({ field }) => (
                      <Input
                        id={idLote}
                        value={field.value ?? ''}
                        onChange={field.onChange}
                        maxLength={50}
                        disabled={!incluida}
                        placeholder={fila.medida ? 'Lote del proveedor' : 'Lote (opcional)'}
                        aria-label={`Lote del proveedor, partida ${fila.posicion}`}
                        aria-required={(incluida && !!fila.medida) || undefined}
                        aria-invalid={errorLote ? true : undefined}
                        aria-describedby={errorLote ? `${idLote}-error` : undefined}
                      />
                    )}
                  />
                  {errorLote && (
                    <p id={`${idLote}-error`} role="alert" className="mt-1 text-2xs text-danger-fg">
                      {errorLote}
                    </p>
                  )}
                </div>
              )}
        </TableCell>

        <TableCell className="align-top">
          {fila.esActivoFijo ? (
            <span className="text-xs text-ink-muted">Activo fijo · sin existencia</span>
          ) : (
            <Controller
              name={`filas.${index}.ubicacionId` as const}
              control={control}
              render={({ field }) => (
                <UbicacionBinSelector
                  modo="asignacion"
                  articuloId={fila.articuloId}
                  subAlmacenId={null}
                  requiereSubAlmacen={false}
                  value={field.value ?? null}
                  onChange={(id) => field.onChange(id)}
                  disabled={!incluida}
                  incluirGeneral={!fila.medida}
                  accionSinUbicaciones={
                    fila.medida ? (
                      <AsignarUbicacionEnLinea
                        articuloId={fila.articuloId}
                        onAsignada={(ubicacionId) => field.onChange(ubicacionId)}
                      />
                    ) : undefined
                  }
                />
              )}
            />
          )}
          {coincideConHelper && <IndicadorAlmacenBadge estado="rackPredeterminado" />}
          {errorRack && (
            <p id={idRack} role="alert" className="mt-1 text-2xs text-danger-fg">
              {errorRack}
            </p>
          )}
        </TableCell>

        <TableCell className="whitespace-nowrap align-top">
          <Button
            type="button"
            variant="ghost"
            size="sm"
            aria-expanded={detallesAbiertos}
            onClick={() => setDetalles((v) => !v)}
          >
            {detallesAbiertos ? '− Detalles' : '+ Detalles'}
          </Button>
        </TableCell>
      </TableRow>

      {detallesAbiertos && (
        <TableRow className="bg-surface-subtle">
          <TableCell />
          <TableCell colSpan={5}>
            <dl className="mb-2 flex gap-6 text-xs text-ink-muted">
              <div>
                <dt>Solicitada</dt>
                <dd className="tabular-nums text-ink">
                  {formatCantidad(fila.cantidadSolicitada, decResuelto)}
                </dd>
              </div>
              <div>
                <dt>Ya recibida</dt>
                <dd className="tabular-nums text-ink">
                  {formatCantidad(fila.cantidadYaRecibida, decResuelto)}
                </dd>
              </div>
            </dl>
            <FormGrid>
              {!fila.medida && (
              <FormCampo span={6}>
                <Field
                  label="Unidad capturada"
                  optional
                  hint="Usa el código del catálogo (por ejemplo CAJA). El sistema convierte y valida el saldo al registrar."
                >
                  <Controller
                    name={`filas.${index}.unidadCapturada` as const}
                    control={control}
                    render={({ field }) => (
                      <Input
                        {...field}
                        value={field.value ?? ''}
                        maxLength={20}
                        placeholder={fila.unidadMedida}
                      />
                    )}
                  />
                </Field>
              </FormCampo>
              )}
              <FormCampo span={6}>
                <Field label="Comentario" optional error={errors?.comentario?.message}>
                  <Controller
                    name={`filas.${index}.comentario` as const}
                    control={control}
                    render={({ field }) => (
                      <Input {...field} value={field.value ?? ''} maxLength={500} />
                    )}
                  />
                </Field>
              </FormCampo>
              <FormCampo span={6}>
                <Field
                  label={fila.medida ? 'Hojas dañadas' : 'Cantidad dañada'}
                  optional
                  hint="Se envía a material en revisión; no queda disponible."
                  error={errors?.cantidadDanada?.message}
                >
                  <Controller
                    name={`filas.${index}.cantidadDanada` as const}
                    control={control}
                    render={({ field }) => (
                      <Input
                        type="number"
                        inputMode={fila.medida ? 'numeric' : 'decimal'}
                        min="0"
                        step={fila.medida ? 1 : stepParaDecimales(decimalesFila)}
                        className="tabular-nums"
                        value={field.value ?? ''}
                        onChange={(e) =>
                          field.onChange(e.target.value === '' ? 0 : Number(e.target.value))
                        }
                      />
                    )}
                  />
                </Field>
              </FormCampo>
              <FormCampo span={6}>
                <Field
                  label="Motivo del daño"
                  required={motivoRequerido}
                  optional={!motivoRequerido}
                  error={errors?.motivoDano?.message}
                >
                  <Controller
                    name={`filas.${index}.motivoDano` as const}
                    control={control}
                    render={({ field }) => (
                      <Input {...field} value={field.value ?? ''} maxLength={500} />
                    )}
                  />
                </Field>
              </FormCampo>
            </FormGrid>
          </TableCell>
        </TableRow>
      )}
    </>
  );
}

function VarianteOption({
  value,
  active,
  onSelect,
  titulo,
  descripcion,
}: {
  value: Variante;
  active: boolean;
  onSelect: (v: Variante) => void;
  titulo: string;
  descripcion: string;
}) {
  return (
    <button
      type="button"
      onClick={() => onSelect(value)}
      aria-pressed={active}
      className={cn(
        'flex-1 rounded-md border p-3 text-left transition-colors',
        active ? 'border-brand bg-brand-soft' : 'border-line-control hover:border-brand',
      )}
    >
      <div className="text-sm font-medium">
        Variante {value} — {titulo}
      </div>
      <div className="mt-0.5 text-xs text-ink-muted">{descripcion}</div>
    </button>
  );
}

type RecepcionFormValues = {
  importacion?: ImportacionRecepcionValues | null;
  ordenCompraId: string;
  fechaMovimiento: string;
  observaciones?: string | null;
  cfdiRecibidoId?: string | null;
  cfdiUuidFiscal?: string | null;
  packingListBlobRef?: string;
  /** PR4: helper de cabecera (bin N4). null = no lo usó. */
  ubicacionHelperId?: string | null;
  filas: FilaRecepcionValues[];
};

function defaultValuesParaVariante(
  variante: Variante,
  hoyIso: string,
  ordenCompraId = '',
): RecepcionFormValues {
  const base: RecepcionFormValues = {
    ordenCompraId,
    fechaMovimiento: hoyIso,
    observaciones: null,
    ubicacionHelperId: null,
    filas: [],
  };
  if (variante === 'A') {
    return { ...base, cfdiRecibidoId: null, cfdiUuidFiscal: null };
  }
  return { ...base, packingListBlobRef: '' };
}

function DatosImportacionRecepcion({ form }: { form: import('react-hook-form').UseFormReturn<RecepcionFormValues> }) {
  const catalogo = useQuery({
    queryKey: ['almacen', 'catalogos-importacion'],
    queryFn: async () => (await apiRequest<{ demo: boolean; aduanas: Record<string, string>; clavesPedimento: Record<string, string> }>('/api/v1/almacen/recepciones/catalogos-importacion')).data,
  });
  return (
    <FormSeccion titulo="Datos de importación" descripcion="Pedimento, BL o guía aérea y contenedor son obligatorios para confirmar. Aduanas y claves de pedimento: lista parcial mientras se carga el catálogo SAT completo.">
        {([['numeroPedimento', 'Número de pedimento'], ['numeroBl', 'BL o guía aérea'], ['numeroContenedor', 'Número de contenedor']] as const).map(([campo, etiqueta]) => (
          <FormCampo key={campo} span={6}>
            <Field label={etiqueta} required error={form.formState.errors.importacion?.[campo]?.message}>
              <Input aria-label={etiqueta} {...form.register(`importacion.${campo}`)} />
            </Field>
          </FormCampo>
        ))}
        {([['aduana', 'Aduana', catalogo.data?.aduanas], ['clavePedimento', 'Clave de pedimento', catalogo.data?.clavesPedimento]] as const).map(([campo, etiqueta, opciones]) => (
          <FormCampo key={campo} span={6}>
            <Field label={etiqueta} required error={form.formState.errors.importacion?.[campo]?.message}>
              <Controller control={form.control} name={`importacion.${campo}`} render={({ field }) => (
                <Select value={field.value ?? ''} onValueChange={field.onChange}>
                  <SelectTrigger aria-label={etiqueta}><SelectValue placeholder="Seleccionar" /></SelectTrigger>
                  <SelectContent>{Object.entries(opciones ?? {}).map(([clave, nombre]) => <SelectItem key={clave} value={clave}>{clave} · {nombre}</SelectItem>)}</SelectContent>
                </Select>
              )} />
            </Field>
          </FormCampo>
        ))}
      {catalogo.isError && <p role="alert" className="text-sm text-danger-fg">No se pudo cargar el catálogo de importación. Vuelve a abrir la recepción para reintentar.</p>}
    </FormSeccion>
  );
}
