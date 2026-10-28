import { LineaActivoFields } from '@/features/activos-fijos/components/LineaActivoFields';
import { FormCampo } from '@/components/erp/forms/FormGrid';
import { FormGrid } from '@/components/erp/forms/FormGrid';
/* eslint-disable react-hooks/incompatible-library -- react-hook-form watch alimenta validación dinámica; React Compiler omite su memoización de forma segura. */
import { useEffect, useMemo, useRef, useState } from 'react';
import { Controller, useForm, useWatch, type Control } from 'react-hook-form';
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
  Field,
  DECIMALES_FALLBACK,
  DepartamentoSelector,
  stepParaDecimales,
  TextAreaField,
  useDecimalesUnidad,
} from '@/components/erp';
import { applyServerErrors, codigoSoporte, esApiError } from '@/lib/api';
import { AvisoEnLinea } from '@/components/erp';
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
import { Naturaleza } from '@/features/compras/api/types';
import { useAgregarLineaManual } from '@/features/compras/ordenes/api/useAgregarLineaManual';
import { useActualizarLinea } from '@/features/compras/ordenes/api/useActualizarLinea';
import { useMedidasArticulo } from '@/features/compras/ordenes/api/useMedidasArticulo';
import { areaHojaDe, etiquetaMedidaMm, hojasDe } from '@/features/almacen/lib/medida-hoja';
import type { ActualizarLineaValues } from '@/features/compras/ordenes/schemas/actualizar-linea';
import { LineaDesdeRqBadge } from '@/features/compras/ordenes/components/LineaDesdeRqBadge';
import { CentroCostoPicker } from '@/features/centros-costo/components/CentroCostoPicker';
import { formatCcMaquinaLabel } from '@/features/centros-costo/lib/cc-maquina-label';
import { cn } from '@/lib/utils';
import { subtotalLinea } from '../lib/subtotal-linea';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';

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

function buildValuesFromLinea(linea: LineaOrdenCompraResponse): AgregarLineaManualValues {
  return {
    articuloId: linea.articuloId,
    cantidad: linea.cantidad,
    unidadMedida: linea.unidadMedida,
    productoAwVarianteId: linea.productoAwVarianteId ?? null,
    precioUnitario: linea.precioUnitario,
    departamentoSolicitanteId: linea.departamentoSolicitanteId,
    descuentoTipo: linea.descuentoTipo ?? null,
    descuentoValor: linea.descuentoValor ?? null,
    indicadorImpuestos: null,
    descripcionExtendida: linea.descripcionExtendida ?? null,
    fechaEntregaLinea: linea.fechaEntregaLinea
      ? new Date(linea.fechaEntregaLinea).toISOString()
      : null,
    textoAdicional: linea.textoAdicional ?? null,
    centroCostoId: linea.centroCostoId,
    fraccionArancelaria: linea.fraccionArancelaria ?? null,
    esActivoFijo: linea.esActivoFijo ?? false,
    claseActivoId: linea.claseActivoId ?? null,
  };
}

/**
 * Códigos de error del API ligados a la fracción arancelaria. Se muestran
 * junto al campo (además del toast) para que el usuario sepa qué corregir.
 */
const CODIGOS_ERROR_FRACCION = [
  'FRACCION_ARANCELARIA_INVALIDA',
  'LINEA_OC_FRACCION_INVALIDA',
  'LINEA_OC_FRACCION_SOLO_COMERCIO_EXTERIOR',
];

/** Errores del API de la medida de hoja → campo del form (hojas pinta el error de `cantidad`). */
const CODIGOS_ERROR_MEDIDA: Record<string, { campo: string }> = {
  VARIANTE_VIDRIO_REQUERIDA: { campo: 'productoAwVarianteId' },
  VARIANTE_NO_APLICA: { campo: 'productoAwVarianteId' },
  VARIANTE_NO_PERTENECE_AL_ARTICULO: { campo: 'productoAwVarianteId' },
  VARIANTE_SIN_MEDIDA: { campo: 'productoAwVarianteId' },
  HOJAS_ENTERAS: { campo: 'cantidad' },
  LINEA_OC_MEDIDA_INCOMPLETA: { campo: 'productoAwVarianteId' },
  LINEA_OC_VIDRIO_UNIDAD: { campo: 'cantidad' },
};

const m2DeHojas = (hojas: number | null, area: number | null) =>
  hojas != null && area != null ? Math.round(hojas * area * 10000) / 10000 : 0;

/**
 * Fracción arancelaria en el PATCH de línea: con valor → se fija; vacía con
 * valor previo → se limpia; sin valor previo ni captura → no se toca.
 */
function comandoFraccion(
  captura: string | null | undefined,
  previa: string | null,
): Pick<ActualizarLineaValues, 'fraccionArancelaria' | 'limpiarFraccionArancelaria'> {
  const valor = captura?.trim() ?? '';
  if (valor !== '') return { fraccionArancelaria: valor };
  if (previa != null) return { limpiarFraccionArancelaria: true };
  return {};
}

export function LineaInlineFormOc({ oc, linea, onCancel, onSaved }: LineaInlineFormOcProps) {
  const esEditar = linea != null;
  const lineaDesdeRq = linea?.requisicionId != null;
  const agregar = useAgregarLineaManual();
  const actualizar = useActualizarLinea();
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();
  const [detallesAbiertos, setDetallesAbiertos] = useState(false);
  // Un artículo de servicio no puede ser activo fijo.
  const [articuloServicio, setArticuloServicio] = useState(linea?.esServicio ?? false);

  const defaultValues: AgregarLineaManualValues = esEditar
    ? buildValuesFromLinea(linea)
    : DEFAULT_AGREGAR_LINEA_MANUAL;

  const [unidadMedidaIdSel, setUnidadMedidaIdSel] = useState<string | null>(null);
  const lookupDecimales = useDecimalesUnidad();
  const decimalesRef = useRef(DECIMALES_FALLBACK);
  // F1-MP-01 (E1): artículo ligado a A+W ⇒ medida + hojas enteras; los m² se derivan.
  const [hojas, setHojas] = useState<number | null>(
    linea?.productoAwVarianteId ? hojasDe(linea.cantidad, linea.areaHojaM2) : null,
  );
  // Fase E PR3.1: el Centro de costo es requerido en la línea MANUAL. En la
  // heredada de RQ el campo es read-only (viene 1:1 de la RQ) y el submit
  // manda null = "no tocar", así que exigirlo dejaría sin salida a las
  // heredadas legadas con CC null.
  const esVidrioRef = useRef(false);
  const resolver = useMemo(
    () =>
      zodResolver(
        crearAgregarLineaManualSchema(
          () => decimalesRef.current,
          !lineaDesdeRq,
          () => esVidrioRef.current,
        ),
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

  const articuloIdActual = form.watch('articuloId');
  const varianteIdActual = form.watch('productoAwVarianteId');
  const medidasQ = useMedidasArticulo(articuloIdActual);
  const medidas = medidasQ.data ?? [];
  // Solo las medidas del artículo ACTUAL deciden si es vidrio (al cambiar de artículo la línea vieja ya no cuenta).
  const esVidrio = medidas.length > 0;
  esVidrioRef.current = esVidrio;
  const medidaSel = medidas.find((m) => m.varianteId === varianteIdActual);
  const areaHoja =
    medidaSel?.areaHojaM2 ??
    areaHojaDe(medidaSel?.anchoMm, medidaSel?.altoMm) ??
    (varianteIdActual && varianteIdActual === linea?.productoAwVarianteId
      ? (linea.areaHojaM2 ?? null)
      : null);
  const cantidadVidrio = useWatch({ control: form.control, name: 'cantidad' });
  const conMedidaYHojas = !!varianteIdActual && hojas != null && hojas > 0;

  // Con medida la unidad es M2 (el dominio lo exige).
  useEffect(() => {
    if (esVidrio && form.getValues('unidadMedida') !== 'M2') {
      form.setValue('unidadMedida', 'M2', { shouldValidate: true });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [esVidrio, articuloIdActual]);

  function cambiarHojas(n: number | null, area: number | null) {
    setHojas(n);
    form.clearErrors('cantidad');
    form.setValue('cantidad', m2DeHojas(n, area), { shouldDirty: true });
  }

  /** Valida en cliente lo que el servidor también rechaza, con el mismo texto. */
  function medidaValida() {
    if (!esVidrio) return true;
    let ok = true;
    if (!varianteIdActual) {
      form.setError('productoAwVarianteId', { message: 'Elige la medida de la hoja.' });
      ok = false;
    }
    if (hojas == null || !Number.isInteger(hojas) || hojas < 1) {
      form.setError('cantidad', { message: 'Captura un número entero de hojas.' });
      ok = false;
    }
    return ok;
  }

  const isPending = agregar.isPending || actualizar.isPending;
  // Fracción arancelaria: solo al EDITAR una línea de OC de comercio exterior.
  // PLATFORM-TODO(<F1-COM-06>): supuesto Vilo.
  const mostrarFraccion = esEditar && oc.esImportacion;

  const precioActual = useWatch({ control: form.control, name: 'precioUnitario' });
  // D1: aviso (no bloqueo) si el precio de una línea que vino de la cotización elegida se modifica.
  const precioCotizado = oc.cotizacionElegida?.precios.find(
    (p) => p.lineaRequisicionId === linea?.lineaRequisicionId,
  )?.precioPorUnidadRequisicion;
  const apartaDeCotizacion =
    esEditar && precioCotizado != null && Math.abs((precioActual ?? 0) - precioCotizado) > 0.00005;

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
        applyServerErrors(form as unknown as Parameters<typeof applyServerErrors>[0], err, {
          shouldFocus: true,
          codigos: CODIGOS_ERROR_MEDIDA,
        })
      ) {
        return;
      }
      if (CODIGOS_ERROR_FRACCION.includes(err.code ?? '')) {
        form.setError('fraccionArancelaria', {
          type: err.code ?? 'FRACCION_ARANCELARIA',
          message: err.problem.detail ?? err.problem.title,
        });
      }
      const partes = [err.problem.detail, codigoSoporte(err)].filter((p): p is string =>
        Boolean(p),
      );
      toast.error(err.problem.title, {
        description: partes.length > 0 ? partes.join(' · ') : undefined,
      });
      return;
    }
    toast.error('Error inesperado al guardar la partida.');
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
            // Vidrio: medida y cantidad viajan juntas (también en líneas de RQ, cuya cantidad se ajusta a hojas enteras).
            cantidad: lineaDesdeRq && !esVidrio ? null : values.cantidad,
            unidadMedida: values.unidadMedida,
            ...(esVidrio ? { productoAwVarianteId: values.productoAwVarianteId } : {}),
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
            esActivoFijo: values.esActivoFijo ?? false,
            claseActivoId: values.esActivoFijo ? (values.claseActivoId ?? null) : null,
            // Solo en OC de comercio exterior: fuera de ella el API rechaza el campo.
            ...(oc.esImportacion
              ? comandoFraccion(values.fraccionArancelaria, linea.fraccionArancelaria)
              : {}),
          },
          idempotencyKey,
        },
        {
          onSuccess: () => {
            toast.success(`Partida ${linea.posicion} actualizada.`);
            onSaved?.();
          },
          onError,
        },
      );
      return;
    }
    agregar.mutate(
      {
        ordenCompraId: oc.id,
        command: { ...values, productoAwVarianteId: esVidrio ? values.productoAwVarianteId : null },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Partida ${resp.posicion} agregada.`);
          // Reset y mantener el form abierto + foco al artículo para
          // agregar otra (mismo UX que RQ).
          form.reset(DEFAULT_AGREGAR_LINEA_MANUAL);
          setUnidadMedidaIdSel(null);
          setHojas(null);
          form.setFocus('articuloId');
          onSaved?.();
        },
        onError,
      },
    );
  }

  return (
    <form
      onSubmit={(e) => {
        if (!medidaValida()) {
          e.preventDefault();
          return;
        }
        return form.handleSubmit(onSubmit, (errs) => {
          // Si el error cae en un campo del bloque colapsado, ábrelo para que sea visible.
          if (
            errs.descuentoTipo ||
            errs.descuentoValor ||
            errs.descripcionExtendida ||
            errs.textoAdicional
          )
            setDetallesAbiertos(true);
        })(e);
      }}
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
        esEditar ? 'border-warning bg-warning-bg/40' : 'border-dashed border-brand/40 bg-brand/5',
      )}
      aria-label={esEditar ? `Editar partida ${linea?.posicion}` : 'Agregar partida'}
      data-component="linea-inline-form-oc"
      data-modo={esEditar ? 'edit' : 'add'}
    >
      {/* Badge si la línea viene de RQ (solo en editar) */}
      {esEditar && lineaDesdeRq && linea?.requisicionId && (
        <div className="flex items-center gap-2 text-xs text-ink-muted">
          <LineaDesdeRqBadge requisicionId={linea.requisicionId} folio={linea.requisicionFolio} />
          <span>
            Artículo y cantidad provienen de la requisición y no se pueden modificar aquí. Sí puedes
            ajustar precio, descuento y datos no estructurales.
          </span>
        </div>
      )}

      {/* ── Fila 1: campos críticos ─────────────────────────────── */}
      <FormGrid className="">
        <FormCampo span={12}>
          <Field
            label="Artículo"
            required
            error={form.formState.errors.articuloId?.message}
            className="@lg:col-span-3 @3xl:col-span-4"
          >
            {(c) => (
              <Controller
                name="articuloId"
                control={form.control}
                render={({ field }) => (
                  <ArticuloSelector
                    triggerProps={c}
                    value={field.value || null}
                    onChange={(id) => {
                      // Otro artículo: la medida anterior ya no aplica.
                      if ((id ?? '') !== field.value) {
                        form.setValue('productoAwVarianteId', null);
                        form.setValue('cantidad', DEFAULT_AGREGAR_LINEA_MANUAL.cantidad);
                        setHojas(null);
                      }
                      field.onChange(id ?? '');
                    }}
                    onSelect={(articulo) => {
                      form.setValue('unidadMedida', articulo.unidadMedidaDefault || 'PZA', {
                        shouldValidate: true,
                      });
                      setUnidadMedidaIdSel(articulo.unidadMedidaId);
                      const srv = articulo.naturaleza === Naturaleza.Servicio;
                      setArticuloServicio(srv);
                      if (srv) {
                        form.setValue('esActivoFijo', false, { shouldDirty: true });
                        form.setValue('claseActivoId', null, { shouldDirty: true });
                      }
                    }}
                    disabled={lineaDesdeRq}
                    // En edición, etiqueta inicial desde el DTO enriquecido para no
                    // depender de la lista capada del typeahead (ADR-0042 addendum).
                    initialLabel={
                      linea && (linea.articuloClave || linea.articuloNombre)
                        ? [linea.articuloClave, linea.articuloNombre].filter(Boolean).join(' · ')
                        : undefined
                    }
                  />
                )}
              />
            )}
          </Field>
        </FormCampo>

        {esVidrio && (
          <>
            <FormCampo span={12}>
              <Field
                label="Medida"
                required
                error={form.formState.errors.productoAwVarianteId?.message}
                className="@3xl:col-span-3"
              >
                {(c) => (
                  <Select
                    value={varianteIdActual ?? ''}
                    onValueChange={(id) => {
                      form.clearErrors('productoAwVarianteId');
                      form.setValue('productoAwVarianteId', id, { shouldDirty: true });
                      const m = medidas.find((x) => x.varianteId === id);
                      cambiarHojas(hojas, m?.areaHojaM2 ?? areaHojaDe(m?.anchoMm, m?.altoMm));
                    }}
                  >
                    <SelectTrigger data-field="medida-hoja" {...c}>
                      <SelectValue
                        placeholder={medidasQ.isLoading ? 'Cargando medidas…' : 'Elige la medida'}
                      />
                    </SelectTrigger>
                    <SelectContent>
                      {medidas.map((m) => (
                        <SelectItem
                          key={m.varianteId}
                          value={m.varianteId}
                          disabled={m.areaHojaM2 == null}
                        >
                          {etiquetaMedidaMm(m.anchoMm, m.altoMm) ?? m.claveVariante}
                          {m.areaHojaM2 == null ? ' (sin medida en A+W)' : ''}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              </Field>
            </FormCampo>
            <FormCampo span={12}>
              <Field
                label="Hojas"
                required
                error={form.formState.errors.cantidad?.message}
                className="@3xl:col-span-1"
              >
                {(c) => (
                  <Input
                    {...c}
                    type="number"
                    inputMode="numeric"
                    min={1}
                    step={1}
                    data-field="hojas"
                    value={hojas ?? ''}
                    onChange={(e) =>
                      cambiarHojas(e.target.value === '' ? null : Number(e.target.value), areaHoja)
                    }
                  />
                )}
              </Field>
            </FormCampo>
          </>
        )}

        <FormCampo span={12}>
          <Field
            label={esVidrio ? 'Cantidad (m²)' : 'Cantidad'}
            required
            error={esVidrio ? undefined : form.formState.errors.cantidad?.message}
            className="@3xl:col-span-2"
          >
            {(c) =>
              esVidrio ? (
                <output
                  id={c.id}
                  data-field="cantidad-m2"
                  className="block rounded-md border bg-surface-muted/50 px-3 py-1.5 text-sm tabular-nums"
                >
                  {conMedidaYHojas ? (cantidadVidrio ?? 0).toFixed(4) : '—'}
                </output>
              ) : (
                <Controller
                  name="cantidad"
                  control={form.control}
                  render={({ field }) => (
                    <DecimalField
                      inputProps={c}
                      value={field.value ?? null}
                      onChange={(v) => field.onChange(v ?? 0)}
                      min={0.001}
                      step={stepParaDecimales(decimalesUnidad)}
                      disabled={lineaDesdeRq}
                    />
                  )}
                />
              )
            }
          </Field>
        </FormCampo>

        <FormCampo span={4}>
          <Field
            label="UM"
            required
            error={form.formState.errors.unidadMedida?.message}
            className="@3xl:col-span-1"
          >
            {/* Read-only (NO disabled): la UM se hereda del artículo. Con
              readOnly el valor sigue registrado y viaja en el submit;
              disabled lo excluiría del payload de react-hook-form. En
              líneas heredadas de RQ el selector está disabled, así que la
              UM queda fija con la unidad guardada en la RQ. */}
            {(c) => (
              <Input
                {...c}
                type="text"
                readOnly
                tabIndex={-1}
                maxLength={20}
                placeholder="—"
                aria-label="Unidad de medida (heredada del artículo)"
                className="bg-surface-muted/50"
                {...form.register('unidadMedida')}
              />
            )}
          </Field>
        </FormCampo>

        <FormCampo span={12}>
          <Field
            label="Precio"
            required
            error={form.formState.errors.precioUnitario?.message}
            className="@3xl:col-span-3"
          >
            {(c) => (
              <Controller
                name="precioUnitario"
                control={form.control}
                render={({ field }) => (
                  <DecimalField
                    inputProps={c}
                    value={field.value ?? null}
                    onChange={(v) => field.onChange(v ?? 0)}
                    min={0}
                    step={0.0001}
                  />
                )}
              />
            )}
          </Field>
        </FormCampo>

        <FormCampo span={12}>
          <Field label="Subtotal" className="@3xl:col-span-2">
            {(c) => <SubtotalPreview
                id={c.id}
                control={form.control}
                moneda={oc.moneda}
                oculto={esVidrio && !conMedidaYHojas}
              />}
          </Field>
        </FormCampo>
      </FormGrid>

      {medidasQ.isError && (
        <AvisoEnLinea tono="advertencia">
          No se pudieron cargar las medidas del artículo.
        </AvisoEnLinea>
      )}

      {apartaDeCotizacion && (
        <AvisoEnLinea tono="advertencia">
          Se aparta de la cotización elegida (precio cotizado: {precioCotizado}).
        </AvisoEnLinea>
      )}

      {/* ── Sub-fila (siempre visible): Centro de costo (Fase E PR3) ──────
          Heredada de RQ → display BLOQUEADO read-only (ADR-0050 "heredado,
          solo lectura", 1:1 de la RQ); manual → picker ABIERTO por proxy
          (el comprador elige, /compras/ordenes/dim3/buscar). */}
      <FormGrid className="border-t border-brand/10 pt-2">
        <FormCampo span={6}>
          <Field
            label="Centro de costo"
            required={!lineaDesdeRq}
            error={form.formState.errors.centroCostoId?.message}
          >
            {(c) =>
              lineaDesdeRq ? (
                // Bloqueado: mismo patrón que la UM heredada (<Input readOnly>).
                <Input
                  id={c.id}
                  type="text"
                  readOnly
                  tabIndex={-1}
                  aria-label="Centro de costo (heredado de la requisición, no editable)"
                  className="bg-surface-muted/50"
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
                    <CentroCostoPicker
                      triggerProps={c}
                      value={field.value || null}
                      onChange={(id) => field.onChange(id ?? null)}
                      endpoint="/api/v1/compras/ordenes/centros-costo/buscar"
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
              )
            }
          </Field>
        </FormCampo>

        <FormCampo span={6}>
          <Field
            label="Departamento solicitante"
            required
            error={form.formState.errors.departamentoSolicitanteId?.message}
          >
            {(c) => (
              <Controller
                name="departamentoSolicitanteId"
                control={form.control}
                render={({ field }) => (
                  <DepartamentoSelector
                    triggerProps={c}
                    value={field.value || null}
                    onChange={(v) => field.onChange(v ?? '')}
                  />
                )}
              />
            )}
          </Field>
        </FormCampo>

        <FormCampo span={6}>
          {mostrarFraccion && (
            <Field
              label="Fracción arancelaria"
              hint="Clave de la tarifa arancelaria de la mercancía (8 u 10 dígitos)."
              error={form.formState.errors.fraccionArancelaria?.message}
            >
              {(c) => (
                <Input
                  {...c}
                  type="text"
                  inputMode="numeric"
                  maxLength={10}
                  placeholder="8 u 10 dígitos"
                  data-field="fraccion-arancelaria"
                  {...form.register('fraccionArancelaria')}
                />
              )}
            </Field>
          )}
        </FormCampo>
      </FormGrid>

      {!articuloServicio && (
        <LineaActivoFields
          // Con recepciones el backend rechaza cambiar el flag.
          disabled={esEditar && (linea?.cantidadRecibida ?? 0) > 0}
          error={form.formState.errors.claseActivoId?.message}
          activo={form.watch('esActivoFijo') ?? false}
          clase={form.watch('claseActivoId')}
          onChange={(activo, clase) => {
            form.setValue('esActivoFijo', activo, { shouldDirty: true });
            form.setValue('claseActivoId', clase, { shouldDirty: true });
          }}
        />
      )}

      {/* ── Toggle "+ Detalles" + actions ────────────────────────── */}
      <div className="flex flex-wrap items-center justify-between gap-2">
        <button
          type="button"
          onClick={() => setDetallesAbiertos((v) => !v)}
          className="inline-flex items-center gap-1 text-xs text-ink-muted hover:text-ink"
          aria-expanded={detallesAbiertos}
        >
          {detallesAbiertos ? (
            <ChevronDown className="h-3 w-3" />
          ) : (
            <ChevronRight className="h-3 w-3" />
          )}
          Detalles (descuento, descripción, texto adicional)
        </button>

        <div className="flex items-center gap-2">
          <Button type="button" variant="ghost" size="sm" onClick={onCancel} disabled={isPending}>
            <X className="mr-1 h-4 w-4" />
            Cancelar
          </Button>
          <Button type="submit" size="sm" disabled={isPending} data-action="submit">
            {esEditar ? (
              <>
                <Check className="mr-1 h-4 w-4" />
                {isPending ? 'Guardando…' : 'Guardar cambios'}
              </>
            ) : (
              <>
                <Plus className="mr-1 h-4 w-4" />
                {isPending ? 'Agregando…' : 'Agregar partida'}
              </>
            )}
          </Button>
        </div>
      </div>

      {/* ── Fila 2 (expandible): campos opcionales ───────────────── */}
      {detallesAbiertos && (
        <FormGrid className="border-t border-brand/20 pt-3">
          <FormCampo span={6}>
            <Field
              label="Descuento (opcional)"
              error={form.formState.errors.descuentoTipo?.message}
            >
              {(c) => (
                <Controller
                  name="descuentoTipo"
                  control={form.control}
                  render={({ field }) => (
                    <Select
                      value={field.value == null ? '__none__' : String(field.value)}
                      onValueChange={(v) => field.onChange(v === '__none__' ? null : Number(v))}
                    >
                      <SelectTrigger data-field="descuento-tipo" {...c}>
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="__none__">Sin descuento</SelectItem>
                        <SelectItem value={String(DescuentoTipo.Porcentaje)}>
                          Porcentaje (%)
                        </SelectItem>
                        <SelectItem value={String(DescuentoTipo.Monto)}>Monto fijo</SelectItem>
                      </SelectContent>
                    </Select>
                  )}
                />
              )}
            </Field>
          </FormCampo>

          <FormCampo span={6}>
            <Field
              label="Valor del descuento"
              error={form.formState.errors.descuentoValor?.message}
            >
              {(c) => (
                <Controller
                  name="descuentoValor"
                  control={form.control}
                  render={({ field }) => (
                    <DecimalField
                      inputProps={c}
                      value={field.value ?? null}
                      onChange={(v) => field.onChange(v)}
                      min={0}
                      step={0.01}
                      disabled={descuentoTipoActual == null}
                    />
                  )}
                />
              )}
            </Field>
          </FormCampo>

          <FormCampo span={12}>
            <div className="@lg:col-span-2">
              <Field
                label="Descripción extendida"
                error={form.formState.errors.descripcionExtendida?.message}
              >
                {(c) => (
                  <Controller
                    name="descripcionExtendida"
                    control={form.control}
                    render={({ field }) => (
                      <TextAreaField
                        textareaProps={c}
                        value={field.value ?? null}
                        onChange={(v) => field.onChange(v || null)}
                        maxLength={1000}
                        minRows={2}
                      />
                    )}
                  />
                )}
              </Field>
            </div>
          </FormCampo>

          <FormCampo span={12}>
            <div className="@lg:col-span-2">
              <Field
                label="Texto adicional (impreso en PDF)"
                error={form.formState.errors.textoAdicional?.message}
              >
                {(c) => (
                  <Controller
                    name="textoAdicional"
                    control={form.control}
                    render={({ field }) => (
                      <TextAreaField
                        textareaProps={c}
                        value={field.value ?? null}
                        onChange={(v) => field.onChange(v || null)}
                        maxLength={2000}
                        minRows={2}
                      />
                    )}
                  />
                )}
              </Field>
            </div>
          </FormCampo>
        </FormGrid>
      )}
    </form>
  );
}

// ============================================================================
// Helpers locales
// ============================================================================

/**
 * Vista previa con descuento y redondeo al par, igual que el dominio.
 */
function SubtotalPreview({
  control,
  moneda,
  id,
  oculto,
}: {
  id: string;
  oculto: boolean;
  control: Control<AgregarLineaManualValues>;
  moneda: string;
}) {
  const cantidad = useWatch({ control, name: 'cantidad' }) ?? 0;
  const precio = useWatch({ control, name: 'precioUnitario' }) ?? 0;
  const tipo = useWatch({ control, name: 'descuentoTipo' });
  const valor = useWatch({ control, name: 'descuentoValor' });
  const subtotal = subtotalLinea(cantidad, precio, tipo, valor);
  return (
    <output
      id={id}
      className="block rounded-md border bg-surface-muted/30 px-3 py-1.5 text-sm tabular-nums"
    >
      {oculto ? '—' : <MoneyDisplay amount={subtotal} currency={moneda} />}
    </output>
  );
}
