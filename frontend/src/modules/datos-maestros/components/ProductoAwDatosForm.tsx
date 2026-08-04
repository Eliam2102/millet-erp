import { useEffect, type ReactNode } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { applyServerErrors, esApiError } from '@/lib/api';
import { ClaveSatSelector } from '@/components/erp/selectors/ClaveSatSelector';
import {
  ActualizarProductoAwSchema,
  type ActualizarProductoAwValues,
} from '@/modules/datos-maestros/schemas/producto-aw';
import { useActualizarProductoAw } from '@/modules/datos-maestros/api';
import type { ProductoAwDetalle } from '@/modules/datos-maestros/api/types';
import { UnidadMedidaSelect } from '@/modules/catalogos/components/UnidadMedidaSelect';
import { CategoriaSelector } from '@/components/erp/selectors/CategoriaSelector';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Form de datos del producto A+W (sección única del detalle, sin
 * tabs). PATCH parcial (ADR-0048): <c>referenciaExterna</c> read-only
 * (correlación con A+W, inmutable). Es la vía para COMPLETAR las
 * claves SAT (prod/serv y unidad) de productos auto-provisionados
 * antes de timbrar.
 *
 * <para>Semántica del PATCH: las claves SAT y el objeto de impuesto NO
 * tienen flag <c>limpiarX</c> en backend — vacío = sin cambio (solo se
 * reemplazan). Las tasas sí: vacío = limpiar.</para>
 */
export interface ProductoAwDatosFormProps {
  producto: ProductoAwDetalle;
}

/** Fallback local de c_ObjetoImp si el PAC no responde (modo 503). */
const OBJETO_IMP_FALLBACK = [
  { codigo: '01', descripcion: 'No objeto de impuesto' },
  { codigo: '02', descripcion: 'Sí objeto de impuesto' },
  { codigo: '03', descripcion: 'Sí objeto, no obligado al desglose' },
];

export function ProductoAwDatosForm({ producto }: ProductoAwDatosFormProps) {
  const canEditar = useHasPermission(
    PermisosCanonicos.DatosMaestrosProductosAwGestionar,
  );
  const actualizar = useActualizarProductoAw();

  const form = useForm<ActualizarProductoAwValues>({
    resolver: zodResolver(ActualizarProductoAwSchema),
    defaultValues: buildDefaults(producto),
  });

  // Si cambia el producto cargado (otra row), resincronizar.
  useEffect(() => {
    form.reset(buildDefaults(producto));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [producto.id]);

  function onSubmit(values: ActualizarProductoAwValues) {
    const claveProdServVacia =
      values.claveProdServSat == null || values.claveProdServSat.length === 0;
    const claveUnidadVacia =
      values.claveUnidadSat == null || values.claveUnidadSat.length === 0;
    const ivaVacio = values.tasaIvaTraslado == null;
    const retIvaVacia = values.tasaRetencionIva == null;
    const retIsrVacia = values.tasaRetencionIsr == null;
    const fraccionVacia =
      values.fraccionArancelaria == null ||
      values.fraccionArancelaria.length === 0;
    const unidadAduanaVacia =
      values.unidadAduana == null || values.unidadAduana.length === 0;
    const pesoVacio = values.pesoUnitarioKg == null;

    actualizar.mutate(
      {
        id: producto.id,
        payload: {
          descripcion: values.descripcion,
          unidadMedidaId: values.unidadMedidaId,
          categoriaId: values.categoriaId,
          // Claves SAT y objetoImp: null = sin cambio (no hay limpiarX).
          claveProdServSat: claveProdServVacia
            ? null
            : values.claveProdServSat,
          claveUnidadSat: claveUnidadVacia ? null : values.claveUnidadSat,
          objetoImp: values.objetoImp ?? null,
          tasaIvaTraslado: ivaVacio ? null : values.tasaIvaTraslado,
          tasaRetencionIva: retIvaVacia ? null : values.tasaRetencionIva,
          tasaRetencionIsr: retIsrVacia ? null : values.tasaRetencionIsr,
          // Solo limpiar si el producto YA tenía categoría y el usuario la
          // deseleccionó (mismo criterio que ArticuloDatosForm).
          limpiarCategoria:
            producto.categoriaId != null && values.categoriaId == null,
          limpiarTasaIvaTraslado: ivaVacio,
          limpiarTasaRetencionIva: retIvaVacia,
          limpiarTasaRetencionIsr: retIsrVacia,
          // Datos de aduana (CCE): vacío = limpiar (mismo criterio que tasas).
          fraccionArancelaria: fraccionVacia
            ? null
            : values.fraccionArancelaria,
          unidadAduana: unidadAduanaVacia ? null : values.unidadAduana,
          pesoUnitarioKg: pesoVacio ? null : values.pesoUnitarioKg,
          limpiarFraccionArancelaria: fraccionVacia,
          limpiarUnidadAduana: unidadAduanaVacia,
          limpiarPesoUnitarioKg: pesoVacio,
        },
        // Key fresca por submit: este form es multi-submit (queda montado tras
        // guardar), así que una key estable daría 422 al 2º guardado con un body
        // distinto (ADR-0020; patrón AccionesOC/SheetNuevaOC).
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success('Producto A+W actualizado');
          form.reset(values);
        },
        onError: (error) => {
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
          toast.error('Error inesperado al actualizar el producto A+W.');
        },
      },
    );
  }

  const dirty = form.formState.isDirty;

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      className="grid grid-cols-1 gap-4 rounded-md border bg-card p-4 md:grid-cols-2 max-w-3xl"
    >
      <FormRow
        label="Referencia externa"
        hint="Correlación con A+W. No editable."
      >
        <Input
          value={producto.referenciaExterna}
          readOnly
          aria-readonly
          className="font-mono"
        />
      </FormRow>

      <FormRow
        label="Unidad de medida"
        hint={
          producto.unidadMedidaId == null
            ? `De A+W: «${producto.unidadMedida}». Asigna una unidad del catálogo.`
            : 'Del catálogo de unidades.'
        }
        error={form.formState.errors.unidadMedidaId?.message}
      >
        <Controller
          name="unidadMedidaId"
          control={form.control}
          render={({ field }) => (
            <UnidadMedidaSelect
              value={field.value ?? null}
              onChange={field.onChange}
              disabled={!canEditar}
              permitirVacio
            />
          )}
        />
      </FormRow>

      <div className="md:col-span-2">
        <FormRow
          label="Descripción"
          required
          error={form.formState.errors.descripcion?.message}
        >
          <Input
            maxLength={254}
            disabled={!canEditar}
            {...form.register('descripcion')}
          />
        </FormRow>
      </div>

      <FormRow
        label="Categoría"
        hint="Opcional. Del catálogo de categorías."
        error={form.formState.errors.categoriaId?.message}
      >
        <Controller
          name="categoriaId"
          control={form.control}
          render={({ field }) => (
            <CategoriaSelector
              value={field.value ?? null}
              onChange={field.onChange}
              disabled={!canEditar}
            />
          )}
        />
      </FormRow>

      <FormRow
        label="Objeto de impuesto"
        hint="Clave SAT c_ObjetoImp (catálogo en vivo). Vacío = sin cambio."
        error={form.formState.errors.objetoImp?.message}
      >
        <Controller
          name="objetoImp"
          control={form.control}
          render={({ field }) => (
            <ClaveSatSelector
              catalogo="objeto-imp"
              value={field.value ?? null}
              onChange={(item) => field.onChange(item?.codigo ?? null)}
              disabled={!canEditar}
              placeholder="— sin asignar —"
              fallbackItems={OBJETO_IMP_FALLBACK}
            />
          )}
        />
      </FormRow>

      <FormRow
        label="Clave prod/serv SAT"
        hint="c_ClaveProdServ (catálogo SAT en vivo). Requerida para timbrar. Vacío = sin cambio."
        error={form.formState.errors.claveProdServSat?.message}
      >
        <Controller
          name="claveProdServSat"
          control={form.control}
          render={({ field }) => (
            <ClaveSatSelector
              catalogo="clave-prod-serv"
              value={field.value ?? null}
              onChange={(item) => field.onChange(item?.codigo ?? null)}
              disabled={!canEditar}
              placeholder="Buscar por código o descripción…"
            />
          )}
        />
      </FormRow>

      <FormRow
        label="Clave unidad SAT"
        hint="c_ClaveUnidad (catálogo SAT en vivo, ej. H87, MTK). Requerida para timbrar. Vacío = sin cambio."
        error={form.formState.errors.claveUnidadSat?.message}
      >
        <Controller
          name="claveUnidadSat"
          control={form.control}
          render={({ field }) => (
            <ClaveSatSelector
              catalogo="clave-unidad"
              value={field.value ?? null}
              onChange={(item) => field.onChange(item?.codigo ?? null)}
              disabled={!canEditar}
              placeholder="Buscar por código o descripción…"
            />
          )}
        />
      </FormRow>

      <FormRow
        label="Tasa IVA traslado"
        hint="Fracción 0–1 (ej. 0.16). Vacío = sin tasa."
        error={form.formState.errors.tasaIvaTraslado?.message}
      >
        <Controller
          name="tasaIvaTraslado"
          control={form.control}
          render={({ field }) => (
            <Input
              type="number"
              step="0.01"
              min={0}
              max={1}
              disabled={!canEditar}
              value={field.value ?? ''}
              onChange={(e) =>
                field.onChange(
                  e.target.value === '' ? null : Number(e.target.value),
                )
              }
            />
          )}
        />
      </FormRow>

      <FormRow
        label="Tasa retención IVA"
        hint="Fracción 0–1. Vacío = sin retención."
        error={form.formState.errors.tasaRetencionIva?.message}
      >
        <Controller
          name="tasaRetencionIva"
          control={form.control}
          render={({ field }) => (
            <Input
              type="number"
              step="0.01"
              min={0}
              max={1}
              disabled={!canEditar}
              value={field.value ?? ''}
              onChange={(e) =>
                field.onChange(
                  e.target.value === '' ? null : Number(e.target.value),
                )
              }
            />
          )}
        />
      </FormRow>

      <FormRow
        label="Tasa retención ISR"
        hint="Fracción 0–1. Vacío = sin retención."
        error={form.formState.errors.tasaRetencionIsr?.message}
      >
        <Controller
          name="tasaRetencionIsr"
          control={form.control}
          render={({ field }) => (
            <Input
              type="number"
              step="0.01"
              min={0}
              max={1}
              disabled={!canEditar}
              value={field.value ?? ''}
              onChange={(e) =>
                field.onChange(
                  e.target.value === '' ? null : Number(e.target.value),
                )
              }
            />
          )}
        />
      </FormRow>

      <div className="md:col-span-2 border-t pt-3 text-sm font-medium text-muted-foreground">
        Datos de aduana (Comercio Exterior)
        <p className="text-xs font-normal">
          Prellenan la línea de una factura de exportación (CCE). Fuente A+W
          cuando esté disponible; si no, captúralos aquí.
        </p>
      </div>

      <FormRow
        label="Fracción arancelaria"
        hint="c_FraccionArancelaria (8–10 dígitos). Vacío = limpiar."
        error={form.formState.errors.fraccionArancelaria?.message}
      >
        <Controller
          name="fraccionArancelaria"
          control={form.control}
          render={({ field }) => (
            <ClaveSatSelector
              catalogo="fraccion-arancelaria"
              value={field.value ?? null}
              initialLabel={field.value ?? null}
              onChange={(item) => field.onChange(item?.codigo ?? null)}
              disabled={!canEditar}
              placeholder="Buscar fracción…"
            />
          )}
        />
      </FormRow>

      <FormRow
        label="Unidad aduanera"
        hint="c_UnidadAduana (ej. 06 = kg). Corresponde a la UMT de la fracción. Vacío = limpiar."
        error={form.formState.errors.unidadAduana?.message}
      >
        <Controller
          name="unidadAduana"
          control={form.control}
          render={({ field }) => (
            <ClaveSatSelector
              catalogo="unidad-aduana"
              value={field.value ?? null}
              initialLabel={field.value ?? null}
              onChange={(item) => field.onChange(item?.codigo ?? null)}
              disabled={!canEditar}
              placeholder="Buscar unidad aduanera…"
            />
          )}
        />
      </FormRow>

      <FormRow
        label="Peso unitario (kg)"
        hint="Base para la cantidad aduanera. Vacío = limpiar."
        error={form.formState.errors.pesoUnitarioKg?.message}
      >
        <Controller
          name="pesoUnitarioKg"
          control={form.control}
          render={({ field }) => (
            <Input
              type="number"
              step="0.000001"
              min={0}
              disabled={!canEditar}
              value={field.value ?? ''}
              onChange={(e) =>
                field.onChange(
                  e.target.value === '' ? null : Number(e.target.value),
                )
              }
            />
          )}
        />
      </FormRow>

      {canEditar && (
        <div className="md:col-span-2 flex flex-wrap items-center justify-end gap-2 border-t pt-3">
          <Button
            type="button"
            variant="ghost"
            onClick={() => form.reset(buildDefaults(producto))}
            disabled={!dirty || actualizar.isPending}
          >
            Descartar cambios
          </Button>
          <Button type="submit" disabled={!dirty || actualizar.isPending}>
            {actualizar.isPending ? 'Guardando…' : 'Guardar cambios'}
          </Button>
        </div>
      )}
    </form>
  );
}

interface FormRowProps {
  label: string;
  required?: boolean;
  hint?: string;
  error?: string;
  children: ReactNode;
}

function FormRow({ label, required, hint, error, children }: FormRowProps) {
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
      {hint != null && error == null && (
        <p className="text-xs text-muted-foreground">{hint}</p>
      )}
      {error != null && (
        <p role="alert" className="text-xs text-rose-600">
          {error}
        </p>
      )}
    </div>
  );
}

function buildDefaults(p: ProductoAwDetalle): ActualizarProductoAwValues {
  return {
    descripcion: p.descripcion,
    unidadMedidaId: p.unidadMedidaId ?? null,
    categoriaId: p.categoriaId ?? null,
    claveProdServSat: p.claveProdServSat ?? null,
    claveUnidadSat: p.claveUnidadSat ?? null,
    objetoImp: /^0[1-8]$/.test(p.objetoImp ?? '') ? p.objetoImp : null,
    tasaIvaTraslado: p.tasaIvaTraslado ?? null,
    tasaRetencionIva: p.tasaRetencionIva ?? null,
    tasaRetencionIsr: p.tasaRetencionIsr ?? null,
    fraccionArancelaria: p.fraccionArancelaria ?? null,
    unidadAduana: p.unidadAduana ?? null,
    pesoUnitarioKg: p.pesoUnitarioKg ?? null,
  };
}
