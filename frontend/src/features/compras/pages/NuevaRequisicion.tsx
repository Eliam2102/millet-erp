import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from '@tanstack/react-router';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { ArrowLeft, HelpCircle } from 'lucide-react';
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
  DepartamentoSelectorPorSucursal,
  SucursalSelector,
  UsuarioSelector,
  ProveedorSelector,
  DatePickerField,
  TextAreaField,
} from '@/components/erp';
import { useSucursales, mapById } from '@/features/catalogos/api';
import { DomainTermTooltip } from '@/components/erp/feedback/DomainTermTooltip';
import {
  Clasificacion,
  Prioridad,
  clasificacionToString,
  prioridadToString,
} from '@/features/compras/api/types';
import {
  CrearRequisicionSchema,
  type CrearRequisicionValues,
} from '@/features/compras/schemas/crear-requisicion';
import { useCrearRequisicion } from '@/features/compras/api/useCrearRequisicion';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import { useUnsavedChangesGuard } from '@/lib/hooks/useUnsavedChangesGuard';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  buildNuevaRequisicionDraftKey,
  clearDraft,
  useDraftPersist,
  useDraftRecovery,
} from '@/features/compras/lib/draft-storage';
import { DEFAULT_BANDEJA_SEARCH } from '@/features/compras/lib/bandeja-search-schema';
import { formatDateLong } from '@/lib/datetime';

export interface NuevaRequisicionProps {
  /** Callback al cerrar (botón Cancelar / X / Esc). Cuando está
   * presente, el componente se renderiza en modo "embedded" — sin
   * h1 ni botón "Volver a bandeja", y al success llama onClose
   * además del navigate. Diseño polish: el sheet wrapper lo usa
   * para coordinar el cierre del overlay.
   *
   * <para>Pasa <c>force: true</c> en el flujo de éxito post-submit
   * para saltar el confirm de "tienes cambios sin guardar" — el
   * registro ya quedó creado, no hay nada que perder. El flujo de
   * cancelar omite el flag para que el provider muestre el prompt
   * si <c>isDirty</c>.</para> */
  onClose?: (opts?: { force?: boolean }) => void;
  /** Reporta al wrapper si el form tiene cambios sin guardar. El
   * Sheet provider lo consume para decidir si el confirm de cierre
   * debe mostrarse. */
  onDirtyChange?: (dirty: boolean) => void;
}

/**
 * <c>P4 — Nueva requisición</c> (doc 05 §5).
 *
 * <para>Form de cabecera con todos los selectores org + clasificación
 * + prioridad + descripción + fecha entrega + proveedor sugerido +
 * requisitante delegado (gateado por
 * <c>seleccionar-requisitante</c>).</para>
 *
 * <para>Comportamiento:</para>
 * <list>
 *   <item><b>Idempotency-Key</b> estable por mount
 *   (<c>useFormIdempotencyKey</c>); doble-submit no duplica RQ.</item>
 *   <item><b>Draft protection</b> (doc 05 §13.2): persiste el form
 *   en <c>localStorage</c> con debounce 500ms; al montar, si existe
 *   un draft, modal "Recuperar borrador". Borra tras submit exitoso.</item>
 *   <item><b>useUnsavedChangesGuard</b>: <c>beforeunload</c> nativo
 *   mientras <c>form.isDirty</c>.</item>
 *   <item><b>Almacén destino dependiente de sucursal</b>: cambiar
 *   sucursal resetea el almacén seleccionado.</item>
 *   <item><b>Errores</b>: 4xx con <c>errores[]</c> →
 *   <c>applyServerErrors</c>; 403
 *   <c>SELECCIONAR_REQUISITANTE_DENEGADO</c> → inline en selector;
 *   resto → toast.</item>
 *   <item><b>Submit OK</b>: invalida bandeja, borra draft, toast con
 *   folio, navigate a <c>/compras/requisiciones/$id</c>.</item>
 * </list>
 */
export function NuevaRequisicion({
  onClose,
  onDirtyChange,
}: NuevaRequisicionProps = {}) {
  const navigate = useNavigate();
  const embedded = onClose != null;
  const userId = useAuthStore((s) => s.user?.id);
  const empresaId = useAuthStore((s) => s.currentEmpresaId);
  const canSelectRequisitante = useHasPermission(
    PermisosCanonicos.ComprasRequisicionesSeleccionarRequisitante,
  );

  const idempotencyKey = useFormIdempotencyKey();
  const draftKey = useMemo(
    () => buildNuevaRequisicionDraftKey(userId, empresaId),
    [userId, empresaId],
  );

  const recovery = useDraftRecovery<CrearRequisicionValues>(draftKey);
  const [showRecoverModal, setShowRecoverModal] = useState(
    recovery.draft != null,
  );

  const form = useForm<CrearRequisicionValues>({
    resolver: zodResolver(CrearRequisicionSchema),
    defaultValues: buildEmptyValues(),
  });

  // Sucursal cambia → reset departamento (PR-A3: el departamento depende de
  // las asignaciones N:M por sucursal; si el depto elegido no opera en la
  // nueva sucursal, queda inválido).
  const sucursalId = useWatch({ control: form.control, name: 'sucursalId' });
  useEffect(() => {
    const currentDepto = form.getValues('departamentoId');
    if (currentDepto && currentDepto.length > 0) {
      form.setValue('departamentoId', '', { shouldDirty: false });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sucursalId]);

  // Persistencia de draft (debounce 500ms vía hook).
  const watched = useWatch({ control: form.control });
  useDraftPersist(draftKey, watched, form.formState.isDirty);
  useUnsavedChangesGuard(form.formState.isDirty);

  // Reporta el isDirty al wrapper para que el confirm del Sheet
  // sepa si debe mostrarse al cerrar.
  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange?.(isDirty);
  }, [isDirty, onDirtyChange]);

  // Catálogo de sucursales — usado para resolver el `clave` (código)
  // del sucursal seleccionado, requerido por el comando backend
  // (CrearRequisicionCommand.SucursalCodigo, validador
  // ^[A-Z]{2,4}$ — ej. "MID", "MX", "CAN").
  const sucursalesQuery = useSucursales();
  const sucursalesMap = useMemo(
    () => mapById(sucursalesQuery.data?.items),
    [sucursalesQuery.data],
  );

  const crear = useCrearRequisicion();

  function onSubmit(values: CrearRequisicionValues) {
    // Resolver sucursalCodigo desde el catálogo. Si no está en el
    // map (raro: la sucursal fue seleccionada del mismo catálogo),
    // bloquear el submit con un toast — el backend lo rechazaría con
    // SUCURSAL_CODIGO_REQUERIDO.
    const sucursal = sucursalesMap.get(values.sucursalId);
    if (sucursal == null) {
      toast.error('No se pudo resolver el código de sucursal.', {
        description:
          'Reintenta seleccionando la sucursal del menú; si persiste, recarga la página.',
      });
      return;
    }

    // fechaSolicitud viene de values (inicializado en buildEmptyValues() al
    // mount) — NO recalcular con new Date() aquí. Si lo recalculas, cada
    // reintento del submit cambia el body y el middleware de idempotencia
    // rechaza con 422 IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY (la key es
    // estable durante el mount; el body también debe serlo).
    const command = {
      ...values,
      sucursalCodigo: sucursal.clave,
      folioAnio: new Date(values.fechaSolicitud).getFullYear(),
    };

    crear.mutate(
      { command, idempotencyKey },
      {
        onSuccess: (response) => {
          if (draftKey) clearDraft(draftKey);
          // Reset isDirty para que useUnsavedChangesGuard no bloquee la nav.
          form.reset(values);
          toast.success(`Requisición ${response.folio} creada`, {
            description: 'Estado: Borrador. Agrega líneas y transmite cuando esté lista.',
          });
          // Si está embedded (Sheet), cerrar el overlay antes de navegar
          // — evita que el detalle se monte mientras el Sheet aún está
          // visible. <c>force: true</c> salta el confirm de "cambios sin
          // guardar" (el registro ya quedó creado, no hay nada que
          // perder; el form.reset(values) limpió isDirty pero el ref
          // del provider sigue stale hasta el próximo render).
          onClose?.({ force: true });
          navigate({
            to: '/compras/requisiciones/$id',
            params: { id: response.id },
          });
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'SELECCIONAR_REQUISITANTE_DENEGADO') {
              form.setError('requisitanteId', {
                type: error.code,
                message:
                  'No tienes permiso para crear a nombre de otro requisitante.',
              });
              return;
            }
            // Cast: applyServerErrors tipa setError(name: string, ...);
            // RHF lo tipa estrictamente contra los keys del schema. El
            // helper hace pass-through del campo del backend (camelCase
            // que ya coincide con los nombres del form), así que el
            // cast es seguro.
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
          toast.error('Error inesperado al crear la requisición.');
        },
      },
    );
  }

  function handleRecuperar() {
    if (recovery.draft) {
      form.reset(recovery.draft.values, {
        keepDirty: true,
      });
    }
    recovery.acknowledge();
    setShowRecoverModal(false);
  }

  function handleDescartarDraft() {
    recovery.discard();
    setShowRecoverModal(false);
  }

  return (
    <div className="space-y-4">
      {!embedded && (
        <div className="flex items-center justify-between">
          <h1 className="text-2xl font-semibold tracking-tight">
            Nueva requisición
          </h1>
          <Button asChild variant="ghost">
            <Link
              to="/compras/requisiciones"
              search={DEFAULT_BANDEJA_SEARCH}
            >
              <ArrowLeft className="mr-2 h-4 w-4" />
              Volver a bandeja
            </Link>
          </Button>
        </div>
      )}

      <form
        onSubmit={form.handleSubmit(onSubmit)}
        noValidate
        className="grid grid-cols-1 gap-4 rounded-md border bg-card p-4 md:grid-cols-2 max-w-4xl"
      >
        {/* Sucursal */}
        <FormRow
          label="Sucursal"
          error={form.formState.errors.sucursalId?.message}
          required
        >
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
        </FormRow>

        {/* Departamento */}
        <FormRow
          label="Departamento"
          error={form.formState.errors.departamentoId?.message}
          required
        >
          <Controller
            name="departamentoId"
            control={form.control}
            render={({ field }) => (
              <DepartamentoSelectorPorSucursal
                sucursalId={sucursalId && sucursalId.length > 0 ? sucursalId : null}
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
              />
            )}
          />
        </FormRow>


        {/* Requisitante (gateado por permiso) */}
        {canSelectRequisitante && (
          <FormRow
            label="Requisitante (delegación)"
            error={form.formState.errors.requisitanteId?.message}
          >
            <Controller
              name="requisitanteId"
              control={form.control}
              render={({ field }) => (
                <UsuarioSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? null)}
                  placeholder="Yo (default)"
                />
              )}
            />
          </FormRow>
        )}

        {/* Clasificación */}
        <FormRow
          label="Clasificación"
          tooltip="Tipo funcional de la requisición. Determina parte de la matriz de aprobación A1 (§3.bis)."
          error={form.formState.errors.clasificacion?.message}
          required
        >
          <Controller
            name="clasificacion"
            control={form.control}
            render={({ field }) => (
              <Select
                value={String(field.value)}
                onValueChange={(v) => field.onChange(Number(v) as Clasificacion)}
              >
                <SelectTrigger aria-label="Clasificación">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {(
                    [
                      Clasificacion.Servicio,
                      Clasificacion.OrdenCompra,
                      Clasificacion.MateriaPrima,
                      Clasificacion.Pinturas,
                    ] as const
                  ).map((c) => (
                    <SelectItem key={c} value={String(c)}>
                      {clasificacionToString(c)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          />
        </FormRow>

        {/* Prioridad */}
        <FormRow
          label="Prioridad"
          tooltip="Orden de atención sugerido. NO afecta la matriz de aprobación; sirve para que el comprador priorice surtido."
          error={form.formState.errors.prioridad?.message}
          required
        >
          <Controller
            name="prioridad"
            control={form.control}
            render={({ field }) => (
              <Select
                value={String(field.value)}
                onValueChange={(v) => field.onChange(Number(v) as Prioridad)}
              >
                <SelectTrigger aria-label="Prioridad">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {(
                    [Prioridad.Baja, Prioridad.Normal, Prioridad.Alta] as const
                  ).map((p) => (
                    <SelectItem key={p} value={String(p)}>
                      {prioridadToString(p)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          />
        </FormRow>

        {/* Fecha entrega deseada */}
        <FormRow
          label="Fecha entrega deseada"
          error={form.formState.errors.fechaEntregaDeseada?.message}
        >
          <Controller
            name="fechaEntregaDeseada"
            control={form.control}
            render={({ field }) => (
              <DatePickerField
                value={field.value ?? null}
                onChange={(d) => field.onChange(d)}
                minDate={new Date()}
              />
            )}
          />
        </FormRow>

        {/* Proveedor sugerido */}
        <FormRow
          label="Proveedor sugerido"
          error={form.formState.errors.proveedorSugeridoId?.message}
        >
          <Controller
            name="proveedorSugeridoId"
            control={form.control}
            render={({ field }) => (
              <ProveedorSelector
                value={field.value ?? null}
                onChange={(id) => field.onChange(id ?? null)}
              />
            )}
          />
        </FormRow>

        {/* Descripción (full-width) */}
        <div className="md:col-span-2">
          <FormRow
            label="Descripción"
            error={form.formState.errors.descripcion?.message}
          >
            <Controller
              name="descripcion"
              control={form.control}
              render={({ field }) => (
                <TextAreaField
                  value={field.value ?? null}
                  onChange={(v) => field.onChange(v)}
                  maxLength={500}
                  minRows={3}
                />
              )}
            />
          </FormRow>
        </div>

        {/* Submit + cancel */}
        <div className="md:col-span-2 flex flex-wrap items-center justify-end gap-2 border-t pt-3">
          <Input
            // Hidden — fechaSolicitud se inicializa una vez en
            // buildEmptyValues() al montar el form y queda estable durante
            // la vida del componente. En el submit usamos values.fechaSolicitud
            // (NO recalculamos con new Date()) para que el body del request
            // sea estable y reintentos con la misma idempotency key no
            // disparen IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY.
            type="hidden"
            {...form.register('fechaSolicitud')}
          />
          {embedded ? (
            <Button
              type="button"
              variant="ghost"
              // Wrapper para descartar el MouseEvent de React — onClose
              // acepta opts opcionales, no un event handler.
              onClick={() => onClose?.()}
              disabled={crear.isPending}
            >
              Cancelar
            </Button>
          ) : (
            <Button asChild variant="ghost">
              <Link to="/compras/requisiciones" search={DEFAULT_BANDEJA_SEARCH}>
                Cancelar
              </Link>
            </Button>
          )}
          <Button type="submit" disabled={crear.isPending}>
            {crear.isPending ? 'Creando…' : 'Crear requisición'}
          </Button>
        </div>
      </form>

      <RecuperarBorradorModal
        open={showRecoverModal}
        savedAt={recovery.draft?.meta.savedAt}
        onRecuperar={handleRecuperar}
        onDescartar={handleDescartarDraft}
      />
    </div>
  );
}

// ─── Helpers de form layout ───────────────────────────────────────

interface FormRowProps {
  label: string;
  tooltip?: string;
  required?: boolean;
  error?: string;
  children: React.ReactNode;
}

function FormRow({ label, tooltip, required, error, children }: FormRowProps) {
  return (
    <div className="space-y-1.5">
      <label className="flex items-center gap-1 text-sm font-medium">
        {label}
        {required && (
          <span aria-hidden="true" className="text-rose-600">
            *
          </span>
        )}
        {tooltip && (
          <DomainTermTooltip term={label} definicion={tooltip}>
            <HelpCircle
              className="h-3.5 w-3.5 cursor-help text-muted-foreground"
              aria-label="Ayuda"
            />
          </DomainTermTooltip>
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

interface RecuperarBorradorModalProps {
  open: boolean;
  savedAt: string | undefined;
  onRecuperar: () => void;
  onDescartar: () => void;
}

function RecuperarBorradorModal({
  open,
  savedAt,
  onRecuperar,
  onDescartar,
}: RecuperarBorradorModalProps) {
  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        // Si el usuario cierra con ESC / click fuera, lo tratamos como
        // "Descartar" para no quedar con un modal zombie cada vez que
        // entre a la pantalla.
        if (!next) onDescartar();
      }}
    >
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Tienes un borrador guardado</DialogTitle>
          <DialogDescription>
            {savedAt != null && (
              <>Última modificación: {formatDateLong(savedAt)}.</>
            )}{' '}
            ¿Quieres recuperarlo o empezar de cero?
          </DialogDescription>
        </DialogHeader>
        <DialogFooter className="flex-col gap-2 sm:flex-row sm:justify-end">
          <Button variant="ghost" onClick={onDescartar}>
            Descartar
          </Button>
          <Button onClick={onRecuperar} autoFocus>
            Recuperar borrador
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function buildEmptyValues(): CrearRequisicionValues {
  return {
    sucursalId: '',
    departamentoId: '',
    requisitanteId: undefined,
    clasificacion: Clasificacion.Servicio,
    prioridad: Prioridad.Normal,
    fechaSolicitud: new Date().toISOString(),
    fechaEntregaDeseada: null,
    proveedorSugeridoId: null,
    descripcion: null,
  } as CrearRequisicionValues;
}
