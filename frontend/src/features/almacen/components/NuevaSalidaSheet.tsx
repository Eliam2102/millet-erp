import { hoyLocalISO } from '@/lib/datetime';
import { useEffect, useMemo, useRef, useState } from 'react';
import {
  Controller,
  useFieldArray,
  useForm,
  useWatch,
  type Resolver,
} from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueries } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { Plus, Trash2 } from 'lucide-react';
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
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  ArticuloSelector,
  AvisoUnidadNoResoluble,
  cabeEnDecimales,
  decimalesDeFila,
  DECIMALES_FALLBACK,
  evaluarDecimalesFila,
  MENSAJE_DECIMALES_UNIDAD,
  RequisicionSelector,
  stepParaDecimales,
  TextAreaField,
  useDecimalesUnidad,
  UsuarioSelector,
} from '@/components/erp';
import type { ArticuloListItem } from '@/features/catalogos/api/types';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  RegistrarSalidaConRqSchema,
  RegistrarSalidaPorValeSchema,
  type FilaSalidaValues,
  type RegistrarSalidaConRqValues,
  type RegistrarSalidaPorValeValues,
} from '@/features/almacen/schemas/salida';
import {
  useRegistrarSalidaConRq,
  useRegistrarSalidaPorVale,
} from '@/features/almacen/api';
import { ValeUpload } from '@/features/almacen/components/ValeUpload';
import { formatCcMaquinaLabel } from '@/features/centros-costo/lib/cc-maquina-label';
import { Dim3Picker } from '@/features/centros-costo/components/Dim3Picker';
import {
  clasificarEntrega,
  construirFilasDeRq,
} from '@/features/almacen/components/nueva-salida-helpers';
import {
  articuloIdsUnicos,
  contarLineasSinCobertura,
  indicesParaAutoAsignarConCobertura,
  opcionesHelper,
  type BinConSaldo,
  type SaldosPorArticulo,
} from '@/features/almacen/components/nueva-salida-ubicacion-helpers';
import { saldosPorUbicacionQueryOptions } from '@/features/almacen/api/useSaldosCierreReportes';
import { UbicacionBinSelector } from '@/components/erp/selectors/UbicacionBinSelector';
import { useRequisicion } from '@/features/compras/api/useRequisicion';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;NuevaSalidaSheet/&gt;</c> — slide-from-right para registrar
 * una salida. Soporta las dos variantes del 00-levantamiento §6:
 * <list type="bullet">
 *   <item><b>A — Con RQ</b>: salida normal contra una RQ aprobada.
 *     Selector + auto-load de líneas; el almacenista marca cuáles
 *     entrega y captura la cantidad (default = pendiente de entregar).
 *     Backend ya valida que no exceda lo planeado.</item>
 *   <item><b>B — Vale urgente</b>: salida sin RQ; sube vale escaneado/
 *     firmado y se regulariza en 48h vinculando una RQ aprobada
 *     posterior (A14). Captura libre por diseño (no hay documento
 *     origen).</item>
 * </list>
 */
export interface NuevaSalidaSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Si <c>false</c>, oculta Variante A del toggle. */
  puedeRegistrarConRq: boolean;
  /** Si <c>false</c>, oculta Variante B del toggle. */
  puedeRegistrarVale: boolean;
}

type Variante = 'A' | 'B';

interface ValeAdjunto {
  blobRef: string;
  nombreArchivo: string;
  tamanoBytes: number;
  contentType: string;
}

export function NuevaSalidaSheet({
  open,
  onOpenChange,
  puedeRegistrarConRq,
  puedeRegistrarVale,
}: NuevaSalidaSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-3xl">
        <SheetHeader>
          <SheetTitle>Nueva salida</SheetTitle>
          <SheetDescription>
            Surte una requisición aprobada o registra un vale urgente.
            El costo se toma del CPP (costo promedio ponderado) del
            inventario.
          </SheetDescription>
        </SheetHeader>

        {open && (
          <NuevaSalidaFormBody
            puedeRegistrarConRq={puedeRegistrarConRq}
            puedeRegistrarVale={puedeRegistrarVale}
            onSuccess={() => onOpenChange(false)}
            onCancel={() => onOpenChange(false)}
          />
        )}
      </SheetContent>
    </Sheet>
  );
}

function NuevaSalidaFormBody({
  puedeRegistrarConRq,
  puedeRegistrarVale,
  onSuccess,
  onCancel,
}: {
  puedeRegistrarConRq: boolean;
  puedeRegistrarVale: boolean;
  onSuccess: () => void;
  onCancel: () => void;
}) {
  const navigate = useNavigate();
  const idempotencyKey = useFormIdempotencyKey();

  const varianteInicial: Variante = puedeRegistrarConRq ? 'A' : 'B';

  const [variante, setVariante] = useState<Variante>(varianteInicial);
  const [valeAdjunto, setValeAdjunto] = useState<ValeAdjunto | null>(null);

  const registrarA = useRegistrarSalidaConRq();
  const registrarB = useRegistrarSalidaPorVale();

  const hoyIso = useMemo(() => hoyLocalISO(), []);

  const resolver: Resolver<SalidaFormValues> = useMemo(() => {
    const schema =
      variante === 'A'
        ? RegistrarSalidaConRqSchema
        : RegistrarSalidaPorValeSchema;
    return zodResolver(schema) as Resolver<SalidaFormValues>;
  }, [variante]);

  const form = useForm<SalidaFormValues>({
    resolver,
    defaultValues: defaultValuesParaVariante(varianteInicial, hoyIso),
  });

  const lineasFA = useFieldArray({
    control: form.control,
    name: 'lineas',
  });

  const lookup = useDecimalesUnidad();

  function cambiarVariante(siguiente: Variante) {
    if (siguiente === variante) return;
    setVariante(siguiente);
    setValeAdjunto(null);
    form.reset(defaultValuesParaVariante(siguiente, hoyIso));
  }

  function onSubmit(values: SalidaFormValues) {
    if (variante === 'A') {
      const v = values as RegistrarSalidaConRqValues;
      // Advisory: decimales por unidad (solo filas incluidas; backend autoritativo).
      let decimalesMal = false;
      v.filas.forEach((f, i) => {
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
      const lineasParaBackend = v.filas
        .filter((f) => f.incluida)
        .map((f) => ({
          articuloId: f.articuloId,
          lineaRqId: f.lineaRqId,
          cantidad: f.cantidad,
          centroCostoId: f.centroCostoId ?? null,
          proyectoId: f.proyectoId ?? null,
          ubicacionReferencia: f.ubicacionReferencia ?? null,
          ubicacionId: f.ubicacionId ?? null,
          comentario: f.comentario ?? null,
        }));
      registrarA.mutate(
        {
          command: {
            requisicionId: v.requisicionId,
            // Salida-por-línea C2: sin subAlmacenId — el backend lo deriva del bin.
            fechaMovimiento: v.fechaMovimiento,
            personaDestinatariaId: v.personaDestinatariaId ?? null,
            observaciones: v.observaciones ?? null,
            lineas: lineasParaBackend,
          },
          idempotencyKey,
        },
        {
          onSuccess: (resp) => {
            toast.success(`Salida ${resp.folio} registrada`);
            onSuccess();
            navigate({
              to: '/almacen/salidas/$id',
              params: { id: resp.salidaId },
            });
          },
          onError: (error) => manejarError(error),
        },
      );
      return;
    }

    if (valeAdjunto == null) {
      form.setError('valeBlobRef' as keyof SalidaFormValues, {
        type: 'required',
        message: 'Sube el vale firmado antes de registrar.',
      });
      return;
    }
    const v = values as RegistrarSalidaPorValeValues;
    {
      let decimalesMal = false;
      v.lineas.forEach((l, i) => {
        const dec = lookup.porId(l.unidadMedidaId);
        if (
          dec != null &&
          l.cantidad != null &&
          Number.isFinite(l.cantidad) &&
          !cabeEnDecimales(l.cantidad, dec)
        ) {
          form.setError(
            `lineas.${i}.cantidad` as Parameters<typeof form.setError>[0],
            { type: 'decimales', message: MENSAJE_DECIMALES_UNIDAD },
          );
          decimalesMal = true;
        }
      });
      if (decimalesMal) return;
    }
    registrarB.mutate(
      {
        command: {
          // Salida-por-línea (vale): sin subAlmacenId — el backend lo deriva del bin.
          fechaMovimiento: v.fechaMovimiento,
          valeBlobRef: valeAdjunto.blobRef,
          personaDestinatariaId: v.personaDestinatariaId ?? null,
          observaciones: v.observaciones ?? null,
          lineas: v.lineas.map((l) => ({
            articuloId: l.articuloId,
            lineaRqId: null,
            cantidad: l.cantidad,
            centroCostoId: l.centroCostoId ?? null,
            proyectoId: l.proyectoId ?? null,
            ubicacionReferencia: l.ubicacionReferencia ?? null,
            ubicacionId: l.ubicacionId ?? null,
            comentario: l.comentario ?? null,
          })),
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Vale ${resp.folio} registrado`, {
            description: 'Regulariza en 48 h vinculando una RQ aprobada.',
          });
          onSuccess();
          navigate({
            to: '/almacen/salidas/$id',
            params: { id: resp.salidaId },
          });
        },
        onError: (error) => manejarError(error),
      },
    );
  }

  function manejarError(error: unknown) {
    if (esApiError(error)) {
      if (error.code === 'SALIDA_STOCK_INSUFICIENTE') {
        toast.error('Stock insuficiente para alguna línea.', {
          description:
            error.problem.detail ??
            'Revisa la cantidad solicitada vs disponible en el sub-almacén.',
        });
        return;
      }
      if (error.code === 'SALIDA_RQ_NO_AUTORIZADA') {
        form.setError('requisicionId', {
          type: error.code,
          message: 'La RQ no está autorizada o ya fue surtida.',
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
    toast.error('Error inesperado al registrar la salida.');
  }

  const isPending = registrarA.isPending || registrarB.isPending;

  return (
    <>
      <form
        id="nueva-salida-form"
        onSubmit={form.handleSubmit(onSubmit)}
        noValidate
        className="flex-1 space-y-4 overflow-y-auto px-6"
      >
        <fieldset className="space-y-2">
          <legend className="text-sm font-medium">Tipo de salida</legend>
          <div className="flex gap-2">
            {puedeRegistrarConRq && (
              <VarianteOption
                value="A"
                active={variante === 'A'}
                onSelect={cambiarVariante}
                titulo="Con RQ"
                descripcion="Salida normal contra requisición aprobada."
              />
            )}
            {puedeRegistrarVale && (
              <VarianteOption
                value="B"
                active={variante === 'B'}
                onSelect={cambiarVariante}
                titulo="Vale urgente"
                descripcion="Sin RQ; adjunta vale firmado. Regulariza en 48 h."
              />
            )}
          </div>
        </fieldset>

        <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
          {variante === 'A' && (
            <Field
              label="Requisición"
              required
              error={form.formState.errors.requisicionId?.message}
            >
              <Controller
                name="requisicionId"
                control={form.control}
                render={({ field }) => (
                  <RequisicionSelector
                    value={field.value || null}
                    onChange={(id) => field.onChange(id ?? '')}
                  />
                )}
              />
            </Field>
          )}

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

          <Field
            label="Persona destinataria (opcional)"
            error={form.formState.errors.personaDestinatariaId?.message}
          >
            <Controller
              name="personaDestinatariaId"
              control={form.control}
              render={({ field }) => (
                <UsuarioSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? null)}
                />
              )}
            />
          </Field>

        </div>

        {variante === 'B' && (
          <Field
            label="Vale firmado"
            required
            error={form.formState.errors.valeBlobRef?.message}
          >
            <ValeUpload
              value={valeAdjunto}
              onChange={(v) => {
                setValeAdjunto(v);
                // El schema valida valeBlobRef en el resolver, así que el
                // blobRef debe entrar al form al subir el archivo — no solo
                // en onSubmit (ahí el resolver ya rechazó). Mismo defecto
                // que el packing list de recepción Variante B.
                form.setValue(
                  'valeBlobRef' as keyof SalidaFormValues,
                  (v?.blobRef ?? '') as never,
                );
                if (v) form.clearErrors('valeBlobRef');
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

        {variante === 'A' ? (
          <FilasDesdeRq form={form} />
        ) : (
          <LineasCapturaLibre form={form} lineasFA={lineasFA} />
        )}
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
          form="nueva-salida-form"
          disabled={isPending}
        >
          {isPending ? 'Registrando…' : 'Registrar salida'}
        </Button>
      </SheetFooter>
    </>
  );
}

// ─── Variante A: filas desde RQ ─────────────────────────────────────────────

function FilasDesdeRq({
  form,
}: {
  form: ReturnType<typeof useForm<SalidaFormValues>>;
}) {
  const requisicionId = useWatch({
    control: form.control,
    name: 'requisicionId',
  });
  const rqQuery = useRequisicion(requisicionId || null);
  const rqId = requisicionId || null;
  const ultimaRqCargada = useRef<string | null>(null);

  useEffect(() => {
    if (!rqId) {
      if (ultimaRqCargada.current !== null) {
        ultimaRqCargada.current = null;
        form.setValue('filas', []);
      }
      return;
    }
    if (rqQuery.data == null) return;
    if (ultimaRqCargada.current === rqId) return;
    ultimaRqCargada.current = rqId;
    form.setValue('filas', construirFilasDeRq(rqQuery.data.lineas));
  }, [rqId, rqQuery.data, form]);

  const filas = useWatch({ control: form.control, name: 'filas' }) ?? [];
  const ubicacionHelperId = useWatch({
    control: form.control,
    name: 'ubicacionHelperId',
  });

  // PR5: saldos por bin de CADA artículo del sheet. Se piden con las mismas
  // queryOptions que usa el selector de cada fila (modo saldo), así que
  // TanStack las deduplica por queryKey: cero llamadas de red extra.
  // La lista de artículos va ordenada y sin repetir para que el arreglo de
  // queries sea estable entre renders.
  const articulos = articuloIdsUnicos(filas);
  const saldosQueries = useQueries({
    // Salida-por-línea C2: sin sub de cabecera → se listan los bins con
    // existencia de cada artículo en TODOS los subs (sub null).
    queries: articulos.map((articuloId) =>
      saldosPorUbicacionQueryOptions(articuloId, null),
    ),
  });

  const saldosPorArticulo: SaldosPorArticulo = new Map(
    articulos.map((articuloId, i) => [
      articuloId,
      (saldosQueries[i]?.data ?? []).map<BinConSaldo>((s) => ({
        ubicacionId: s.ubicacionId,
        clave: s.clave,
        nombre: s.nombre,
        cantidad: s.cantidad,
      })),
    ]),
  );

  const cargandoSaldos = saldosQueries.some((q) => q.isLoading);
  const opciones = opcionesHelper(filas, saldosPorArticulo);
  const sinCobertura = contarLineasSinCobertura(
    filas,
    ubicacionHelperId,
    saldosPorArticulo,
  );
  const claveHelper = opciones.find(
    (o) => o.ubicacionId === ubicacionHelperId,
  )?.clave;

  /**
   * Aplica el bin del helper a las filas que aún no tienen ubicación Y cuyo
   * artículo tiene existencia ahí. Las que no cubre quedan intactas — el
   * aviso de abajo las cuenta para que el almacenista las resuelva.
   */
  function aplicarHelper(valor: string | null) {
    form.setValue('ubicacionHelperId', valor, { shouldDirty: true });
    if (!valor) return;
    for (const index of indicesParaAutoAsignarConCobertura(
      filas,
      valor,
      saldosPorArticulo,
    )) {
      form.setValue(`filas.${index}.ubicacionId`, valor, {
        shouldDirty: true,
        shouldValidate: true,
      });
    }
  }

  if (!rqId) {
    return (
      <section className="rounded-md border border-dashed bg-muted/30 p-4 text-sm text-muted-foreground">
        Selecciona una requisición para cargar las líneas pendientes de
        entregar.
      </section>
    );
  }

  if (rqQuery.isLoading) {
    return (
      <section className="rounded-md border border-dashed bg-muted/30 p-4 text-sm text-muted-foreground">
        Cargando líneas de la RQ…
      </section>
    );
  }

  if (rqQuery.isError) {
    return (
      <section
        role="alert"
        className="rounded-md border border-rose-300 bg-rose-50 p-4 text-sm text-rose-700"
      >
        No se pudo cargar la RQ. Verifica el id o vuelve a intentar.
      </section>
    );
  }

  if (filas.length === 0) {
    return (
      <section className="rounded-md border border-dashed bg-muted/30 p-4 text-sm text-muted-foreground">
        Esta RQ no tiene líneas.
      </section>
    );
  }

  const errorArrayLevel = form.formState.errors.filas;
  const errorMessage =
    errorArrayLevel && !Array.isArray(errorArrayLevel)
      ? errorArrayLevel.message
      : undefined;

  // ADR-0043 #3: ya no filtramos líneas sin pendiente — se muestran todas
  // (la RQ vive en EnSurtido durante entregas en parcialidades). El conteo
  // del header refleja cuántas siguen con pendiente; si ninguna lo tiene,
  // un aviso informativo lo señala (las líneas quedan visibles como registro).
  const pendientes = filas.filter((f) => f.pendienteEntregar > 0).length;

  return (
    <section className="space-y-2">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold">
          Líneas de la RQ ({pendientes} con pendiente de {filas.length})
        </h3>
      </div>

      {pendientes === 0 && (
        <p className="rounded-md border border-dashed bg-muted/30 p-3 text-xs text-muted-foreground">
          Todas las líneas de esta RQ ya fueron entregadas por completo al
          solicitante. Quedan visibles como registro; no hay nada pendiente
          de entregar.
        </p>
      )}

      {errorMessage && (
        <p role="alert" className="text-xs text-rose-600">
          {errorMessage}
        </p>
      )}

      {/* PR5: helper de ubicación de cabecera. A diferencia del de recepción,
          aquí el bin no se elige libremente: sólo se ofrecen bins CON
          existencia, etiquetados con a cuántos artículos del sheet cubren, y
          al aplicarlo sólo toca las filas que puede surtir. Un auto-apply a
          ciegas mandaría líneas a bins vacíos y el trigger abortaría la
          transacción con SALDO_INEXISTENTE. */}
      <div className="rounded-md border border-dashed bg-muted/20 p-3">
        <label
          className="mb-1 block text-xs font-medium text-muted-foreground"
          htmlFor="salida-ubicacion-helper"
        >
          Ubicación por defecto (opcional)
        </label>
        <Select
          value={ubicacionHelperId || ''}
          onValueChange={(v) => aplicarHelper(v || null)}
          disabled={cargandoSaldos || opciones.length === 0}
        >
          <SelectTrigger id="salida-ubicacion-helper">
            <SelectValue
              placeholder={
                cargandoSaldos
                  ? 'Cargando existencias…'
                  : opciones.length === 0
                    ? 'Sin existencias para estos artículos'
                    : 'Sin ubicación por defecto'
              }
            />
          </SelectTrigger>
          <SelectContent>
            {opciones.map((o) => (
              <SelectItem key={o.ubicacionId} value={o.ubicacionId}>
                {o.etiqueta}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <p className="mt-1 text-xs text-muted-foreground">
          Se aplica sólo a las líneas sin ubicación cuyo artículo tenga
          existencia ahí. Nunca reemplaza un rack ya capturado.
        </p>
        {sinCobertura > 0 && (
          <p role="alert" className="mt-2 text-xs text-amber-700">
            {sinCobertura}{' '}
            {sinCobertura === 1 ? 'línea sin existencia' : 'líneas sin existencia'}{' '}
            en {claveHelper ?? 'la ubicación elegida'} — elígeles rack
            individualmente.
          </p>
        )}
      </div>

      <div className="space-y-2">
        {filas.map((fila, index) => (
          <FilaLineaRq
            key={fila.lineaRqId}
            index={index}
            fila={fila}
            ubicacionHelperId={ubicacionHelperId ?? null}
            control={form.control}
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
  centroCostoId?: { message?: string };
  proyectoId?: { message?: string };
  ubicacionReferencia?: { message?: string };
  ubicacionId?: { message?: string };
  comentario?: { message?: string };
}

function FilaLineaRq({
  index,
  fila,
  ubicacionHelperId,
  control,
  errors,
}: {
  index: number;
  fila: FilaSalidaValues;
  /** PR5: bin de cabecera vigente, para marcar las filas que coinciden. */
  ubicacionHelperId: string | null;
  control: ReturnType<typeof useForm<SalidaFormValues>>['control'];
  errors: FilaErrors | undefined;
}) {
  const incluida = useWatch({
    control,
    name: `filas.${index}.incluida` as const,
  });
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
              aria-label={`Incluir línea ${fila.posicion} en la salida`}
            />
          )}
        />

        <div className="flex-1 space-y-2">
          <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1 text-sm">
            <span className="font-semibold">Línea #{fila.posicion}</span>
            <BadgeEntrega fila={fila} />
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
            <span className="text-xs text-muted-foreground">
              UM: {fila.unidadMedida}
            </span>
          </div>

          <dl className="grid grid-cols-4 gap-2 text-xs">
            <Stat label="Solicitada">
              {formatCantidad(fila.cantidadSolicitada)}
            </Stat>
            <Stat label="Planeado almacén">
              {formatCantidad(fila.cantidadPlaneadaAlmacen)}
            </Stat>
            <Stat label="Ya entregado">
              {formatCantidad(fila.cantidadYaEntregada)}
            </Stat>
            <Stat label="Pendiente entregar" highlight>
              {formatCantidad(fila.pendienteEntregar)}
            </Stat>
          </dl>

          {incluida && (
            <div className="grid grid-cols-1 gap-2 md:grid-cols-2">
              <Field
                label="Cantidad a entregar"
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
                      max={fila.pendienteEntregar}
                      value={field.value ?? ''}
                      onChange={(e) =>
                        field.onChange(
                          e.target.value === '' ? 0 : Number(e.target.value),
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
                  Máximo: {formatCantidad(fila.pendienteEntregar)}
                </p>
                <AvisoUnidadNoResoluble visible={unidadNoResoluble} />
              </Field>

              <Field label="CC-Máquina">
                {/* Fase E PR5 Camino 1: el CC-Máquina se HEREDA de la línea de
                    RQ y queda BLOQUEADO (read-only). Mismo molde que la UM/CC
                    heredada de OC (LineaInlineFormOc). El backend lo re-deriva
                    autoritativo (ignora lo que mande el FE). "—" si la RQ no
                    traía CC. */}
                <Input
                  type="text"
                  readOnly
                  tabIndex={-1}
                  aria-label="CC-Máquina (heredado de la requisición, no editable)"
                  className="bg-muted/50"
                  value={
                    fila.centroCostoId
                      ? formatCcMaquinaLabel({
                          clave: fila.centroCostoClave,
                          nombre: fila.centroCostoNombre,
                        })
                      : '—'
                  }
                />
              </Field>

              <Field
                label={
                  <span className="inline-flex items-center gap-1.5">
                    Ubicación (rack) de salida
                    {!!ubicacionHelperId &&
                      fila.ubicacionId === ubicacionHelperId && (
                        <span className="rounded bg-emerald-100 px-1.5 py-0.5 text-[10px] font-medium text-emerald-700">
                          coincide
                        </span>
                      )}
                  </span>
                }
                error={errors?.ubicacionId?.message}
              >
                <Controller
                  name={`filas.${index}.ubicacionId` as const}
                  control={control}
                  render={({ field }) => (
                    <UbicacionBinSelector
                      modo="saldo"
                      articuloId={fila.articuloId}
                      // Salida-por-línea C2: sin sub de cabecera; el bin se elige
                      // entre TODOS los subs con existencia (el sub se deriva).
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

// ─── Variante B: líneas captura libre ───────────────────────────────────────

function LineasCapturaLibre({
  form,
  lineasFA,
}: {
  form: ReturnType<typeof useForm<SalidaFormValues>>;
  lineasFA: ReturnType<typeof useFieldArray<SalidaFormValues, 'lineas'>>;
}) {
  const lineas = useWatch({ control: form.control, name: 'lineas' }) ?? [];
  const ubicacionHelperId = useWatch({
    control: form.control,
    name: 'ubicacionHelperId',
  });

  // Salida-por-línea (vale): mismo helper "Ubicación por defecto" que la
  // variante A. Saldos por bin de cada artículo (sub null → todos los subs),
  // deduplicados por queryKey con el selector de cada línea.
  const articulos = articuloIdsUnicos(lineas);
  const saldosQueries = useQueries({
    queries: articulos.map((articuloId) =>
      saldosPorUbicacionQueryOptions(articuloId, null),
    ),
  });

  const saldosPorArticulo: SaldosPorArticulo = new Map(
    articulos.map((articuloId, i) => [
      articuloId,
      (saldosQueries[i]?.data ?? []).map<BinConSaldo>((s) => ({
        ubicacionId: s.ubicacionId,
        clave: s.clave,
        nombre: s.nombre,
        cantidad: s.cantidad,
      })),
    ]),
  );

  const cargandoSaldos = saldosQueries.some((q) => q.isLoading);
  const opciones = opcionesHelper(lineas, saldosPorArticulo);
  const sinCobertura = contarLineasSinCobertura(
    lineas,
    ubicacionHelperId,
    saldosPorArticulo,
  );
  const claveHelper = opciones.find(
    (o) => o.ubicacionId === ubicacionHelperId,
  )?.clave;

  /**
   * Aplica el bin del helper a las líneas que aún no tienen ubicación Y cuyo
   * artículo tiene existencia ahí. Mismo comportamiento que la variante A.
   */
  function aplicarHelper(valor: string | null) {
    form.setValue('ubicacionHelperId', valor, { shouldDirty: true });
    if (!valor) return;
    for (const index of indicesParaAutoAsignarConCobertura(
      lineas,
      valor,
      saldosPorArticulo,
    )) {
      form.setValue(`lineas.${index}.ubicacionId`, valor, {
        shouldDirty: true,
        shouldValidate: true,
      });
    }
  }

  return (
    <section className="space-y-2">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold">
          Líneas ({lineasFA.fields.length})
        </h3>
        <Button
          type="button"
          size="sm"
          variant="outline"
          onClick={() =>
            lineasFA.append({
              articuloId: '',
              unidadMedidaId: null,
              lineaRqId: null,
              cantidad: 1,
              centroCostoId: null,
              proyectoId: null,
              ubicacionReferencia: null,
              ubicacionId: null,
              comentario: null,
            })
          }
        >
          <Plus className="mr-2 h-4 w-4" />
          Agregar línea
        </Button>
      </div>

      {form.formState.errors.lineas &&
        !Array.isArray(form.formState.errors.lineas) && (
          <p role="alert" className="text-xs text-rose-600">
            {form.formState.errors.lineas.message}
          </p>
        )}

      {/* Helper "Ubicación por defecto": mismo andamiaje que la variante A —
          sólo bins CON existencia; al aplicarlo toca las líneas sin ubicación
          cuyo artículo tenga existencia ahí. Nunca pisa un bin ya capturado. */}
      <div className="rounded-md border border-dashed bg-muted/20 p-3">
        <label
          className="mb-1 block text-xs font-medium text-muted-foreground"
          htmlFor="vale-ubicacion-helper"
        >
          Ubicación por defecto (opcional)
        </label>
        <Select
          value={ubicacionHelperId || ''}
          onValueChange={(v) => aplicarHelper(v || null)}
          disabled={cargandoSaldos || opciones.length === 0}
        >
          <SelectTrigger id="vale-ubicacion-helper">
            <SelectValue
              placeholder={
                cargandoSaldos
                  ? 'Cargando existencias…'
                  : opciones.length === 0
                    ? 'Sin existencias para estos artículos'
                    : 'Sin ubicación por defecto'
              }
            />
          </SelectTrigger>
          <SelectContent>
            {opciones.map((o) => (
              <SelectItem key={o.ubicacionId} value={o.ubicacionId}>
                {o.etiqueta}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <p className="mt-1 text-xs text-muted-foreground">
          Se aplica sólo a las líneas sin ubicación cuyo artículo tenga
          existencia ahí. Nunca reemplaza un rack ya capturado.
        </p>
        {sinCobertura > 0 && (
          <p role="alert" className="mt-2 text-xs text-amber-700">
            {sinCobertura}{' '}
            {sinCobertura === 1 ? 'línea sin existencia' : 'líneas sin existencia'}{' '}
            en {claveHelper ?? 'la ubicación elegida'} — elígeles rack
            individualmente.
          </p>
        )}
      </div>

      <div className="space-y-2">
        {lineasFA.fields.map((field, index) => (
          <LineaLibreForm
            key={field.id}
            index={index}
            control={form.control}
            errors={
              Array.isArray(form.formState.errors.lineas)
                ? (form.formState.errors.lineas[index] as
                    | LineaLibreErrors
                    | undefined)
                : undefined
            }
            onRemove={() => lineasFA.remove(index)}
            onArticuloSelect={(articulo) =>
              form.setValue(
                `lineas.${index}.unidadMedidaId` as Parameters<
                  typeof form.setValue
                >[0],
                articulo.unidadMedidaId,
              )
            }
          />
        ))}
      </div>
    </section>
  );
}

interface LineaLibreErrors {
  articuloId?: { message?: string };
  cantidad?: { message?: string };
  centroCostoId?: { message?: string };
  proyectoId?: { message?: string };
  ubicacionReferencia?: { message?: string };
  ubicacionId?: { message?: string };
  comentario?: { message?: string };
}

function LineaLibreForm({
  index,
  control,
  errors,
  onRemove,
  onArticuloSelect,
}: {
  index: number;
  control: ReturnType<typeof useForm<SalidaFormValues>>['control'];
  errors: LineaLibreErrors | undefined;
  onRemove: () => void;
  onArticuloSelect: (articulo: ArticuloListItem) => void;
}) {
  const lookupLinea = useDecimalesUnidad();
  const umId = useWatch({
    control,
    name: `lineas.${index}.unidadMedidaId` as const,
  });
  const articuloId = useWatch({
    control,
    name: `lineas.${index}.articuloId` as const,
  });
  const decimalesLinea = lookupLinea.porId(umId) ?? DECIMALES_FALLBACK;
  return (
    <div className="rounded-md border border-dashed border-primary/40 bg-primary/5 p-3 space-y-2">
      <div className="flex items-start justify-between gap-2">
        <span className="text-xs font-semibold text-muted-foreground">
          Línea #{index + 1}
        </span>
        <Button
          type="button"
          size="sm"
          variant="ghost"
          onClick={onRemove}
          aria-label={`Quitar línea ${index + 1}`}
        >
          <Trash2 className="h-4 w-4" />
        </Button>
      </div>

      <div className="grid grid-cols-1 gap-2 md:grid-cols-2">
        <Field label="Artículo" required error={errors?.articuloId?.message}>
          <Controller
            name={`lineas.${index}.articuloId` as const}
            control={control}
            render={({ field }) => (
              <ArticuloSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
                onSelect={onArticuloSelect}
              />
            )}
          />
        </Field>

        <Field label="Cantidad" required error={errors?.cantidad?.message}>
          <Controller
            name={`lineas.${index}.cantidad` as const}
            control={control}
            render={({ field }) => (
              <Input
                type="number"
                inputMode="decimal"
                step={stepParaDecimales(decimalesLinea)}
                min="0"
                value={field.value ?? ''}
                onChange={(e) =>
                  field.onChange(
                    e.target.value === '' ? '' : Number(e.target.value),
                  )
                }
              />
            )}
          />
        </Field>

        <Field
          label="CC-Máquina"
          error={errors?.centroCostoId?.message}
        >
          {/* Fase E PR5 Camino 2 (vale): el almacenista elige el CC-Máquina con
              el picker ABIERTO por proxy — todas las Dim3 activas del sistema,
              sin filtro de alcance. El endpoint gateado por almacen.salidas.por-vale
              abre el selector (el backend, no el FE). Molde de la línea manual de OC. */}
          <Controller
            name={`lineas.${index}.centroCostoId` as const}
            control={control}
            render={({ field }) => (
              <Dim3Picker
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? null)}
                endpoint="/api/v1/almacen/salidas/dim3/buscar"
              />
            )}
          />
        </Field>

        <Field
          label="Proyecto (opcional)"
          error={errors?.proyectoId?.message}
        >
          <Controller
            name={`lineas.${index}.proyectoId` as const}
            control={control}
            render={({ field }) => (
              <Input
                {...field}
                value={field.value ?? ''}
                placeholder="GUID"
                spellCheck={false}
              />
            )}
          />
        </Field>

        <Field
          label="Ubicación (rack) de salida"
          required
          error={errors?.ubicacionId?.message}
        >
          <Controller
            name={`lineas.${index}.ubicacionId` as const}
            control={control}
            render={({ field }) => (
              <UbicacionBinSelector
                modo="saldo"
                articuloId={articuloId}
                // Salida-por-línea (vale): sin sub de cabecera; bin entre TODOS
                // los subs con existencia (el sub se deriva).
                subAlmacenId={null}
                requiereSubAlmacen={false}
                value={field.value ?? null}
                onChange={(id) => field.onChange(id ?? '')}
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
              name={`lineas.${index}.comentario` as const}
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
    </div>
  );
}

// ─── Helpers ───────────────────────────────────────────────────────────────

function BadgeEntrega({ fila }: { fila: FilaSalidaValues }) {
  const estado = clasificarEntrega(
    fila.cantidadYaEntregada,
    fila.cantidadSolicitada,
  );
  if (estado === 'sin-entregar') return null;
  const esParcial = estado === 'parcial';
  return (
    <span
      className={cn(
        'rounded px-1.5 py-0.5 text-[10px] font-medium',
        esParcial
          ? 'bg-amber-100 text-amber-800'
          : 'bg-emerald-100 text-emerald-800',
      )}
    >
      {esParcial
        ? `Parcial · ${formatCantidad(fila.cantidadYaEntregada)}/${formatCantidad(
            fila.cantidadSolicitada,
          )}`
        : 'Entregada'}
    </span>
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
        className={cn('font-mono', highlight && 'font-semibold text-primary')}
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
  // PR5: ReactNode y no string — el label de la ubicación lleva el badge
  // "coincide" cuando la fila ya apunta al bin del helper.
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

// ─── Form values type + helpers ────────────────────────────────────────────

type SalidaFormValues = {
  requisicionId?: string;
  fechaMovimiento: string;
  personaDestinatariaId?: string | null;
  observaciones?: string | null;
  valeBlobRef?: string;
  // PR5 (variante A): bin de cabecera que auto-aplica a las filas vacías con
  // cobertura. Sólo estado del form — no viaja al backend.
  ubicacionHelperId?: string | null;
  // Variante A: filas pre-pobladas desde la RQ.
  filas?: FilaSalidaValues[];
  // Variante B: líneas captura libre.
  lineas?: {
    articuloId: string;
    unidadMedidaId?: string | null;
    lineaRqId?: string | null;
    cantidad: number;
    centroCostoId?: string | null;
    proyectoId?: string | null;
    ubicacionReferencia?: string | null;
    ubicacionId?: string | null;
    comentario?: string | null;
  }[];
};

function defaultValuesParaVariante(
  variante: Variante,
  hoyIso: string,
): SalidaFormValues {
  const base: SalidaFormValues = {
    fechaMovimiento: hoyIso,
    personaDestinatariaId: null,
    observaciones: null,
  };
  if (variante === 'A') {
    return { ...base, requisicionId: '', ubicacionHelperId: null, filas: [] };
  }
  return {
    ...base,
    valeBlobRef: '',
    ubicacionHelperId: null,
    lineas: [
      {
        articuloId: '',
        unidadMedidaId: null,
        lineaRqId: null,
        cantidad: 1,
        centroCostoId: null,
        proyectoId: null,
        ubicacionReferencia: null,
        ubicacionId: null,
        comentario: null,
      },
    ],
  };
}

