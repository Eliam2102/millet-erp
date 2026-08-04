import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Plus } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Input } from '@/components/ui/input';
import {
  ProveedorSelector,
  SucursalSelector,
  CondicionesPagoSelector,
  UsoPrincipalSelector,
  DatePickerField,
  TextAreaField,
  DecimalField,
} from '@/components/erp';
import {
  useSucursales,
  mapById,
} from '@/features/catalogos/api';
import {
  CrearOrdenCompraVaciaSchema,
  DEFAULT_CREAR_OC_VACIA,
  type CrearOrdenCompraVaciaValues,
} from '@/features/compras/ordenes/schemas/crear-oc-vacia';
import { useCrearOrdenCompraVacia } from '@/features/compras/ordenes/api/useCrearOrdenCompraVacia';
import { useAgregarLineaDesdeRequisicion } from '@/features/compras/ordenes/api/useAgregarLineaDesdeRequisicion';
import { SelectorRequisicionesConsolidacion } from '@/features/compras/ordenes/components/SelectorRequisicionesConsolidacion';
import {
  useRequisicionesDisponibles,
  type RequisicionDisponible,
} from '@/features/compras/ordenes/api/useRequisicionesDisponibles';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import { useUnsavedChangesGuard } from '@/lib/hooks/useUnsavedChangesGuard';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { useAutoGenerarOcAlAutorizar } from '@/lib/auth/useComprasSettings';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { DesdeRequisicionContext } from '@/features/compras/ordenes/components/nueva-orden-compra-context';
import {
  buildNuevaOrdenCompraDraftKey,
  clearDraft,
  useDraftPersist,
  useDraftRecovery,
} from '@/features/compras/lib/draft-storage';
import { HistorialComprasProveedor } from '@/features/compras/ordenes/components/HistorialComprasProveedor';
import { UploadCorreoAutorizacion } from '@/features/compras/ordenes/components/UploadCorreoAutorizacion';

/**
 * <c>P4 — Sheet "Nueva OC"</c> con detección de 3 modos (FOC3,
 * doc 05 §10.4):
 *
 * <list>
 *   <item><b>Vacía</b> (default): sin RQ previa, sin consolidación.
 *   El comprador captura cabecera + más adelante agrega líneas
 *   manuales en el editor del detalle (UF2-PR3).</item>
 *   <item><b>Consolidación N:1</b>: el comprador agrega varias RQs
 *   autorizadas al payload. UF2-PR1 muestra el botón "Agregar
 *   requisiciones" como stub — el selector multi-select real es
 *   <c>&lt;SelectorRequisicionesConsolidacion/&gt;</c> (UF2-PR2,
 *   restricción de sucursal).</item>
 *   <item><b>Sin RQ previa</b> (FOC11): toggle gateado por
 *   <c>compras.ordenes.crear-sin-rq</c>; al activar exige motivo y
 *   muestra el slot para adjuntar el correo de autorización
 *   (<c>&lt;UploadCorreoAutorizacion/&gt;</c> stub UF3-PR2).</item>
 * </list>
 *
 * <para><b>Modo "1:1 desde RQ"</b>: el doc 07 lo describe entrando
 * desde la bandeja de RQ con un botón "Convertir". Ese botón vive
 * en RQ (out of scope OC); cuando se construya, navegará al Sheet
 * con un <c>requisicionId</c> en query params y el Sheet lo detectará
 * para pre-llenar la cabecera y submitear vía
 * <c>useCrearOrdenCompraDesdeRequisicion</c>. UF2-PR1 deja el hook
 * listo pero NO añade el entry point.</para>
 */
export interface SheetNuevaOCProps {
  /** Callback al cerrar (botón Cancelar / X / Esc). El provider
   * shell-level lo pasa para coordinar el cierre del overlay.
   * <c>force: true</c> en el flujo de éxito post-submit salta el
   * confirm de "tienes cambios sin guardar". */
  onClose?: (opts?: { force?: boolean }) => void;
  /** Reporta al wrapper si el form tiene cambios sin guardar. */
  onDirtyChange?: (dirty: boolean) => void;
  /**
   * Cuando se provee, el Sheet entra en modo "Convertir 1:1 desde RQ":
   * pre-llena sucursal con la de la RQ, pre-selecciona la RQ una vez
   * que <c>useRequisicionesDisponibles</c> la lista, oculta el botón
   * "Agregar requisiciones (consolidación)". Ver
   * <c>DesdeRequisicionContext</c>.
   */
  desdeRequisicion?: DesdeRequisicionContext | null;
}

const MONEDAS_DISPONIBLES: ReadonlyArray<{ codigo: string; nombre: string }> =
  [
    { codigo: 'MXN', nombre: 'Peso Mexicano (MXN)' },
    { codigo: 'USD', nombre: 'Dólar EUA (USD)' },
    { codigo: 'EUR', nombre: 'Euro (EUR)' },
    { codigo: 'CAD', nombre: 'Dólar Canadiense (CAD)' },
    { codigo: 'GBP', nombre: 'Libra Esterlina (GBP)' },
  ];

export function SheetNuevaOC({
  onClose,
  onDirtyChange,
  desdeRequisicion = null,
}: SheetNuevaOCProps = {}) {
  const navigate = useNavigate();
  const userId = useAuthStore((s) => s.user?.id);
  const empresaId = useAuthStore((s) => s.currentEmpresaId);
  const canCrearSinRq = useHasPermission(
    PermisosCanonicos.ComprasOrdenesCrearSinRq,
  );

  const autoGenerarOcAlAutorizar = useAutoGenerarOcAlAutorizar();
  // Modo 1:1 cuando el sheet se abrió desde el detalle RQ con
  // "Convertir a OC". Toggleable solo al montar — el caller decide.
  const modoUnoAUno = desdeRequisicion != null;

  const idempotencyKey = useFormIdempotencyKey();
  const draftKey = useMemo(
    () => buildNuevaOrdenCompraDraftKey(userId, empresaId),
    [userId, empresaId],
  );

  const recovery = useDraftRecovery<CrearOrdenCompraVaciaValues>(draftKey);
  const [showRecoverModal, setShowRecoverModal] = useState(
    recovery.draft != null,
  );

  // Modo consolidación: estado de RQs seleccionadas + visibilidad
  // del modal selector. Las RQs viven SOLO en estado del Sheet, NO en
  // el form (no son campos persistibles del comando, son metadatos
  // del flujo). Si la sucursal cambia, las RQs se limpian para no
  // violar §10.5.
  const [selectorAbierto, setSelectorAbierto] = useState(false);
  const [rqsSeleccionadas, setRqsSeleccionadas] = useState<
    readonly RequisicionDisponible[]
  >([]);

  const form = useForm<CrearOrdenCompraVaciaValues>({
    resolver: zodResolver(CrearOrdenCompraVaciaSchema),
    defaultValues: DEFAULT_CREAR_OC_VACIA,
    mode: 'onBlur',
  });

  // Reportar dirty al provider para el confirm-on-close.
  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange?.(isDirty);
  }, [isDirty, onDirtyChange]);

  // beforeunload nativo cuando hay cambios.
  useUnsavedChangesGuard(isDirty);

  // Persistir el draft con debounce.
  const watchedValues = useWatch({ control: form.control });
  useDraftPersist(draftKey, watchedValues, isDirty);

  // Catálogos.
  const sucursalesQuery = useSucursales();
  const sucursalesMap = useMemo(
    () => mapById(sucursalesQuery.data?.items),
    [sucursalesQuery.data],
  );

  // Watch fields via useWatch (estable, suscripción granular). El
  // form.watch() de RHF dispara re-renders extra y trigger el lint
  // react-hooks/incompatible-library.
  const proveedorId = useWatch({ control: form.control, name: 'proveedorId' });
  const sucursalDestinoId = useWatch({
    control: form.control,
    name: 'sucursalDestinoId',
  });
  const moneda = useWatch({ control: form.control, name: 'moneda' });
  const sinRequisicionPrevia = useWatch({
    control: form.control,
    name: 'sinRequisicionPrevia',
  });

  // Resetea almacén Y limpia RQs seleccionadas cuando cambia sucursal.
  // Almacén: FK lógica al sucursal. RQs: §10.5 — no se pueden mezclar
  // sucursales en una OC consolidada.
  //
  // Pattern derived state (sin useEffect): evita
  // <c>react-hooks/set-state-in-effect</c>. Trackeamos el último
  // sucursalDestinoId visto y, si cambió, hacemos las limpiezas en
  // el render (idempotentes — no son side-effects externos sino
  // sincronización entre dos pieces de state React).
  const [trackedSucursal, setTrackedSucursal] = useState<string | undefined>(
    sucursalDestinoId,
  );
  if (sucursalDestinoId !== trackedSucursal) {
    setTrackedSucursal(sucursalDestinoId);
    // En modo 1:1 NO limpiamos rqsSeleccionadas porque la sucursal
    // cambió por la pre-carga desde la RQ origen. Las RQs siguen
    // siendo válidas (misma sucursal).
    if (!modoUnoAUno) {
      setRqsSeleccionadas([]);
    }
  }

  // Modo 1:1 (PR-B 2026-05-13): si la apertura del Sheet vino con
  // desdeRequisicion, pre-llenamos el campo de sucursal y dejamos que
  // useRequisicionesDisponibles localice la RQ. Cuando aparece, la
  // marcamos como seleccionada — equivale a que el usuario la haya
  // elegido en el modal selector.
  //
  // Mismo pattern de derived state durante render que el bloque de
  // trackedSucursal arriba: trackeamos el último requisicionId aplicado
  // y, si cambió, hacemos las setStates en render (idempotentes).
  const [trackedDesdeRqId, setTrackedDesdeRqId] = useState<string | null>(null);
  if (
    desdeRequisicion != null &&
    desdeRequisicion.requisicionId !== trackedDesdeRqId
  ) {
    setTrackedDesdeRqId(desdeRequisicion.requisicionId);
    form.setValue('sucursalDestinoId', desdeRequisicion.sucursalId, {
      shouldDirty: false,
    });
  }

  const requisicionesDisponiblesQuery = useRequisicionesDisponibles(
    modoUnoAUno ? desdeRequisicion?.sucursalId ?? null : null,
  );
  // Pre-selecciona la RQ una vez que aparece en useRequisicionesDisponibles.
  // Pattern derived state: trackeamos si ya se pre-seleccionó esta RQ
  // específica para evitar loop.
  const [preselectedRqId, setPreselectedRqId] = useState<string | null>(null);
  if (
    modoUnoAUno &&
    desdeRequisicion != null &&
    preselectedRqId !== desdeRequisicion.requisicionId &&
    requisicionesDisponiblesQuery.data != null
  ) {
    const match = requisicionesDisponiblesQuery.data.find(
      (r) => r.id === desdeRequisicion.requisicionId,
    );
    if (match) {
      setPreselectedRqId(desdeRequisicion.requisicionId);
      setRqsSeleccionadas([match]);
    }
  }

  const crearMut = useCrearOrdenCompraVacia();
  const agregarLineaMut = useAgregarLineaDesdeRequisicion();

  function handleRecuperar() {
    if (recovery.draft) {
      form.reset(recovery.draft.values, {
        keepDirty: true,
        keepDirtyValues: false,
      });
    }
    recovery.acknowledge();
    setShowRecoverModal(false);
  }

  function handleDescartarDraft() {
    recovery.discard();
    setShowRecoverModal(false);
  }

  async function onSubmit(values: CrearOrdenCompraVaciaValues) {
    // Lookup del SucursalCodigo (clave) requerido por el backend para
    // construir el folio. Si la sucursal no está en cache, fallback al
    // id raw — el backend rechazará con 422 SUCURSAL_CODIGO_INVALIDO.
    const sucursal = sucursalesMap.get(values.sucursalDestinoId);
    const sucursalCodigo = sucursal?.clave ?? '';
    if (!sucursalCodigo) {
      toast.error('No se pudo resolver el código de sucursal.', {
        description:
          'Recarga la página o contacta a soporte si persiste.',
      });
      return;
    }

    try {
      const response = await crearMut.mutateAsync({
        command: {
          ...values,
          sucursalCodigo,
          folioAnio: new Date().getFullYear(),
        },
        idempotencyKey,
      });

      // Modo Consolidación: si hay RQs seleccionadas, después de
      // crear la OC vacía iteramos llamando POST /lineas/desde-requisicion
      // por cada una. Cada call necesita su propio Idempotency-Key — el
      // middleware del BE valida que sea un UUID v4 puro (`INVALID_IDEMPOTENCY_KEY`
      // si concatenamos strings). El sub-key del Sheet ya está usado
      // para el POST de la OC vacía; aquí generamos uno fresco por
      // iteración con crypto.randomUUID(). Una recarga del Sheet
      // (mount nuevo) generaría sub-keys distintos y el BE haría dedupe
      // por (empresaId, key, requestBodyHash); no buscamos ese dedupe
      // entre cargas porque el flujo no es re-entrante (RQ pasa a
      // ComprometidaEnOcId tras la primera ejecución exitosa).
      let lineasTotalesAgregadas = 0;
      const rqsConError: { rq: RequisicionDisponible; mensaje: string }[] = [];
      for (const rq of rqsSeleccionadas) {
        try {
          const lineasResp = await agregarLineaMut.mutateAsync({
            ordenCompraId: response.id,
            command: { requisicionId: rq.id },
            idempotencyKey: crypto.randomUUID(),
          });
          lineasTotalesAgregadas += lineasResp.lineasAgregadas;
        } catch (err) {
          const mensaje = esApiError(err)
            ? (err.problem.code ?? err.problem.title ?? 'Error desconocido')
            : err instanceof Error
              ? err.message
              : 'Error desconocido';
          rqsConError.push({ rq, mensaje });
        }
      }

      // Limpia el draft + cierra el sheet con force + navega al detalle.
      if (draftKey) clearDraft(draftKey);

      if (rqsSeleccionadas.length === 0) {
        toast.success(`OC creada: ${response.folio}`, {
          description: 'Continuá en el detalle para agregar líneas.',
        });
      } else if (rqsConError.length === 0) {
        toast.success(`OC creada: ${response.folio}`, {
          description: `Consolidada de ${rqsSeleccionadas.length} RQ(s) — ${lineasTotalesAgregadas} líneas agregadas.`,
        });
      } else {
        // Algunas RQs fallaron — la OC ya existe pero está parcial.
        // Mostramos toast de advertencia con conteos. El usuario puede
        // agregar las restantes manualmente desde el detalle (UF2-PR3).
        toast.warning(`OC creada parcial: ${response.folio}`, {
          description: `Se agregaron ${rqsSeleccionadas.length - rqsConError.length}/${rqsSeleccionadas.length} RQs. Errores: ${rqsConError.map((x) => `${x.rq.folio} (${x.mensaje})`).join(', ')}.`,
          duration: 10_000,
        });
      }

      onClose?.({ force: true });
      navigate({
        to: '/compras/ordenes/$id',
        params: { id: response.id },
      });
    } catch (err) {
      if (esApiError(err)) {
        // Cast: applyServerErrors tipa setError(name: string, ...);
        // useForm<T> lo tipa como UseFormSetError<T> (más estricto).
        // Mismo cast que NuevaRequisicion.
        applyServerErrors(
          form as unknown as Parameters<typeof applyServerErrors>[0],
          err,
        );
        const code = err.problem.code;
        if (code === 'CREAR_SIN_RQ_DENEGADO') {
          toast.error('No tienes permiso para OCs sin requisición previa.');
        } else if (err.status >= 500) {
          toast.error('Error interno al crear la OC.', {
            description: `Reporta el código: ${err.traceId ?? 'sin trace'}`,
          });
        }
      } else {
        toast.error('Error inesperado al crear la OC.');
      }
    }
  }

  return (
    <>
      {/* Recuperar borrador (modal pre-form) */}
      <Dialog open={showRecoverModal} onOpenChange={setShowRecoverModal}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Recuperar borrador</DialogTitle>
            <DialogDescription>
              Tienes un borrador de OC sin guardar de una sesión anterior.
              ¿Quieres continuar con esos datos o empezar de nuevo?
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={handleDescartarDraft}>
              Descartar y empezar de nuevo
            </Button>
            <Button onClick={handleRecuperar}>Recuperar borrador</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <form
        onSubmit={form.handleSubmit(onSubmit)}
        className="space-y-4 pt-2"
        data-component="sheet-nueva-oc"
        noValidate
      >
        {/* Toggle "Sin RQ previa" + botón consolidación */}
        <div className="flex flex-wrap gap-2 rounded-md border bg-muted/20 p-3 text-xs">
          {canCrearSinRq && (
            <label className="flex items-center gap-2">
              <input
                type="checkbox"
                {...form.register('sinRequisicionPrevia')}
                className="h-4 w-4 rounded border-input"
                data-action="toggle-sin-rq"
              />
              <span className="font-medium">
                Esta OC va sin requisición previa
              </span>
              <span className="text-muted-foreground">
                (FOC11 — requiere motivo y evidencia)
              </span>
            </label>
          )}
          {/*
            Botón "Agregar requisiciones (consolidación)" oculto cuando:
            - Modo 1:1 desde RQ (la RQ ya viene seleccionada, no se mezcla
              con otras).
            - Setting AutoGenerarOcAlAutorizar=true: las RQs ya se
              comprometen automáticamente al autorizar, no quedan
              disponibles para consolidación manual.
          */}
          {!modoUnoAUno && autoGenerarOcAlAutorizar !== true && (
            <div className="ml-auto flex items-center gap-2">
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => {
                  if (
                    sucursalDestinoId == null ||
                    sucursalDestinoId.length === 0
                  ) {
                    toast.warning(
                      'Selecciona primero la sucursal de destino. Las RQs disponibles dependen de la sucursal (§10.5).',
                    );
                    return;
                  }
                  setSelectorAbierto(true);
                }}
                data-action="abrir-selector-consolidacion"
              >
                <Plus className="mr-1 h-3.5 w-3.5" />
                {rqsSeleccionadas.length === 0
                  ? 'Agregar requisiciones (consolidación)'
                  : `Editar ${rqsSeleccionadas.length} RQ(s) seleccionadas`}
              </Button>
            </div>
          )}
        </div>

        {/* Chips de RQs seleccionadas (modo Consolidación) */}
        {rqsSeleccionadas.length > 0 && (
          <div
            className="flex flex-wrap gap-1.5 rounded-md border border-blue-200 bg-blue-50/40 p-2 text-xs"
            data-component="rqs-seleccionadas-chips"
          >
            <span className="font-medium text-blue-900">
              Consolidando {rqsSeleccionadas.length}{' '}
              {rqsSeleccionadas.length === 1 ? 'RQ' : 'RQs'}:
            </span>
            {rqsSeleccionadas.map((rq) => (
              <span
                key={rq.id}
                className="inline-flex items-center gap-1 rounded-full border border-blue-300 bg-white px-2 py-0.5 font-mono text-blue-900"
                data-rq-chip={rq.id}
              >
                {rq.folio} · {rq.totalLineas}L
                <button
                  type="button"
                  onClick={() =>
                    setRqsSeleccionadas((prev) =>
                      prev.filter((r) => r.id !== rq.id),
                    )
                  }
                  className="text-blue-700 hover:text-blue-900"
                  aria-label={`Quitar ${rq.folio} de la consolidación`}
                >
                  ×
                </button>
              </span>
            ))}
          </div>
        )}

        {/* Modal selector de RQs (UF2-PR2) */}
        <SelectorRequisicionesConsolidacion
          open={selectorAbierto}
          onOpenChange={setSelectorAbierto}
          sucursalId={sucursalDestinoId || null}
          rqsPreviamenteSeleccionadas={rqsSeleccionadas}
          onConfirm={(rqs) => setRqsSeleccionadas(rqs)}
        />

        {/* Panel motivo + uploader stub (solo modo SinRq) */}
        {sinRequisicionPrevia && (
          <div className="space-y-3 rounded-md border border-amber-200 bg-amber-50/40 p-3">
            <Controller
              control={form.control}
              name="motivoSinRequisicion"
              render={({ field, fieldState }) => (
                <FieldGroup
                  label="Motivo (obligatorio)"
                  error={fieldState.error?.message}
                >
                  <TextAreaField
                    value={field.value ?? null}
                    onChange={field.onChange}
                    minRows={2}
                  />
                </FieldGroup>
              )}
            />
            <UploadCorreoAutorizacion />
          </div>
        )}

        {/* Sección Proveedor (con panel historial al lado) */}
        <div className="grid gap-3 md:grid-cols-2">
          <Controller
            control={form.control}
            name="proveedorId"
            render={({ field, fieldState }) => (
              <FieldGroup label="Proveedor" error={fieldState.error?.message}>
                <ProveedorSelector
                  value={field.value ?? null}
                  onChange={(v) => field.onChange(v ?? '')}
                />
              </FieldGroup>
            )}
          />
          <HistorialComprasProveedor proveedorId={proveedorId || null} />
        </div>

        {/* Sección Org (sucursal + almacén) */}
        <div className="grid gap-3 md:grid-cols-2">
          <Controller
            control={form.control}
            name="sucursalDestinoId"
            render={({ field, fieldState }) => (
              <FieldGroup
                label="Sucursal destino"
                error={fieldState.error?.message}
              >
                <SucursalSelector
                  value={field.value ?? null}
                  onChange={(v) => field.onChange(v ?? '')}
                />
              </FieldGroup>
            )}
          />
        </div>

        {/* Sección comercial (condiciones + uso + moneda + TC) */}
        <div className="grid gap-3 md:grid-cols-2">
          <Controller
            control={form.control}
            name="condicionesPagoId"
            render={({ field, fieldState }) => (
              <FieldGroup
                label="Condiciones de pago"
                error={fieldState.error?.message}
              >
                <CondicionesPagoSelector
                  value={field.value || null}
                  onChange={(v) => field.onChange(v ?? '')}
                />
              </FieldGroup>
            )}
          />

          <Controller
            control={form.control}
            name="usoPrincipalId"
            render={({ field, fieldState }) => (
              <FieldGroup
                label="Uso principal"
                error={fieldState.error?.message}
              >
                <UsoPrincipalSelector
                  value={field.value || null}
                  onChange={(v) => field.onChange(v ?? '')}
                />
              </FieldGroup>
            )}
          />
        </div>

        <div className="grid gap-3 md:grid-cols-3">
          <Controller
            control={form.control}
            name="moneda"
            render={({ field, fieldState }) => (
              <FieldGroup label="Moneda" error={fieldState.error?.message}>
                <Select value={field.value || 'MXN'} onValueChange={field.onChange}>
                  <SelectTrigger data-field="moneda">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {MONEDAS_DISPONIBLES.map((m) => (
                      <SelectItem key={m.codigo} value={m.codigo}>
                        {m.nombre}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </FieldGroup>
            )}
          />
          {moneda !== 'MXN' && (
            <Controller
              control={form.control}
              name="tipoCambio"
              render={({ field, fieldState }) => (
                <FieldGroup
                  label={`Tipo de cambio (${moneda} → MXN)`}
                  error={fieldState.error?.message}
                >
                  <DecimalField
                    value={field.value ?? null}
                    onChange={field.onChange}
                    min={0.000001}
                    step={0.000001}
                  />
                </FieldGroup>
              )}
            />
          )}
          <Controller
            control={form.control}
            name="fechaDocumento"
            render={({ field, fieldState }) => (
              <FieldGroup
                label="Fecha del documento"
                error={fieldState.error?.message}
              >
                <DatePickerField
                  value={field.value ?? null}
                  onChange={(v) => field.onChange(v ?? '')}
                />
              </FieldGroup>
            )}
          />
        </div>

        {/* Sección logística básica (importación + fecha entrega) */}
        <div className="grid gap-3 md:grid-cols-2">
          <FieldGroup label="Bandera">
            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                {...form.register('esImportacion')}
                className="h-4 w-4 rounded border-input"
                data-field="es-importacion"
              />
              Es importación (campos de importación se llenan en UF3-PR1)
            </label>
          </FieldGroup>
          <Controller
            control={form.control}
            name="fechaEntregaEsperada"
            render={({ field, fieldState }) => (
              <FieldGroup
                label="Fecha entrega esperada (opcional)"
                error={fieldState.error?.message}
              >
                <DatePickerField
                  value={field.value ?? null}
                  onChange={(v) => field.onChange(v ?? null)}
                />
              </FieldGroup>
            )}
          />
        </div>

        {/* Observaciones */}
        <Controller
          control={form.control}
          name="observaciones"
          render={({ field, fieldState }) => (
            <FieldGroup
              label="Observaciones (opcional)"
              error={fieldState.error?.message}
            >
              <TextAreaField
                value={field.value ?? null}
                onChange={(v) => field.onChange(v || null)}
                minRows={3}
              />
            </FieldGroup>
          )}
        />

        {/* Footer del Sheet con acciones */}
        <div className="flex items-center justify-end gap-2 border-t pt-3">
          <Button
            type="button"
            variant="outline"
            onClick={() => onClose?.()}
            disabled={crearMut.isPending}
          >
            Cancelar
          </Button>
          <Button
            type="submit"
            disabled={crearMut.isPending}
            data-action="submit"
          >
            {crearMut.isPending ? 'Creando…' : 'Crear OC'}
          </Button>
        </div>
      </form>
    </>
  );
}

/**
 * Helper local: agrupa label + child + error de campo. Usado por los
 * <c>&lt;Select&gt;</c> y checkbox que no tienen el wrapper estándar
 * de los <c>*Field</c> del UX kit.
 */
function FieldGroup({
  label,
  error,
  children,
}: {
  label: string;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1">
      <label className="text-xs font-medium text-foreground">{label}</label>
      {children}
      {error && (
        <p className="text-xs text-destructive" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}

// Re-export helper para que el caller del Sheet pueda armar el form
// programáticamente sin importarlo de schemas/ directamente.
export { CrearOrdenCompraVaciaSchema };

// Suprimir warning de unused import si Input no se usa en este archivo.
void Input;
