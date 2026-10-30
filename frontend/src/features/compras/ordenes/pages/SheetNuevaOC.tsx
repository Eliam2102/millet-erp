import { CampoFacturaOc } from '../components/CampoFacturaOc';
import { DialogBody } from '@/components/ui/dialog';
import { FormGrid, FormCampo } from '@/components/erp/forms/FormGrid';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { EyeOff, Plus, X } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Badge } from '@/components/ui/badge';
import { Separator } from '@/components/ui/separator';
import { SeccionOc, type EstadoSeccionOc } from '@/features/compras/ordenes/components/SeccionOc';
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
  Field,
  TextAreaField,
  DecimalField,
} from '@/components/erp';
import { useSucursales, useDepartamentos, mapById } from '@/features/catalogos/api';
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
  sucursalPorDefecto,
  useSucursalesPermitidas,
} from '@/features/compras/ordenes/api/useSucursalesPermitidas';
import { applyServerErrors, esApiError, useFormIdempotencyKey } from '@/lib/api';
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

const MONEDAS_DISPONIBLES: ReadonlyArray<{ codigo: string; nombre: string }> = [
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
  const sucursalActivaId = useAuthStore((s) => s.currentSucursalId);
  const canCrearSinRq = useHasPermission(PermisosCanonicos.ComprasOrdenesCrearSinRq);

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
  const [showRecoverModal, setShowRecoverModal] = useState(recovery.draft != null);

  // Modo consolidación: estado de RQs seleccionadas + visibilidad
  // del modal selector. Las RQs viven SOLO en estado del Sheet, NO en
  // el form (no son campos persistibles del comando, son metadatos
  // del flujo). Si la sucursal cambia, las RQs se limpian para no
  // violar §10.5.
  const [selectorAbierto, setSelectorAbierto] = useState(false);
  const [rqsSeleccionadas, setRqsSeleccionadas] = useState<readonly RequisicionDisponible[]>([]);

  const form = useForm<CrearOrdenCompraVaciaValues>({
    resolver: zodResolver(CrearOrdenCompraVaciaSchema),
    // La sucursal destino arranca en la activa del sistema; desde RQ manda la de la RQ.
    defaultValues: { ...DEFAULT_CREAR_OC_VACIA, sucursalDestinoId: sucursalActivaId ?? '' },
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
  const sucursalesMap = useMemo(() => mapById(sucursalesQuery.data?.items), [sucursalesQuery.data]);

  // Default de sucursal destino: con una sola sucursal permitida se
  // preselecciona una sola vez (si el campo sigue vacío). Con varias
  // queda vacío y obligatorio (validado en el schema). El ref evita que
  // al limpiarlo el usuario se vuelva a rellenar solo.
  const sucursalesPermitidasQuery = useSucursalesPermitidas();
  const sucursalDefaultAplicada = useRef(false);
  useEffect(() => {
    if (sucursalDefaultAplicada.current) return;
    const defecto = sucursalPorDefecto(sucursalesPermitidasQuery.data ?? []);
    if (defecto == null) return;
    sucursalDefaultAplicada.current = true;
    if (!form.getValues('sucursalDestinoId')) {
      form.setValue('sucursalDestinoId', defecto, { shouldDirty: false });
    }
  }, [sucursalesPermitidasQuery.data, form]);

  // Watch fields via useWatch (estable, suscripción granular). El
  // form.watch() de RHF dispara re-renders extra y trigger el lint
  // react-hooks/incompatible-library.
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
  const [trackedSucursal, setTrackedSucursal] = useState<string | undefined>(sucursalDestinoId);
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
  if (desdeRequisicion != null && desdeRequisicion.requisicionId !== trackedDesdeRqId) {
    setTrackedDesdeRqId(desdeRequisicion.requisicionId);
    form.setValue('sucursalDestinoId', desdeRequisicion.sucursalId, {
      shouldDirty: false,
    });
  }

  const requisicionesDisponiblesQuery = useRequisicionesDisponibles(
    modoUnoAUno ? (desdeRequisicion?.sucursalId ?? null) : null,
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

  const departamentosQuery = useDepartamentos();
  const deptosMap = useMemo(() => mapById(departamentosQuery.data?.items), [departamentosQuery.data]);
  const totalPartidas = rqsSeleccionadas.reduce((n, rq) => n + rq.totalLineas, 0);

  // Estado por sección derivado del form: errores vigentes de RHF y los
  // obligatorios que ya define el esquema (sin reglas nuevas).
  const errores = form.formState.errors;
  const seccion = (
    campos: ReadonlyArray<keyof CrearOrdenCompraVaciaValues>,
    completa: boolean,
  ): EstadoSeccionOc => {
    const n = campos.filter((c) => errores[c] != null).length;
    return { errores: n, completa: completa && n === 0 };
  };
  const wv = watchedValues;
  const estadoSec1 = seccion(['sucursalDestinoId', 'proveedorId'], !!wv.sucursalDestinoId && !!wv.proveedorId);
  const estadoSec2 = seccion(
    ['motivoSinRequisicion'],
    sinRequisicionPrevia ? !!wv.motivoSinRequisicion?.trim() : rqsSeleccionadas.length > 0,
  );
  const estadoSec3 = seccion(
    ['condicionesPagoId', 'moneda', 'tipoCambio', 'fechaDocumento', 'fechaEntregaEsperada'],
    !!wv.condicionesPagoId && !!wv.moneda && !!wv.fechaDocumento && (moneda === 'MXN' || wv.tipoCambio != null),
  );
  const estadoSec4 = seccion(
    ['usoCfdiClave', 'metodoPago', 'formaPagoClave'],
    !!wv.usoCfdiClave && !!wv.metodoPago && !!wv.formaPagoClave,
  );
  const estadoSec5 = seccion(['usoPrincipalId', 'observaciones'], !!wv.usoPrincipalId);

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
        description: 'Recarga la página o contacta a soporte si persiste.',
      });
      return;
    }

    if (!values.sinRequisicionPrevia && rqsSeleccionadas.length === 0) {
      toast.error(
        'Selecciona una requisición o marca «Esta orden va sin requisición previa» e indica el motivo y la evidencia.',
      );
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
            ? (err.problem.detail ?? err.problem.title ?? 'Error desconocido')
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
          description: 'Continúa en el detalle para agregar partidas.',
        });
      } else if (rqsConError.length === 0) {
        toast.success(`OC creada: ${response.folio}`, {
          description: `Consolidada de ${rqsSeleccionadas.length} RQ(s) — ${lineasTotalesAgregadas} partidas agregadas.`,
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
        applyServerErrors(form as unknown as Parameters<typeof applyServerErrors>[0], err);
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
        <DialogContent size="sm">
          <DialogHeader>
            <DialogTitle>Recuperar borrador</DialogTitle>
          </DialogHeader>
          <DialogBody>
            <DialogDescription>
              Tienes un borrador de OC sin guardar de una sesión anterior. ¿Quieres continuar con
              esos datos o empezar de nuevo?
            </DialogDescription>
          </DialogBody>
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
        className="space-y-6 pt-1"
        data-component="sheet-nueva-oc"
        noValidate
      >
        {/* 1 · Destino y proveedor */}
        <SeccionOc numero={1} titulo="Destino y proveedor" estado={estadoSec1}>
          <Controller
            control={form.control}
            name="sucursalDestinoId"
            render={({ field, fieldState }) => (
              <Field
                label="Sucursal destino"
                required
                hint="Define qué requisiciones puedes consolidar."
                error={fieldState.error?.message}
              >
                {(c) => (
                  <SucursalSelector
                    triggerProps={c}
                    value={field.value ?? null}
                    onChange={(v) => field.onChange(v ?? '')}
                  />
                )}
              </Field>
            )}
          />
          <Controller
            control={form.control}
            name="proveedorId"
            render={({ field, fieldState }) => (
              <Field label="Proveedor" error={fieldState.error?.message}>
                {(c) => (
                  <ProveedorSelector
                    triggerProps={c}
                    value={field.value ?? null}
                    onChange={(v) => field.onChange(v ?? '')}
                  />
                )}
              </Field>
            )}
          />
        </SeccionOc>

        <Separator />

        {/* 2 · Origen de la compra */}
        <SeccionOc numero={2} titulo="Origen de la compra" estado={estadoSec2}>
          <Controller
            control={form.control}
            name="sinRequisicionPrevia"
            render={({ field }) => (
              <fieldset className="min-w-0 space-y-2">
                <legend className="mb-2 text-xs font-medium text-ink-strong">
                  ¿De dónde sale esta orden?
                </legend>
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  {[
                    {
                      valor: false,
                      titulo: 'Desde requisiciones',
                      texto: 'Consolida una o varias RQ disponibles de la sucursal.',
                    },
                    {
                      valor: true,
                      titulo: 'Sin requisición previa',
                      texto: 'Pide motivo y correo de autorización.',
                    },
                  ].map((op) => {
                    const deshabilitada = op.valor && !canCrearSinRq;
                    return (
                      <label
                        key={op.titulo}
                        className={cn(
                          'flex cursor-pointer items-start gap-2.5 rounded-md border border-line-control bg-surface-card p-3',
                          'has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-ring has-[:focus-visible]:ring-offset-1',
                          field.value === op.valor && 'border-brand bg-brand-soft',
                          deshabilitada && 'cursor-not-allowed opacity-60',
                        )}
                      >
                        <input
                          type="radio"
                          name="origen-oc"
                          checked={field.value === op.valor}
                          onChange={() => field.onChange(op.valor)}
                          disabled={deshabilitada}
                          className="mt-0.5 h-4 w-4 shrink-0 accent-[var(--mt-primary)]"
                          data-action={op.valor ? 'toggle-sin-rq' : 'origen-desde-rq'}
                        />
                        <span className="min-w-0">
                          <span className="block text-sm font-medium text-ink">{op.titulo}</span>
                          <span className="block text-xs text-ink-muted">{op.texto}</span>
                        </span>
                      </label>
                    );
                  })}
                </div>
              </fieldset>
            )}
          />

          {!sinRequisicionPrevia && (
            <div
              className="overflow-hidden rounded-md border border-line bg-surface-card"
              data-component="rqs-seleccionadas-chips"
            >
              <div className="flex items-center justify-between gap-2 bg-surface-muted/40 px-3 py-2.5 text-xs">
                <p aria-live="polite" className="text-ink-muted">
                  {rqsSeleccionadas.length === 0 ? (
                    'Aún no agregas requisiciones.'
                  ) : (
                    <>
                      <strong className="font-semibold text-ink">
                        {rqsSeleccionadas.length}{' '}
                        {rqsSeleccionadas.length === 1 ? 'requisición' : 'requisiciones'}
                      </strong>{' '}
                      · {totalPartidas} {totalPartidas === 1 ? 'partida se agregará' : 'partidas se agregarán'} a la OC
                    </>
                  )}
                </p>
                {/*
                  Botón "Agregar" oculto cuando:
                  - Modo 1:1 desde RQ (la RQ ya viene seleccionada, no se mezcla
                    con otras).
                  - Setting AutoGenerarOcAlAutorizar=true: las RQs ya se
                    comprometen automáticamente al autorizar, no quedan
                    disponibles para consolidación manual.
                */}
                {!modoUnoAUno && autoGenerarOcAlAutorizar !== true && (
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() => {
                      if (sucursalDestinoId == null || sucursalDestinoId.length === 0) {
                        toast.warning(
                          'Selecciona primero la sucursal de destino. Las RQs disponibles dependen de la sucursal.',
                        );
                        return;
                      }
                      setSelectorAbierto(true);
                    }}
                    data-action="abrir-selector-consolidacion"
                  >
                    <Plus className="mr-1 h-3.5 w-3.5" aria-hidden="true" />
                    Agregar
                  </Button>
                )}
              </div>
              {rqsSeleccionadas.length > 0 && (
                <ul className="divide-y divide-line-divider border-t border-line-divider">
                  {rqsSeleccionadas.map((rq) => (
                    <li
                      key={rq.id}
                      data-rq-chip={rq.id}
                      className="flex items-center gap-2 px-3 py-2.5 text-xs"
                    >
                      <span className="font-mono text-ink">{rq.folio}</span>
                      <span className="min-w-0 flex-1 truncate text-ink-muted">
                        · {deptosMap.get(rq.departamentoId)?.nombre ?? 'Sin área'} · {rq.totalLineas}{' '}
                        {rq.totalLineas === 1 ? 'partida' : 'partidas'}
                      </span>
                      <button
                        type="button"
                        onClick={() =>
                          setRqsSeleccionadas((prev) => prev.filter((r) => r.id !== rq.id))
                        }
                        className="rounded-sm p-1 text-ink-muted hover:text-ink focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                        aria-label={`Quitar ${rq.folio} de la consolidación`}
                      >
                        <X className="h-4 w-4" aria-hidden="true" />
                      </button>
                    </li>
                  ))}
                </ul>
              )}
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

          {/* Motivo + adjunto (solo modo SinRq) */}
          {sinRequisicionPrevia && (
            <div className="space-y-3.5">
              <Controller
                control={form.control}
                name="motivoSinRequisicion"
                render={({ field, fieldState }) => (
                  <Field label="Motivo" required error={fieldState.error?.message}>
                    {(c) => (
                      <TextAreaField
                        textareaProps={c}
                        value={field.value ?? null}
                        onChange={field.onChange}
                        minRows={2}
                      />
                    )}
                  </Field>
                )}
              />
              <UploadCorreoAutorizacion />
            </div>
          )}
        </SeccionOc>

        <Separator />

        {/* 3 · Condiciones comerciales */}
        <SeccionOc numero={3} titulo="Condiciones comerciales" estado={estadoSec3}>
          <FormGrid>
            <FormCampo span={6}>
              <Controller
                control={form.control}
                name="condicionesPagoId"
                render={({ field, fieldState }) => (
                  <Field label="Condiciones de pago" error={fieldState.error?.message}>
                    {(c) => (
                      <CondicionesPagoSelector
                        triggerProps={c}
                        value={field.value || null}
                        onChange={(v) => field.onChange(v ?? '')}
                      />
                    )}
                  </Field>
                )}
              />
            </FormCampo>
            <FormCampo span={6}>
              <Controller
                control={form.control}
                name="moneda"
                render={({ field, fieldState }) => (
                  <Field label="Moneda" error={fieldState.error?.message}>
                    {(c) => (
                      <Select value={field.value || 'MXN'} onValueChange={field.onChange}>
                        <SelectTrigger data-field="moneda" {...c}>
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
                    )}
                  </Field>
                )}
              />
            </FormCampo>
            {moneda !== 'MXN' && (
              <>
                <FormCampo span={6}>
                  <Controller
                    control={form.control}
                    name="tipoCambio"
                    render={({ field, fieldState }) => (
                      <Field
                        label={`Tipo de cambio (${moneda} → MXN)`}
                        error={fieldState.error?.message}
                      >
                        {(c) => (
                          <DecimalField
                            inputProps={c}
                            value={field.value ?? null}
                            onChange={field.onChange}
                            min={0.000001}
                            step={0.000001}
                          />
                        )}
                      </Field>
                    )}
                  />
                </FormCampo>
                <FormCampo span={6} className="self-start pt-[26px]">
                  <p className="flex h-ctl-md items-center text-xs text-ink-muted">
                    Aparece solo con moneda distinta a MXN.
                  </p>
                </FormCampo>
              </>
            )}
            <FormCampo span={6}>
              <Controller
                control={form.control}
                name="fechaDocumento"
                render={({ field, fieldState }) => (
                  <Field label="Fecha del documento" error={fieldState.error?.message}>
                    {(c) => (
                      <DatePickerField
                        id={c.id}
                        value={field.value ?? null}
                        onChange={(v) => field.onChange(v ?? '')}
                      />
                    )}
                  </Field>
                )}
              />
            </FormCampo>
            <FormCampo span={6}>
              <Controller
                control={form.control}
                name="fechaEntregaEsperada"
                render={({ field, fieldState }) => (
                  <Field label="Entrega esperada" optional error={fieldState.error?.message}>
                    {(c) => (
                      <DatePickerField
                        id={c.id}
                        value={field.value ?? null}
                        onChange={(v) => field.onChange(v ?? null)}
                      />
                    )}
                  </Field>
                )}
              />
            </FormCampo>
          </FormGrid>

          <label
            className={cn(
              'flex cursor-pointer items-start gap-2.5 rounded-md border border-line-control bg-surface-card p-3',
              'has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-ring has-[:focus-visible]:ring-offset-1',
              'has-[:checked]:border-brand has-[:checked]:bg-brand-soft',
            )}
          >
            <input
              type="checkbox"
              {...form.register('esImportacion')}
              className="mt-0.5 h-4 w-4 shrink-0 accent-[var(--mt-primary)]"
              data-field="es-importacion"
            />
            <span className="min-w-0">
              <span className="block text-sm font-medium text-ink">
                Es importación o exportación
              </span>
              <span className="block text-xs text-ink-muted">
                Incoterm, país, BL y demás datos se capturan en el detalle antes de enviarla a
                autorización.
              </span>
            </span>
          </label>
        </SeccionOc>

        <Separator />

        {/* 4 · Datos para la factura del proveedor */}
        <SeccionOc
          numero={4}
          titulo="Datos para la factura del proveedor"
          subtitulo="Puedes crear la OC sin ellos; son obligatorios para enviarla a autorización."
          estado={estadoSec4}
          derecha={<Badge variant="neutral">Se piden al autorizar</Badge>}
        >
          <FormGrid>
            {(['usoCfdiClave', 'metodoPago', 'formaPagoClave'] as const).map((campo) => (
              <FormCampo key={campo} span={campo === 'usoCfdiClave' ? 12 : 6}>
                <Controller
                  control={form.control}
                  name={campo}
                  render={({ field, fieldState }) => (
                    <CampoFacturaOc
                      campo={campo}
                      value={field.value}
                      onChange={field.onChange}
                      error={fieldState.error?.message}
                    />
                  )}
                />
              </FormCampo>
            ))}
          </FormGrid>
        </SeccionOc>

        <Separator />

        {/* 5 · Uso y observaciones */}
        <SeccionOc numero={5} titulo="Uso y observaciones" estado={estadoSec5}>
          <Controller
            control={form.control}
            name="usoPrincipalId"
            render={({ field, fieldState }) => (
              <Field
                label={
                  <span className="flex items-center justify-between gap-2">
                    Uso interno de la compra
                    <span className="inline-flex items-center gap-1 font-normal text-ink-muted">
                      <EyeOff className="h-3.5 w-3.5" aria-hidden="true" />
                      No se imprime en la OC
                    </span>
                  </span>
                }
                error={fieldState.error?.message}
              >
                {(c) => (
                  <UsoPrincipalSelector
                    triggerProps={c}
                    value={field.value || null}
                    onChange={(v) => field.onChange(v ?? '')}
                  />
                )}
              </Field>
            )}
          />
          <Controller
            control={form.control}
            name="observaciones"
            render={({ field, fieldState }) => (
              <Field label="Observaciones" optional error={fieldState.error?.message}>
                {(c) => (
                  <TextAreaField
                    textareaProps={c}
                    value={field.value ?? null}
                    onChange={(v) => field.onChange(v || null)}
                    minRows={3}
                  />
                )}
              </Field>
            )}
          />
        </SeccionOc>

        {/* Footer pegado al borde inferior del scroll */}
        <div className="sticky -bottom-5 -mx-6 -mb-5 flex items-center justify-end gap-2 border-t border-line-divider bg-background px-6 py-4">
          <Button
            type="button"
            variant="outline"
            onClick={() => onClose?.()}
            disabled={crearMut.isPending}
          >
            Cancelar
          </Button>
          <Button type="submit" disabled={crearMut.isPending} data-action="submit">
            {crearMut.isPending ? 'Creando…' : 'Crear OC'}
          </Button>
        </div>
      </form>
    </>
  );
}

// Re-export helper para que el caller del Sheet pueda armar el form
// programáticamente sin importarlo de schemas/ directamente.
export { CrearOrdenCompraVaciaSchema };

// Suprimir warning de unused import si Input no se usa en este archivo.
void Input;
