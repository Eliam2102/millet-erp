import { useEffect, useMemo, useRef, useState } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Check, ChevronDown, ChevronRight, Plus, X } from 'lucide-react';
import { toast } from 'sonner';
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
  DecimalField,
  DECIMALES_FALLBACK,
  DepartamentoSelector,
  stepParaDecimales,
  TextAreaField,
  useDecimalesUnidad,
} from '@/components/erp';
import {
  applyServerErrors,
  esApiError,
} from '@/lib/api';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { useQueryClient } from '@tanstack/react-query';
import { handleOcMutationError } from '@/features/compras/ordenes/lib/handle-conflict';
import {
  crearAgregarLineaManualSchema,
  DEFAULT_AGREGAR_LINEA_MANUAL,
  type AgregarLineaManualValues,
} from '@/features/compras/ordenes/schemas/agregar-linea-manual';
import {
  DescuentoTipo,
  type LineaOrdenCompraResponse,
  type OrdenCompraDetalleResponse,
} from '@/features/compras/ordenes/api/types';
import { useAgregarLineaManual } from '@/features/compras/ordenes/api/useAgregarLineaManual';
import { useActualizarLinea } from '@/features/compras/ordenes/api/useActualizarLinea';
import { LineaDesdeRqBadge } from '@/features/compras/ordenes/components/LineaDesdeRqBadge';
import { Dim3Picker } from '@/features/centros-costo/components/Dim3Picker';
import { formatCcMaquinaLabel } from '@/features/centros-costo/lib/cc-maquina-label';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;LineaInlineFormOc/&gt;</c> — form expandible inline (sin modal)
 * para AGREGAR o EDITAR líneas de OC. Mismo patrón que el
 * <c>LineaInlineForm</c> de RQ — la doc cross-módulo
 * <c>frontend/docs/patrones-compras.md §6</c> establece este como el
 * exemplar para CRUD de items dentro de un master.
 *
 * <para><b>Modo agregar</b> (sin <c>linea</c>): el form se mantiene
 * abierto entre submits — al guardar, los campos se resetean y el cursor
 * vuelve al artículo. Cancelar colapsa.</para>
 *
 * <para><b>Modo editar</b> (con <c>linea</c>): el form arranca con los
 * valores de la línea y aparece reemplazando la fila correspondiente
 * (border ámbar). Submit la actualiza vía PATCH y llama
 * <c>onSaved</c>.</para>
 *
 * <para><b>Línea heredada de RQ</b>
 * (<c>linea.requisicionId != null</c>): bloquea <c>articuloId</c> y
 * <c>cantidad</c> por la política §4.2 — la RQ es la fuente de verdad
 * de qué se pidió. El badge <c>&lt;LineaDesdeRqBadge/&gt;</c> aparece
 * en el header del form para que el usuario sepa por qué los campos
 * están disabled.</para>
 *
 * <para><b>Layout</b>: 5 campos críticos en grid (artículo, cantidad,
 * UM, precio unitario, fecha entrega). Botón "+ Detalles" expande una
 * segunda fila con los opcionales (almacén, departamento, descuento,
 * descripción extendida, texto adicional).</para>
 */
export interface LineaInlineFormOcProps {
  oc: OrdenCompraDetalleResponse;
  /** Si se provee, modo EDITAR con valores precargados. */
  linea?: LineaOrdenCompraResponse | null;
  /** Callback al cancelar (botón Cancelar / Esc). */
  onCancel: () => void;
  /** Callback tras submit exitoso. Modo agregar: el form se resetea para
   * agregar otra; modo editar: el parent debe colapsar. */
  onSaved?: () => void;
}

function buildValuesFromLinea(
  linea: LineaOrdenCompraResponse,
): AgregarLineaManualValues {
  return {
    articuloId: linea.articuloId,
    cantidad: linea.cantidad,
    unidadMedida: linea.unidadMedida,
    precioUnitario: linea.precioUnitario,
    departamentoSolicitanteId: linea.departamentoSolicitanteId,
    descuentoTipo: null,
    descuentoValor: null,
    indicadorImpuestos: null,
    descripcionExtendida: linea.descripcionExtendida ?? null,
    fechaEntregaLinea: linea.fechaEntregaLinea
      ? new Date(linea.fechaEntregaLinea).toISOString()
      : null,
    textoAdicional: linea.textoAdicional ?? null,
    centroCostoId: linea.centroCostoId,
  };
}

export function LineaInlineFormOc({
  oc,
  linea,
  onCancel,
  onSaved,
}: LineaInlineFormOcProps) {
  const esEditar = linea != null;
  const lineaDesdeRq = linea?.requisicionId != null;
  const agregar = useAgregarLineaManual();
  const actualizar = useActualizarLinea();
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();
  const [detallesAbiertos, setDetallesAbiertos] = useState(false);

  const defaultValues: AgregarLineaManualValues = esEditar
    ? buildValuesFromLinea(linea)
    : DEFAULT_AGREGAR_LINEA_MANUAL;

  const [unidadMedidaIdSel, setUnidadMedidaIdSel] = useState<string | null>(null);
  const lookupDecimales = useDecimalesUnidad();
  const decimalesRef = useRef(DECIMALES_FALLBACK);
  // Fase E PR3.1: el CC-Máquina es requerido en la línea MANUAL. En la
  // heredada de RQ el campo es read-only (viene 1:1 de la RQ) y el submit
  // manda null = "no tocar", así que exigirlo dejaría sin salida a las
  // heredadas legadas con CC null.
  const resolver = useMemo(
    () =>
      zodResolver(
        crearAgregarLineaManualSchema(() => decimalesRef.current, !lineaDesdeRq),
      ),
    [lineaDesdeRq],
  );

  const form = useForm<AgregarLineaManualValues>({
    resolver,
    defaultValues,
    mode: 'onBlur',
  });

  // Decimales de la unidad del artículo (ADR-0046 Etapa 2 PR-2c, advisory):
  // por id (alta) o por código (edición/heredado); fallback global si no resuelve.
  const decimalesUnidad =
    lookupDecimales.porId(unidadMedidaIdSel) ??
    lookupDecimales.porCodigo(form.watch('unidadMedida')) ??
    DECIMALES_FALLBACK;
  decimalesRef.current = decimalesUnidad;

  useEffect(() => {
    if (form.getValues('cantidad') != null) {
      void form.trigger('cantidad');
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [decimalesUnidad]);

  // Auto-foco al montar (igual que RQ): agregar → artículo, editar →
  // cantidad. En líneas heredadas de RQ, articulo y cantidad están
  // disabled, así que enfocamos precio en su lugar.
  useEffect(() => {
    if (esEditar && lineaDesdeRq) {
      form.setFocus('precioUnitario');
    } else {
      form.setFocus(esEditar ? 'cantidad' : 'articuloId');
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const isPending = agregar.isPending || actualizar.isPending;

  const descuentoTipoActual = useWatch({
    control: form.control,
    name: 'descuentoTipo',
  });

  function onError(err: unknown) {
    // 409 → ConflictDialog en modo simple (refresh + revisar). Cierra
    // el form inline para que el dialog se vea sin overlap visual.
    if (
      handleOcMutationError(err, {
        ordenCompraId: oc.id,
        conflictDialog,
        queryClient,
      })
    ) {
      onCancel();
      return;
    }
    if (esApiError(err)) {
      if (
        applyServerErrors(
          form as unknown as Parameters<typeof applyServerErrors>[0],
          err,
        )
      ) {
        return;
      }
      toast.error(err.problem.title, {
        description: err.traceId ? `Código: ${err.traceId}` : undefined,
      });
      return;
    }
    toast.error('Error inesperado al guardar la línea.');
  }

  function onSubmit(values: AgregarLineaManualValues) {
    // Idempotency-Key por submit (no por montaje): form multi-submit;
    // ambas ramas (editar/agregar) la consumen. En las variables del
    // mutate, nunca dentro del mutationFn (ADR-0020).
    const idempotencyKey = crypto.randomUUID();
    if (esEditar && linea != null) {
      actualizar.mutate(
        {
          ordenCompraId: oc.id,
          lineaId: linea.id,
          command: {
            articuloId: lineaDesdeRq ? null : values.articuloId,
            cantidad: lineaDesdeRq ? null : values.cantidad,
            unidadMedida: values.unidadMedida,
            precioUnitario: values.precioUnitario,
            departamentoSolicitanteId: values.departamentoSolicitanteId,
            descripcionExtendida: values.descripcionExtendida,
            fechaEntregaLinea: values.fechaEntregaLinea,
            descuentoTipo: values.descuentoTipo,
            descuentoValor: values.descuentoValor,
            indicadorImpuestos: values.indicadorImpuestos,
            // Heredada → null (no se toca; el backend lo rechazaría). Manual →
            // el CC elegido. Fase E PR3.
            centroCostoId: lineaDesdeRq ? null : (values.centroCostoId ?? null),
          },
          idempotencyKey,
        },
        {
          onSuccess: () => {
            toast.success(`Línea ${linea.posicion} actualizada.`);
            onSaved?.();
          },
          onError,
        },
      );
      return;
    }
    agregar.mutate(
      { ordenCompraId: oc.id, command: values, idempotencyKey },
      {
        onSuccess: (resp) => {
          toast.success(`Línea ${resp.posicion} agregada.`);
          // Reset y mantener el form abierto + foco al artículo para
          // agregar otra (mismo UX que RQ).
          form.reset(DEFAULT_AGREGAR_LINEA_MANUAL);
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
        if (e.key === 'Escape' && !isPending) {
          e.preventDefault();
          onCancel();
        }
      }}
      className={cn(
        'space-y-3 rounded-md border p-3',
        // Editar: borde sólido ámbar; agregar: borde dashed primary.
        esEditar
          ? 'border-amber-400 bg-amber-50/40'
          : 'border-dashed border-primary/40 bg-primary/5',
      )}
      aria-label={
        esEditar ? `Editar línea ${linea?.posicion}` : 'Agregar línea'
      }
      data-component="linea-inline-form-oc"
      data-modo={esEditar ? 'edit' : 'add'}
    >
      {/* Badge si la línea viene de RQ (solo en editar) */}
      {esEditar && lineaDesdeRq && linea?.requisicionId && (
        <div className="flex items-center gap-2 text-xs text-muted-foreground">
          <LineaDesdeRqBadge
            requisicionId={linea.requisicionId}
            folio={linea.requisicionFolio}
          />
          <span>
            Artículo y cantidad están bloqueados por política §4.2 — la RQ
            es la fuente de verdad. Sí puedes ajustar precio, descuento y
            datos no estructurales.
          </span>
        </div>
      )}

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
                disabled={lineaDesdeRq}
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
                min={0.001}
                step={stepParaDecimales(decimalesUnidad)}
                disabled={lineaDesdeRq}
              />
            )}
          />
        </FieldInline>

        <FieldInline
          label="UM"
          required
          error={form.formState.errors.unidadMedida?.message}
          className="md:col-span-1"
        >
          {/* Read-only (NO disabled): la UM se hereda del artículo. Con
              readOnly el valor sigue registrado y viaja en el submit;
              disabled lo excluiría del payload de react-hook-form. En
              líneas heredadas de RQ el selector está disabled, así que la
              UM queda fija con la unidad guardada en la RQ. */}
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
          error={form.formState.errors.precioUnitario?.message}
          className="md:col-span-3"
        >
          <Controller
            name="precioUnitario"
            control={form.control}
            render={({ field }) => (
              <DecimalField
                value={field.value ?? null}
                onChange={(v) => field.onChange(v ?? 0)}
                min={0}
                step={0.0001}
              />
            )}
          />
        </FieldInline>

        <FieldInline
          label="Subtotal"
          className="md:col-span-2"
        >
          <SubtotalPreview control={form.control} />
        </FieldInline>
      </div>

      {/* ── Sub-fila (siempre visible): CC-Máquina (Fase E PR3) ──────
          Heredada de RQ → display BLOQUEADO read-only (ADR-0050 "heredado,
          solo lectura", 1:1 de la RQ); manual → picker ABIERTO por proxy
          (el comprador elige, /compras/ordenes/dim3/buscar). */}
      <div className="border-t border-primary/10 pt-2">
        <FieldInline
          label="CC-Máquina"
          error={form.formState.errors.centroCostoId?.message}
        >
          {lineaDesdeRq ? (
            // Bloqueado: mismo patrón que la UM heredada (<Input readOnly>).
            <Input
              type="text"
              readOnly
              tabIndex={-1}
              aria-label="CC-Máquina (heredado de la requisición, no editable)"
              className="bg-muted/50"
              value={
                linea?.centroCostoId
                  ? formatCcMaquinaLabel({
                      clave: linea.centroCostoClave,
                      nombre: linea.centroCostoNombre,
                    })
                  : '—'
              }
            />
          ) : (
            <Controller
              name="centroCostoId"
              control={form.control}
              render={({ field }) => (
                <Dim3Picker
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? null)}
                  endpoint="/api/v1/compras/ordenes/dim3/buscar"
                  initialLabel={
                    linea?.centroCostoId
                      ? formatCcMaquinaLabel({
                          clave: linea.centroCostoClave,
                          nombre: linea.centroCostoNombre,
                        })
                      : undefined
                  }
                />
              )}
            />
          )}
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
          Detalles (almacén, departamento, descuento, texto adicional)
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
          <Button
            type="submit"
            size="sm"
            disabled={isPending}
            data-action="submit"
          >
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
        <div className="grid grid-cols-1 gap-2 border-t border-primary/20 pt-3 md:grid-cols-2">
          <FieldInline
            label="Departamento solicitante"
            required
            error={form.formState.errors.departamentoSolicitanteId?.message}
          >
            <Controller
              name="departamentoSolicitanteId"
              control={form.control}
              render={({ field }) => (
                <DepartamentoSelector
                  value={field.value || null}
                  onChange={(v) => field.onChange(v ?? '')}
                />
              )}
            />
          </FieldInline>

          <FieldInline
            label="Descuento (opcional)"
            error={form.formState.errors.descuentoTipo?.message}
          >
            <Controller
              name="descuentoTipo"
              control={form.control}
              render={({ field }) => (
                <Select
                  value={
                    field.value == null ? '__none__' : String(field.value)
                  }
                  onValueChange={(v) =>
                    field.onChange(v === '__none__' ? null : Number(v))
                  }
                >
                  <SelectTrigger data-field="descuento-tipo">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="__none__">Sin descuento</SelectItem>
                    <SelectItem value={String(DescuentoTipo.Porcentaje)}>
                      Porcentaje (%)
                    </SelectItem>
                    <SelectItem value={String(DescuentoTipo.Monto)}>
                      Monto fijo
                    </SelectItem>
                  </SelectContent>
                </Select>
              )}
            />
          </FieldInline>

          <FieldInline
            label="Valor del descuento"
            error={form.formState.errors.descuentoValor?.message}
          >
            <Controller
              name="descuentoValor"
              control={form.control}
              render={({ field }) => (
                <DecimalField
                  value={field.value ?? null}
                  onChange={(v) => field.onChange(v)}
                  min={0}
                  step={0.01}
                  disabled={descuentoTipoActual == null}
                />
              )}
            />
          </FieldInline>

          <div className="md:col-span-2">
            <FieldInline
              label="Descripción extendida"
              error={form.formState.errors.descripcionExtendida?.message}
            >
              <Controller
                name="descripcionExtendida"
                control={form.control}
                render={({ field }) => (
                  <TextAreaField
                    value={field.value ?? null}
                    onChange={(v) => field.onChange(v || null)}
                    maxLength={1000}
                    minRows={2}
                  />
                )}
              />
            </FieldInline>
          </div>

          <div className="md:col-span-2">
            <FieldInline
              label="Texto adicional (impreso en PDF)"
              error={form.formState.errors.textoAdicional?.message}
            >
              <Controller
                name="textoAdicional"
                control={form.control}
                render={({ field }) => (
                  <TextAreaField
                    value={field.value ?? null}
                    onChange={(v) => field.onChange(v || null)}
                    maxLength={2000}
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

// ============================================================================
// Helpers locales
// ============================================================================

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

/**
 * <c>&lt;SubtotalPreview/&gt;</c> — calcula <c>cantidad * precio</c>
 * client-side para preview en el form. Tras el submit, el backend
 * recalcula con descuento + redondeo banker's.
 */
function SubtotalPreview({
  control,
}: {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  control: any;
}) {
  const cantidad = useWatch({ control, name: 'cantidad' }) ?? 0;
  const precio = useWatch({ control, name: 'precioUnitario' }) ?? 0;
  const subtotal = (Number(cantidad) || 0) * (Number(precio) || 0);
  return (
    <div className="rounded-md border bg-muted/30 px-3 py-1.5 text-sm tabular-nums">
      {subtotal.toFixed(2)}
    </div>
  );
}
