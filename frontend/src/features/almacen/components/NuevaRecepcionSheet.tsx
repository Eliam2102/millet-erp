import { hoyLocalISO } from '@/lib/datetime';
import { useEffect, useMemo, useRef, useState } from 'react';
import {
  Controller,
  useForm,
  useWatch,
  type Resolver,
} from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigate } from '@tanstack/react-router';
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
import {
  AvisoUnidadNoResoluble,
  decimalesDeFila,
  DECIMALES_FALLBACK,
  evaluarDecimalesFila,
  MENSAJE_DECIMALES_UNIDAD,
  OrdenCompraSelector,
  stepParaDecimales,
  TextAreaField,
  useDecimalesUnidad,
} from '@/components/erp';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  RegistrarRecepcionFacturaSchema,
  RegistrarRecepcionPackingListSchema,
  TOLERANCIA_RECEPCION_DEFAULT,
  type FilaRecepcionValues,
  type RegistrarRecepcionFacturaValues,
  type RegistrarRecepcionPackingListValues,
} from '@/features/almacen/schemas/recepcion';
import {
  useRegistrarRecepcionFactura,
  useRegistrarRecepcionPackingList,
} from '@/features/almacen/api';
import { PackingListUpload } from '@/features/almacen/components/PackingListUpload';
import { UbicacionBinSelector } from '@/components/erp/selectors/UbicacionBinSelector';
import { UbicacionSelector } from '@/components/erp/selectors/UbicacionSelector';
import {
  construirFilas,
  contarLineasEnOtraUbicacion,
  indicesParaAutoAsignar,
} from '@/features/almacen/components/nueva-recepcion-helpers';
import { useOrdenCompra } from '@/features/compras/ordenes/api/useOrdenCompra';
import { useProveedor } from '@/features/catalogos/api';
import { CfdiPorProcesarPicker } from '@/features/cxp/components/CfdiPorProcesarPicker';
import { CargarCfdiSheet } from '@/features/cxp/components/CargarCfdiSheet';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { cn } from '@/lib/utils';

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
}: NuevaRecepcionSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-3xl">
        <SheetHeader>
          <SheetTitle>Nueva recepción</SheetTitle>
          <SheetDescription>
            Selecciona la OC autorizada y marca las líneas que llegaron.
            El costo se toma de la OC; la cantidad valida tolerancia
            server-side.
          </SheetDescription>
        </SheetHeader>

        {open && (
          <NuevaRecepcionFormBody
            onSuccess={() => onOpenChange(false)}
            onCancel={() => onOpenChange(false)}
          />
        )}
      </SheetContent>
    </Sheet>
  );
}

function NuevaRecepcionFormBody({
  onSuccess,
  onCancel,
}: {
  onSuccess: () => void;
  onCancel: () => void;
}) {
  const navigate = useNavigate();
  const idempotencyKey = useFormIdempotencyKey();
  const [variante, setVariante] = useState<Variante>('A');
  const [packingListAdjunto, setPackingListAdjunto] =
    useState<PackingListAdjunto | null>(null);
  // Variante A — vínculo fiscal: sheet apilado de carga de XML + modo
  // "folio fiscal a mano" (cuando el CFDI aún no está en el ERP).
  const [cargarCfdiOpen, setCargarCfdiOpen] = useState(false);
  const [capturaUuidManual, setCapturaUuidManual] = useState(false);

  const registrarA = useRegistrarRecepcionFactura();
  const registrarB = useRegistrarRecepcionPackingList();

  const hoyIso = useMemo(() => hoyLocalISO(), []);

  const resolver: Resolver<RecepcionFormValues> = useMemo(() => {
    const schema =
      variante === 'A'
        ? RegistrarRecepcionFacturaSchema
        : RegistrarRecepcionPackingListSchema;
    return zodResolver(schema) as Resolver<RecepcionFormValues>;
  }, [variante]);

  const form = useForm<RecepcionFormValues>({
    resolver,
    defaultValues: defaultValuesParaVariante('A', hoyIso),
  });

  function cambiarVariante(siguiente: Variante) {
    if (siguiente === variante) return;
    setVariante(siguiente);
    setPackingListAdjunto(null);
    setCapturaUuidManual(false);
    form.reset(defaultValuesParaVariante(siguiente, hoyIso));
  }

  // ── Helper de cabecera nivel 4 (bin por defecto) ─────────────────────────
  // Sin sub-almacén de cabecera (se deriva del bin), el helper lista TODAS las
  // ubicaciones con su ruta vía <UbicacionSelector>.
  /**
   * Aplica el helper: guarda la elección en la cabecera y auto-asigna el bin a
   * las líneas que aún NO tienen ubicación. Las que ya tienen otra se respetan
   * (el aviso de divergencia se muestra sobre la lista de líneas).
   */
  function aplicarUbicacionHelper(ubicacionId: string | null) {
    form.setValue('ubicacionHelperId', ubicacionId, { shouldDirty: true });
    if (!ubicacionId) return;
    for (const index of indicesParaAutoAsignar(form.getValues('filas') ?? [])) {
      form.setValue(`filas.${index}.ubicacionId`, ubicacionId, {
        shouldDirty: true,
        shouldValidate: true,
      });
    }
  }

  const lookup = useDecimalesUnidad();

  // Vínculo fiscal (variante A): el picker de CFDIs se acota al RFC del
  // proveedor de la OC. La query de OC se dedupe con la de LineasDesdeOc
  // (misma queryKey); el RFC sale del catálogo de proveedores.
  const ordenCompraId = useWatch({
    control: form.control,
    name: 'ordenCompraId',
  });
  const ocQuery = useOrdenCompra(ordenCompraId || null);
  const proveedorQuery = useProveedor(ocQuery.data?.proveedorId ?? null);
  const rfcProveedor = proveedorQuery.data?.rfc;
  const puedeLeerCfdis = useHasPermission(
    PermisosCanonicos.CuentasPorPagarCfdisLeer,
  );
  const puedeCargarCfdi = useHasPermission(
    PermisosCanonicos.CuentasPorPagarCfdisCargarManual,
  );

  // Al cambiar de OC, el CFDI seleccionado deja de corresponder al
  // proveedor — se descarta (no aplica al primer render).
  const ocPrevia = useRef<string>(ordenCompraId ?? '');
  useEffect(() => {
    if (ocPrevia.current === (ordenCompraId ?? '')) return;
    ocPrevia.current = ordenCompraId ?? '';
    if (variante === 'A') {
      form.setValue('cfdiRecibidoId', null);
    }
  }, [ordenCompraId, variante, form]);

  function onSubmit(values: RecepcionFormValues) {
    // Advisory: decimales por unidad (solo filas incluidas; backend autoritativo).
    let decimalesMal = false;
    values.filas.forEach((f, i) => {
      if (!f.incluida) return;
      // Solo 'invalidos' bloquea; 'no-resoluble' avisa (sin bloquear) en la fila.
      if (
        evaluarDecimalesFila(lookup, {
          unidadMedida: f.unidadMedida,
          cantidad: f.cantidad,
        }) === 'invalidos'
      ) {
        form.setError(
          `filas.${i}.cantidad` as Parameters<typeof form.setError>[0],
          { type: 'decimales', message: MENSAJE_DECIMALES_UNIDAD },
        );
        decimalesMal = true;
      }
    });
    if (decimalesMal) return;
    const lineasParaBackend = values.filas
      .filter((f) => f.incluida)
      .map((f) => ({
        articuloId: f.articuloId,
        lineaOcId: f.lineaOcId,
        cantidad: f.cantidad,
        ubicacionReferencia: f.ubicacionReferencia ?? null,
        ubicacionId: f.ubicacionId ?? null,
        comentario: f.comentario ?? null,
      }));

    if (variante === 'A') {
      const v = values as RegistrarRecepcionFacturaValues;
      registrarA.mutate(
        {
          command: {
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
    if (esApiError(error)) {
      if (error.code === 'RECEPCION_EXCEDE_TOLERANCIA') {
        toast.error('Cantidad excede tolerancia de la línea de OC.', {
          description:
            error.problem.detail ??
            'Ajusta la cantidad o solicita autorización a tu supervisor.',
        });
        return;
      }
      if (error.code === 'RECEPCION_SIN_CFDI') {
        form.setError('cfdiRecibidoId', {
          type: error.code,
          message:
            'Vincula el CFDI del proveedor o captura su folio fiscal (UUID).',
        });
        return;
      }
      if (error.code === 'RECEPCION_OC_NO_AUTORIZADA') {
        form.setError('ordenCompraId', {
          type: error.code,
          message: 'La OC no está en estado que acepte recepción.',
        });
        return;
      }
      if (error.code === 'RECEPCION_MULTI_SUBALMACEN') {
        toast.error('Las líneas van a sub-almacenes distintos.', {
          description:
            'Todas las ubicaciones (racks) de la recepción deben pertenecer al mismo sub-almacén.',
        });
        return;
      }
      if (
        applyServerErrors(
          form as unknown as Parameters<typeof applyServerErrors>[0],
          error,
        )
      ) {
        return;
      }
      toast.error(error.problem.title, {
        description: error.traceId ? `Código: ${error.traceId}` : undefined,
      });
      return;
    }
    toast.error('Error inesperado al registrar la recepción.');
  }

  const isPending = registrarA.isPending || registrarB.isPending;

  return (
    <>
      <form
        id="nueva-recepcion-form"
        onSubmit={form.handleSubmit(onSubmit)}
        noValidate
        className="flex-1 space-y-4 overflow-y-auto px-6"
      >
        <fieldset className="space-y-2">
          <legend className="text-sm font-medium">Tipo de recepción</legend>
          <div className="flex gap-2">
            <VarianteOption
              value="A"
              active={variante === 'A'}
              onSelect={cambiarVariante}
              titulo="Con factura/CFDI"
              descripcion="Insumos y refacciones — el proveedor llega con factura."
            />
            <VarianteOption
              value="B"
              active={variante === 'B'}
              onSelect={cambiarVariante}
              titulo="Con packing list"
              descripcion="Materiales directos no-vidrio — factura llega después por CxP."
            />
          </div>
        </fieldset>

        <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
          <Field
            label="Orden de compra"
            required
            error={form.formState.errors.ordenCompraId?.message}
          >
            <Controller
              name="ordenCompraId"
              control={form.control}
              render={({ field }) => (
                <OrdenCompraSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? '')}
                  soloConPendienteRecepcion
                />
              )}
            />
          </Field>

          {/* PR4: helper de cabecera nivel 4. Opcional — al elegirlo, auto-aplica
              el bin a las líneas que aún no tienen ubicación. Las que ya tienen
              otra NO se tocan (el aviso de divergencia sale sobre las líneas).
              Se persiste para reportería; null = el almacenista no lo usó. */}
          <Field
            label="Ubicación por defecto (opcional)"
            error={form.formState.errors.ubicacionHelperId?.message}
          >
            <Controller
              name="ubicacionHelperId"
              control={form.control}
              render={({ field }) => (
                <UbicacionSelector
                  value={field.value || null}
                  onChange={(id) => aplicarUbicacionHelper(id)}
                  placeholder="Sin ubicación por defecto"
                />
              )}
            />
          </Field>

          <Field
            label="Fecha de movimiento"
            required
            error={form.formState.errors.fechaMovimiento?.message}
          >
            <Controller
              name="fechaMovimiento"
              control={form.control}
              render={({ field }) => (
                <Input
                  {...field}
                  type="date"
                  value={field.value ?? ''}
                />
              )}
            />
          </Field>

          {variante === 'A' && (
            <div className="space-y-1.5 md:col-span-2">
              <label className="flex items-center gap-1 text-sm font-medium">
                CFDI del proveedor
                <span aria-hidden="true" className="text-rose-600">
                  *
                </span>
              </label>

              {!ordenCompraId ? (
                <p className="rounded-md border border-dashed bg-muted/30 p-3 text-xs text-muted-foreground">
                  Selecciona la orden de compra para vincular el CFDI del
                  proveedor.
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

                  {capturaUuidManual || !puedeLeerCfdis ? (
                    <div className="space-y-1.5 rounded-md border border-dashed border-primary/60 p-3">
                      <label
                        htmlFor="cfdi-uuid-fiscal"
                        className="text-sm font-medium"
                      >
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
                              field.onChange(
                                e.target.value === '' ? null : e.target.value,
                              )
                            }
                            placeholder="AD662D33-6934-459C-A128-BDF0393E0062"
                            spellCheck={false}
                            className="font-mono uppercase"
                          />
                        )}
                      />
                      {form.formState.errors.cfdiUuidFiscal?.message && (
                        <p role="alert" className="text-xs text-rose-600">
                          {form.formState.errors.cfdiUuidFiscal.message}
                        </p>
                      )}
                      <p className="text-xs text-muted-foreground">
                        Viene impreso en la representación del CFDI (y en su
                        QR). CxP enlazará el CFDI cuando el XML llegue por su
                        canal.
                      </p>
                      {puedeLeerCfdis && (
                        <button
                          type="button"
                          className="text-xs text-primary underline-offset-2 hover:underline"
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
                      className="text-xs text-primary underline-offset-2 hover:underline"
                      onClick={() => {
                        setCapturaUuidManual(true);
                        form.setValue('cfdiRecibidoId', null);
                      }}
                    >
                      ¿El CFDI no está en el sistema? Captura el folio fiscal
                      del impreso
                    </button>
                  )}

                  {form.formState.errors.cfdiRecibidoId?.message && (
                    <p role="alert" className="text-xs text-rose-600">
                      {form.formState.errors.cfdiRecibidoId.message}
                    </p>
                  )}
                </>
              )}
            </div>
          )}
        </div>

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

        <LineasDesdeOc form={form} />
      </form>

      <SheetFooter>
        <Button
          type="button"
          variant="ghost"
          onClick={onCancel}
          disabled={isPending}
        >
          Cancelar
        </Button>
        <Button
          type="submit"
          form="nueva-recepcion-form"
          disabled={isPending}
        >
          {isPending ? 'Registrando…' : 'Registrar recepción'}
        </Button>
      </SheetFooter>

      {/* Sheet apilado: carga del XML del proveedor (canal CargaManual de
          CxP). Al cargar, el CFDI recién ingresado queda vinculado. */}
      <CargarCfdiSheet
        open={cargarCfdiOpen}
        onOpenChange={setCargarCfdiOpen}
        onCargado={(resp) => {
          form.setValue('cfdiRecibidoId', resp.id, { shouldValidate: true });
          form.setValue('cfdiUuidFiscal', null);
          setCapturaUuidManual(false);
        }}
      />
    </>
  );
}

// ─── Líneas desde OC ───────────────────────────────────────────────────────

function LineasDesdeOc({
  form,
}: {
  form: ReturnType<typeof useForm<RecepcionFormValues>>;
}) {
  const ordenCompraId = useWatch({
    control: form.control,
    name: 'ordenCompraId',
  });
  const ocQuery = useOrdenCompra(ordenCompraId || null);
  const ocId = ordenCompraId || null;
  const ultimaOcCargada = useRef<string | null>(null);

  useEffect(() => {
    if (!ocId) {
      if (ultimaOcCargada.current !== null) {
        ultimaOcCargada.current = null;
        form.setValue('filas', []);
      }
      return;
    }
    if (ocQuery.data == null) return;
    if (ultimaOcCargada.current === ocId) return;
    ultimaOcCargada.current = ocId;
    form.setValue('filas', construirFilas(ocQuery.data.lineas));
  }, [ocId, ocQuery.data, form]);

  const filas = useWatch({ control: form.control, name: 'filas' }) ?? [];
  const ubicacionHelperId = useWatch({
    control: form.control,
    name: 'ubicacionHelperId',
  });

  if (!ocId) {
    return (
      <section className="rounded-md border border-dashed bg-muted/30 p-4 text-sm text-muted-foreground">
        Selecciona una orden de compra para cargar las líneas pendientes
        de recepción.
      </section>
    );
  }

  if (ocQuery.isLoading) {
    return (
      <section className="rounded-md border border-dashed bg-muted/30 p-4 text-sm text-muted-foreground">
        Cargando líneas de la OC…
      </section>
    );
  }

  if (ocQuery.isError) {
    return (
      <section
        role="alert"
        className="rounded-md border border-rose-300 bg-rose-50 p-4 text-sm text-rose-700"
      >
        No se pudo cargar la OC. Verifica el id o vuelve a intentar.
      </section>
    );
  }

  if (filas.length === 0) {
    return (
      <section className="rounded-md border border-dashed bg-muted/30 p-4 text-sm text-muted-foreground">
        Esta OC no tiene líneas pendientes de recepción — todas ya
        fueron recibidas en su totalidad.
      </section>
    );
  }

  // PR4: divergencias vs el helper de cabecera (solo líneas incluidas que ya
  // traían otra ubicación — las vacías las auto-asignó el helper).
  const lineasEnOtraUbicacion = contarLineasEnOtraUbicacion(
    filas,
    ubicacionHelperId,
  );

  const errorArrayLevel = form.formState.errors.filas;
  const errorMessage =
    errorArrayLevel && !Array.isArray(errorArrayLevel)
      ? errorArrayLevel.message
      : undefined;

  return (
    <section className="space-y-2">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold">
          Líneas de la OC ({filas.length} pendientes)
        </h3>
      </div>

      {errorMessage && (
        <p role="alert" className="text-xs text-rose-600">
          {errorMessage}
        </p>
      )}

      {/* PR4: aviso informativo (no bloquea) cuando el helper de cabecera
          diverge de líneas que ya tenían otra ubicación capturada. */}
      {lineasEnOtraUbicacion > 0 && (
        <p
          role="status"
          className="rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-800"
        >
          {lineasEnOtraUbicacion === 1
            ? '1 línea va a otra ubicación distinta de la predeterminada.'
            : `${lineasEnOtraUbicacion} líneas van a otra ubicación distinta de la predeterminada.`}
        </p>
      )}

      <div className="space-y-2">
        {filas.map((fila, index) => (
          <FilaLineaOc
            key={fila.lineaOcId}
            index={index}
            fila={fila}
            control={form.control}
            coincideConHelper={
              !!ubicacionHelperId && fila.ubicacionId === ubicacionHelperId
            }
            errors={
              Array.isArray(form.formState.errors.filas)
                ? (form.formState.errors.filas[index] as FilaErrors | undefined)
                : undefined
            }
          />
        ))}
      </div>
    </section>
  );
}

interface FilaErrors {
  cantidad?: { message?: string };
  ubicacionReferencia?: { message?: string };
  ubicacionId?: { message?: string };
  comentario?: { message?: string };
}

function FilaLineaOc({
  index,
  fila,
  control,
  coincideConHelper,
  errors,
}: {
  index: number;
  fila: FilaRecepcionValues;
  control: ReturnType<typeof useForm<RecepcionFormValues>>['control'];
  /** PR4: la ubicación de la fila es la misma que el helper de cabecera. */
  coincideConHelper: boolean;
  errors: FilaErrors | undefined;
}) {
  const incluida = useWatch({
    control,
    name: `filas.${index}.incluida` as const,
  });
  const maxConTolerancia = fila.pendiente * (1 + TOLERANCIA_RECEPCION_DEFAULT);
  const lookupFila = useDecimalesUnidad();
  const decResuelto = decimalesDeFila(lookupFila, {
    unidadMedida: fila.unidadMedida,
  });
  const decimalesFila = decResuelto ?? DECIMALES_FALLBACK;
  const unidadNoResoluble = decResuelto == null;

  return (
    <div
      className={cn(
        'rounded-md border p-3',
        incluida
          ? 'border-primary/60 bg-primary/5'
          : 'border-muted bg-muted/20',
      )}
    >
      <div className="flex items-start gap-3">
        <Controller
          name={`filas.${index}.incluida` as const}
          control={control}
          render={({ field }) => (
            <input
              type="checkbox"
              className="mt-1 h-4 w-4 cursor-pointer rounded border-muted-foreground"
              checked={field.value}
              onChange={(e) => field.onChange(e.target.checked)}
              aria-label={`Incluir línea ${fila.posicion} en la recepción`}
            />
          )}
        />

        <div className="flex-1 space-y-2">
          <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1 text-sm">
            <span className="font-semibold">Línea #{fila.posicion}</span>
            <span className="text-xs text-muted-foreground">
              Artículo:{' '}
              {fila.articuloClave ? (
                <>
                  <code className="font-mono">{fila.articuloClave}</code>
                  {fila.articuloNombre && <> · {fila.articuloNombre}</>}
                </>
              ) : (
                <code className="font-mono" title={fila.articuloId}>
                  {fila.articuloId.slice(0, 8)}…
                </code>
              )}
            </span>
            <span className="text-xs text-muted-foreground">UM: {fila.unidadMedida}</span>
          </div>

          <dl className="grid grid-cols-3 gap-2 text-xs">
            <Stat label="Solicitada">{formatCantidad(fila.cantidadSolicitada)}</Stat>
            <Stat label="Ya recibida">{formatCantidad(fila.cantidadYaRecibida)}</Stat>
            <Stat label="Pendiente" highlight>
              {formatCantidad(fila.pendiente)}
            </Stat>
          </dl>

          {incluida && (
            <div className="grid grid-cols-1 gap-2 md:grid-cols-2">
              <Field
                label="Cantidad a recibir"
                required
                error={errors?.cantidad?.message}
              >
                <Controller
                  name={`filas.${index}.cantidad` as const}
                  control={control}
                  render={({ field }) => (
                    <Input
                      type="number"
                      inputMode="decimal"
                      step={stepParaDecimales(decimalesFila)}
                      min="0"
                      max={maxConTolerancia}
                      value={field.value ?? ''}
                      onChange={(e) =>
                        field.onChange(
                          e.target.value === ''
                            ? 0
                            : Number(e.target.value),
                        )
                      }
                      aria-describedby={`fila-${index}-max`}
                    />
                  )}
                />
                <p
                  id={`fila-${index}-max`}
                  className="mt-1 text-[11px] text-muted-foreground"
                >
                  Máximo con 5% tolerancia: {formatCantidad(maxConTolerancia)}
                </p>
                <AvisoUnidadNoResoluble visible={unidadNoResoluble} />
              </Field>

              <Field
                label={
                  coincideConHelper ? (
                    <span className="inline-flex items-center gap-1.5">
                      Ubicación (rack)
                      <span className="rounded-sm bg-emerald-100 px-1.5 py-0.5 text-[10px] font-medium text-emerald-700">
                        coincide
                      </span>
                    </span>
                  ) : (
                    'Ubicación (rack)'
                  )
                }
                error={errors?.ubicacionId?.message}
              >
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
                    />
                  )}
                />
              </Field>

              <div className="md:col-span-2">
                <Field
                  label="Comentario (opcional)"
                  error={errors?.comentario?.message}
                >
                  <Controller
                    name={`filas.${index}.comentario` as const}
                    control={control}
                    render={({ field }) => (
                      <Input
                        {...field}
                        value={field.value ?? ''}
                        maxLength={500}
                      />
                    )}
                  />
                </Field>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function Stat({
  label,
  children,
  highlight,
}: {
  label: string;
  children: React.ReactNode;
  highlight?: boolean;
}) {
  return (
    <div>
      <dt className="text-muted-foreground">{label}</dt>
      <dd
        className={cn(
          'font-mono',
          highlight && 'font-semibold text-primary',
        )}
      >
        {children}
      </dd>
    </div>
  );
}

function formatCantidad(n: number): string {
  return n.toLocaleString('es-MX', {
    minimumFractionDigits: 0,
    maximumFractionDigits: 4,
  });
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
        active
          ? 'border-primary bg-primary/5'
          : 'border-muted hover:border-muted-foreground/50',
      )}
    >
      <div className="text-sm font-medium">
        Variante {value} — {titulo}
      </div>
      <div className="mt-0.5 text-xs text-muted-foreground">{descripcion}</div>
    </button>
  );
}

function Field({
  label,
  required,
  error,
  children,
}: {
  // PR4: acepta ReactNode para poder anexar el badge "coincide" al label.
  label: React.ReactNode;
  required?: boolean;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1.5">
      <label className="flex items-center gap-1 text-sm font-medium">
        {label}
        {required && (
          <span aria-hidden="true" className="text-rose-600">
            *
          </span>
        )}
      </label>
      {children}
      {error != null && (
        <p role="alert" className="text-xs text-rose-600">
          {error}
        </p>
      )}
    </div>
  );
}

type RecepcionFormValues = {
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
): RecepcionFormValues {
  const base: RecepcionFormValues = {
    ordenCompraId: '',
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

