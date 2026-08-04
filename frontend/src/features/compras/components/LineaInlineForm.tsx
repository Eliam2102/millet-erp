import { useEffect, useMemo, useRef, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Check, ChevronDown, ChevronRight, Plus, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  ArticuloSelector,
  DatePickerField,
  DecimalField,
  DECIMALES_FALLBACK,
  MoneyField,
  stepParaDecimales,
  TextAreaField,
  useDecimalesUnidad,
} from '@/components/erp';
import {
  applyServerErrors,
  esApiError,
} from '@/lib/api';
import { esConflictoConcurrencia } from '@/lib/api/error';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { useQueryClient } from '@tanstack/react-query';
import { comprasKeys } from '@/features/compras/api/keys';
import {
  crearLineaSchema,
  type LineaValues,
} from '@/features/compras/schemas/linea';
import {
  useActualizarLinea,
  useAgregarLinea,
} from '@/features/compras/api/useLineas';
import { useCcMaquinaPrellenado } from '@/features/compras/api/useCcMaquinaPrellenado';
import type { LineaResponse } from '@/features/compras/api/types';
import { Dim3Picker } from '@/features/centros-costo/components/Dim3Picker';
import { formatCcMaquinaLabel } from '@/features/centros-costo/lib/cc-maquina-label';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;LineaInlineForm/&gt;</c> — form expandible inline (sin modal)
 * para AGREGAR o EDITAR líneas en el editor. Diseño polish
 * (design/frontend-polish): para una RQ con muchas líneas,
 * abrir/cerrar un modal por cada una es fricción innecesaria.
 *
 * <para><b>Modo agregar</b> (sin <c>linea</c>): el form se mantiene
 * abierto entre submits — al guardar, los campos se resetean y el
 * cursor vuelve al artículo, listo para la próxima. Cancelar colapsa.</para>
 *
 * <para><b>Modo editar</b> (con <c>linea</c>): el form arranca con los
 * valores de la línea seleccionada. Submit la actualiza vía PATCH y
 * llama <c>onSaved</c>. Cancelar colapsa sin guardar. La fila
 * existente queda visible debajo durante la edición.</para>
 *
 * <para><b>Layout</b>: 5 campos críticos en grid (artículo, cantidad,
 * unidad, precio, fecha). Botón "+ Detalles" expande una segunda fila
 * con los opcionales (cuenta contable, centro de costo, proyecto,
 * notas).</para>
 *
 * <para><b>Validación</b>: misma <see cref="LineaSchema"/> en ambos
 * modos. Errores 422 del backend se mapean al campo correspondiente
 * con <c>applyServerErrors</c>. 409 abre el conflict dialog en modo
 * simple.</para>
 */
export interface LineaInlineFormProps {
  requisicionId: string;
  /** Si se provee, el form arranca en modo EDITAR con los valores de
   * esta línea. Si es <c>null</c>/<c>undefined</c>, modo AGREGAR. */
  linea?: LineaResponse | null;
  /** Callback al cancelar (botón Cancelar / Esc). El parent decide
   * cómo colapsar el form. */
  onCancel: () => void;
  /** Callback tras submit exitoso. Modo agregar: el form se resetea
   * para agregar otra; modo editar: el parent debe colapsar. */
  onSaved?: () => void;
}

const VALORES_INICIALES: LineaValues = {
  articuloId: '',
  cantidad: 1,
  // Placeholder hasta elegir artículo: la UM se hereda de
  // UnidadMedidaDefault al seleccionar (campo read-only). No se puede
  // guardar una línea sin artículo (required), así que '' nunca persiste.
  unidadMedida: '',
  precioEstimadoMonto: 0,
  precioEstimadoMoneda: 'MXN',
  cuentaContableId: null,
  // Fase E PR2.1: CC-Máquina requerido. Empty string = "no elegido aún" (mismo
  // patrón que articuloId); el schema `idLike` lo rechaza al submit → bloquea.
  centroCostoId: '',
  proyecto: null,
  fechaRequerida: null,
  notas: null,
} as LineaValues;

function buildValuesFromLinea(linea: LineaResponse): LineaValues {
  return {
    articuloId: linea.articuloId,
    cantidad: linea.cantidad,
    unidadMedida: linea.unidadMedida,
    precioEstimadoMonto: linea.precioEstimadoMonto,
    precioEstimadoMoneda: linea.precioEstimadoMoneda,
    cuentaContableId: linea.cuentaContableId,
    // Línea histórica sin CC → '' obliga a elegir uno al editar (PR2.1).
    centroCostoId: linea.centroCostoId ?? '',
    proyecto: linea.proyecto,
    fechaRequerida: linea.fechaRequerida,
    notas: linea.notas,
  } as LineaValues;
}

export function LineaInlineForm({
  requisicionId,
  linea,
  onCancel,
  onSaved,
}: LineaInlineFormProps) {
  const esEditar = linea != null;
  const agregar = useAgregarLinea();
  const actualizar = useActualizarLinea();
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();
  const [detallesAbiertos, setDetallesAbiertos] = useState(false);

  const [unidadMedidaIdSel, setUnidadMedidaIdSel] = useState<string | null>(null);
  const lookupDecimales = useDecimalesUnidad();

  // El refine de decimales del schema lee este ref en tiempo de validación
  // (ADR-0046 Etapa 2 PR-2c, advisory): así el resolver no se recrea al cambiar
  // de artículo. Se actualiza más abajo, una vez resueltos los decimales.
  const decimalesRef = useRef(DECIMALES_FALLBACK);
  const resolver = useMemo(
    () => zodResolver(crearLineaSchema(() => decimalesRef.current)),
    [],
  );

  const form = useForm<LineaValues>({
    resolver,
    defaultValues: esEditar
      ? buildValuesFromLinea(linea)
      : VALORES_INICIALES,
  });

  // Decimales de la unidad del artículo: por id (alta, preciso) o por código
  // (edición / heredado, matcheo del string); fallback global si no resuelve.
  const decimalesUnidad =
    lookupDecimales.porId(unidadMedidaIdSel) ??
    lookupDecimales.porCodigo(form.watch('unidadMedida')) ??
    DECIMALES_FALLBACK;
  decimalesRef.current = decimalesUnidad;

  // Re-valida la cantidad cuando cambian los decimales de la unidad resuelta.
  useEffect(() => {
    if (form.getValues('cantidad') != null) {
      void form.trigger('cantidad');
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [decimalesUnidad]);

  // Auto-foco al montar — en agregar al artículo (más común empezar
  // por ahí); en editar a la cantidad (lo más común que cambia).
  useEffect(() => {
    form.setFocus(esEditar ? 'cantidad' : 'articuloId');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Prellenado del CC-Máquina (Fase E PR2, Card 2): solo en AGREGAR. Si el
  // alcance del usuario resuelve a exactamente 1 máquina, se prellena; el watch
  // de centroCostoId re-aplica en cada línea nueva (incluido el reset tras
  // submit). El label acompaña al id para que el trigger muestre "clave —
  // nombre" y no el GUID. En edición no corre (la línea ya trae su valor).
  const { maquina: prellenadoCc } = useCcMaquinaPrellenado(!esEditar);
  const [prellenadoLabel, setPrellenadoLabel] = useState<string | undefined>();
  const centroCostoActual = form.watch('centroCostoId');
  useEffect(() => {
    if (esEditar || !prellenadoCc || centroCostoActual) return;
    form.setValue('centroCostoId', prellenadoCc.id);
    setPrellenadoLabel(
      formatCcMaquinaLabel({
        clave: prellenadoCc.clave,
        nombre: prellenadoCc.nombre,
      }),
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [prellenadoCc, centroCostoActual, esEditar]);

  const isPending = agregar.isPending || actualizar.isPending;

  function onError(error: Error) {
    if (esConflictoConcurrencia(error)) {
      conflictDialog.openSimple({
        traceId: error.traceId,
        onRefrescar: () =>
          queryClient.invalidateQueries({
            queryKey: comprasKeys.requisicion(requisicionId),
          }),
      });
      return;
    }
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
        description: error.traceId
          ? `Código: ${error.traceId}`
          : undefined,
      });
      return;
    }
    toast.error('Error inesperado al guardar la línea.');
  }

  function onSubmit(values: LineaValues) {
    if (esEditar && linea != null) {
      // PATCH estructural — no incluye notas (endpoint propio).
      const { notas: _ignored, ...estructural } = values;
      void _ignored;
      actualizar.mutate(
        {
          requisicionId,
          lineaId: linea.id,
          values: estructural,
        },
        {
          onSuccess: () => {
            toast.success('Línea actualizada');
            onSaved?.();
          },
          onError,
        },
      );
      return;
    }
    // Idempotency-Key por submit (no por montaje): este form es
    // multi-submit (resetea y queda abierto para agregar otra línea), así
    // que cada add necesita su propia key. Va en las variables del mutate,
    // nunca dentro del mutationFn (ADR-0020).
    const idempotencyKey = crypto.randomUUID();
    agregar.mutate(
      { requisicionId, values, idempotencyKey },
      {
        onSuccess: (resp) => {
          toast.success(`Línea ${resp.posicion} agregada`);
          // Reset y mantener el form abierto + foco para agregar otra.
          form.reset(VALORES_INICIALES);
          setUnidadMedidaIdSel(null);
          form.setFocus('articuloId');
          onSaved?.();
        },
        onError,
      },
    );
  }

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      onKeyDown={(e) => {
        // Esc en cualquier campo cancela el form (UX consistente con
        // dialogs). Ignora el Esc mientras hay submit en curso.
        if (e.key === 'Escape' && !isPending) {
          e.preventDefault();
          onCancel();
        }
      }}
      className={cn(
        'space-y-3 rounded-md border p-3',
        // Editar: borde sólido ámbar (warning sutil — "este registro
        // existente se está modificando"); agregar: borde dashed
        // primary para diferenciarlo claramente de la edición y de las
        // filas existentes.
        esEditar
          ? 'border-amber-400 bg-amber-50/40'
          : 'border-dashed border-primary/40 bg-primary/5',
      )}
      aria-label={esEditar ? `Editar línea ${linea?.posicion}` : 'Agregar línea'}
    >
      {/* ── Fila 1: campos críticos ─────────────────────────────── */}
      <div className="grid grid-cols-1 gap-2 md:grid-cols-12">
        <FieldInline
          label="Artículo"
          required
          error={form.formState.errors.articuloId?.message}
          className="md:col-span-4"
        >
          <Controller
            name="articuloId"
            control={form.control}
            render={({ field }) => (
              <ArticuloSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
                onSelect={(articulo) => {
                  form.setValue(
                    'unidadMedida',
                    articulo.unidadMedidaDefault || 'PZA',
                    { shouldValidate: true },
                  );
                  setUnidadMedidaIdSel(articulo.unidadMedidaId);
                }}
                // En edición, etiqueta inicial desde el DTO enriquecido para no
                // depender de la lista capada del typeahead (ADR-0042 addendum).
                initialLabel={
                  linea && (linea.articuloClave || linea.articuloNombre)
                    ? [linea.articuloClave, linea.articuloNombre]
                        .filter(Boolean)
                        .join(' · ')
                    : undefined
                }
              />
            )}
          />
        </FieldInline>

        <FieldInline
          label="Cantidad"
          required
          error={form.formState.errors.cantidad?.message}
          className="md:col-span-2"
        >
          <Controller
            name="cantidad"
            control={form.control}
            render={({ field }) => (
              <DecimalField
                value={field.value ?? null}
                onChange={(v) => field.onChange(v ?? 0)}
                min={0}
                step={stepParaDecimales(decimalesUnidad)}
              />
            )}
          />
        </FieldInline>

        <FieldInline
          label="Unidad"
          required
          error={form.formState.errors.unidadMedida?.message}
          className="md:col-span-1"
        >
          {/* Read-only (NO disabled): la UM se hereda del artículo. Con
              readOnly el valor sigue registrado y viaja en el submit;
              disabled lo excluiría del payload de react-hook-form. */}
          <Input
            type="text"
            readOnly
            tabIndex={-1}
            maxLength={20}
            placeholder="—"
            aria-label="Unidad de medida (heredada del artículo)"
            className="bg-muted/50"
            {...form.register('unidadMedida')}
          />
        </FieldInline>

        <FieldInline
          label="Precio"
          required
          error={
            form.formState.errors.precioEstimadoMonto?.message ??
            form.formState.errors.precioEstimadoMoneda?.message
          }
          className="md:col-span-3"
        >
          <Controller
            name="precioEstimadoMonto"
            control={form.control}
            render={({ field: montoField }) => (
              <Controller
                name="precioEstimadoMoneda"
                control={form.control}
                render={({ field: monedaField }) => (
                  <MoneyField
                    value={
                      montoField.value != null
                        ? {
                            amount: montoField.value,
                            currency: monedaField.value || 'MXN',
                          }
                        : null
                    }
                    onChange={(money) => {
                      montoField.onChange(money?.amount ?? 0);
                      monedaField.onChange(money?.currency ?? 'MXN');
                    }}
                  />
                )}
              />
            )}
          />
        </FieldInline>

        <FieldInline
          label="Fecha"
          error={form.formState.errors.fechaRequerida?.message}
          className="md:col-span-2"
        >
          <Controller
            name="fechaRequerida"
            control={form.control}
            render={({ field }) => (
              <DatePickerField
                value={field.value ?? null}
                onChange={(d) => field.onChange(d)}
                minDate={new Date()}
              />
            )}
          />
        </FieldInline>
      </div>

      {/* ── Sub-fila (siempre visible): CC-Máquina (Fase E PR2) ──────
          Sale de "Detalles" a primera clase: será obligatorio (PR2.1) y su
          combobox necesita ancho para "clave — nombre + área". El selector va
          por la ruta FILTRADA por alcance (/dim3/buscar); el display heredado
          se resuelve aparte por el read-port (detalle de RQ). ADR-0050. */}
      <div className="border-t border-primary/10 pt-2">
        <FieldInline
          label="CC-Máquina"
          error={form.formState.errors.centroCostoId?.message}
        >
          <Controller
            name="centroCostoId"
            control={form.control}
            render={({ field }) => (
              <Dim3Picker
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? null)}
                endpoint="/api/v1/centros-costo/dim3/buscar"
                // Etiqueta inicial del trigger sin abrir el picker: en edición,
                // del DTO enriquecido (read-port; "No catalogado" si el id no
                // resuelve); en agregar, la del prellenado (Card 2). Sin CC,
                // undefined → placeholder.
                initialLabel={
                  linea?.centroCostoId
                    ? formatCcMaquinaLabel({
                        clave: linea.centroCostoClave,
                        nombre: linea.centroCostoNombre,
                      })
                    : prellenadoLabel
                }
              />
            )}
          />
        </FieldInline>
      </div>

      {/* ── Toggle "+ Detalles" + actions ────────────────────────── */}
      <div className="flex flex-wrap items-center justify-between gap-2">
        <button
          type="button"
          onClick={() => setDetallesAbiertos((v) => !v)}
          className="inline-flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground"
          aria-expanded={detallesAbiertos}
        >
          {detallesAbiertos ? (
            <ChevronDown className="h-3 w-3" />
          ) : (
            <ChevronRight className="h-3 w-3" />
          )}
          Detalles (cuenta contable, proyecto, notas)
        </button>

        <div className="flex items-center gap-2">
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={onCancel}
            disabled={isPending}
          >
            <X className="mr-1 h-4 w-4" />
            Cancelar
          </Button>
          <Button type="submit" size="sm" disabled={isPending}>
            {esEditar ? (
              <>
                <Check className="mr-1 h-4 w-4" />
                {isPending ? 'Guardando…' : 'Guardar cambios'}
              </>
            ) : (
              <>
                <Plus className="mr-1 h-4 w-4" />
                {isPending ? 'Agregando…' : 'Agregar línea'}
              </>
            )}
          </Button>
        </div>
      </div>

      {/* ── Fila 2 (expandible): campos opcionales ───────────────── */}
      {detallesAbiertos && (
        <div className="grid grid-cols-1 gap-2 border-t border-primary/20 pt-3 md:grid-cols-3">
          <FieldInline
            label="Proyecto"
            error={form.formState.errors.proyecto?.message}
          >
            <Input
              type="text"
              placeholder="opcional"
              {...form.register('proyecto')}
            />
          </FieldInline>

          <FieldInline
            label="Cuenta contable"
            error={form.formState.errors.cuentaContableId?.message}
          >
            <Input
              type="text"
              placeholder="opcional"
              {...form.register('cuentaContableId')}
            />
          </FieldInline>

          <div className="md:col-span-3">
            <FieldInline
              label="Notas"
              error={form.formState.errors.notas?.message}
            >
              <Controller
                name="notas"
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
            </FieldInline>
          </div>
        </div>
      )}
    </form>
  );
}

interface FieldInlineProps {
  label: string;
  required?: boolean;
  error?: string;
  className?: string;
  children: React.ReactNode;
}

function FieldInline({
  label,
  required,
  error,
  className,
  children,
}: FieldInlineProps) {
  return (
    <div className={cn('space-y-1', className)}>
      <label className="flex items-center gap-1 text-xs font-medium text-muted-foreground">
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
