import { FormCampo } from '@/components/erp/forms/FormGrid';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { useEffect, type ReactNode } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { applyServerErrors, esApiError, codigoSoporte } from '@/lib/api';
import {
  ActualizarArticuloSchema,
  type ActualizarArticuloValues,
} from '@/modules/datos-maestros/schemas/articulo';
import { useActualizarArticulo, useProductoAw } from '@/modules/datos-maestros/api';
import { Naturaleza, type ArticuloDetalle } from '@/modules/datos-maestros/api/types';
import { UnidadMedidaSelect } from '@/modules/catalogos/components/UnidadMedidaSelect';
import { MonedaSelector } from '@/components/erp/selectors/MonedaSelector';
import { CategoriaSelector } from '@/components/erp/selectors/CategoriaSelector';
import { ProductoAwSelector } from '@/features/facturacion/components/selectors/ProductoAwSelector';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Form de datos del artículo (sección única, sin tabs). PATCH parcial:
 * <c>clave</c> read-only (inmutable). Mismo permiso de mutación que
 * proveedores: <c>compartido.catalogos.administrar</c>.
 *
 * <para>El cambio individual de <c>naturaleza</c> aquí es complementario
 * al endpoint bulk <c>/articulos/reclasificar-naturaleza</c> (F9-PR1) —
 * uno-a-uno cuando el admin solo necesita ajustar 1 artículo.</para>
 */
export interface ArticuloDatosFormProps {
  articulo: ArticuloDetalle;
}

export function ArticuloDatosForm({ articulo }: ArticuloDatosFormProps) {
  const canEditar = useHasPermission(PermisosCanonicos.CompartidoCatalogosAdministrar);
  const actualizar = useActualizarArticulo();
  // Etiqueta del producto ya ligado (el selector solo conoce el id).
  // El selector usa el lookup de facturación y el detalle de Productos A+W: sin ambos permisos se oculta.
  const puedeLookup = useHasPermission(PermisosCanonicos.FacturacionFacturasEmitir);
  const puedeProductosAw = useHasPermission(PermisosCanonicos.DatosMaestrosProductosAwGestionar);
  const puedeLigarProducto = puedeLookup && puedeProductosAw;
  const productoLigado = useProductoAw(puedeLigarProducto ? articulo.productoAwId : null);

  const form = useForm<ActualizarArticuloValues>({
    resolver: zodResolver(ActualizarArticuloSchema),
    defaultValues: buildDefaults(articulo),
  });

  useEffect(() => {
    form.reset(buildDefaults(articulo));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [articulo.id]);

  function onSubmit(values: ActualizarArticuloValues) {
    const descripcionVacia =
      values.descripcionLarga == null || values.descripcionLarga.length === 0;
    const monedaVacia =
      values.precioReferenciaMoneda == null || values.precioReferenciaMoneda.length === 0;
    const montoVacio = values.precioReferenciaMonto == null;
    // El backend tiene un solo flag para limpiar el par (monto, moneda).
    const limpiarPrecio = montoVacio && monedaVacia;

    actualizar.mutate(
      {
        id: articulo.id,
        payload: {
          nombre: values.nombre,
          unidadMedidaId: values.unidadMedidaId,
          naturaleza: values.naturaleza,
          descripcionLarga: descripcionVacia ? null : values.descripcionLarga,
          categoriaId: values.categoriaId,
          precioReferenciaMonto: limpiarPrecio ? null : values.precioReferenciaMonto,
          precioReferenciaMoneda: limpiarPrecio ? null : values.precioReferenciaMoneda,
          limpiarDescripcionLarga: descripcionVacia,
          // Solo limpiar si el artículo YA tenía categoría asignada y el usuario
          // la deseleccionó. Un artículo NO reconciliado (categoriaId null de
          // origen) NO se toca al guardar → conserva su string legacy.
          limpiarCategoria: articulo.categoriaId != null && values.categoriaId == null,
          limpiarPrecioReferencia: limpiarPrecio,
          // E07 D5: solo viaja si cambió el vínculo; null con vínculo previo = desligar.
          productoAwId:
            values.productoAwId != null && values.productoAwId !== articulo.productoAwId
              ? values.productoAwId
              : null,
          limpiarProductoAw: articulo.productoAwId != null && values.productoAwId == null,
        },
        // Key fresca por submit: este form es multi-submit (queda montado tras
        // guardar), así que una key estable daría 422 al 2º guardado con un body
        // distinto (ADR-0020; patrón AccionesOC/SheetNuevaOC).
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success('Artículo actualizado');
          form.reset(values);
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (
              applyServerErrors(form as unknown as Parameters<typeof applyServerErrors>[0], error)
            ) {
              return;
            }
            toast.error(error.problem.title, {
              description: codigoSoporte(error),
            });
            return;
          }
          toast.error('Error inesperado al actualizar el artículo.');
        },
      },
    );
  }

  const dirty = form.formState.isDirty;

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      className="  gap-4 rounded-md border bg-surface-card p-4"
    >
      <FormSeccion titulo="Datos del registro">
        <FormCampo span={6}>
          <FormRow label="Clave" hint="No editable.">
            <Input value={articulo.clave} readOnly aria-readonly className="font-mono" />
          </FormRow>
        </FormCampo>

        <FormCampo span={6}>
          <FormRow
            label="Unidad de medida"
            hint={
              articulo.unidadMedidaId == null
                ? `Legacy: «${articulo.unidadMedidaDefault}». Asigna una unidad del catálogo.`
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
        </FormCampo>

        <FormCampo span={12}>
          <div className="md:col-span-2">
            <FormRow label="Nombre" required error={form.formState.errors.nombre?.message}>
              <Input maxLength={254} disabled={!canEditar} {...form.register('nombre')} />
            </FormRow>
          </div>
        </FormCampo>

        <FormCampo span={6}>
          <FormRow
            label="Naturaleza"
            required
            error={form.formState.errors.naturaleza?.message}
            hint="Alimenta la matriz de aprobación de Compras."
          >
            <Controller
              name="naturaleza"
              control={form.control}
              render={({ field }) => (
                <Select
                  value={String(field.value)}
                  onValueChange={(v) => field.onChange(Number(v))}
                  disabled={!canEditar}
                >
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={String(Naturaleza.Estandar)}>Estándar</SelectItem>
                    <SelectItem value={String(Naturaleza.Servicio)}>Servicio</SelectItem>
                    <SelectItem value={String(Naturaleza.Critico)}>Crítico</SelectItem>
                    <SelectItem value={String(Naturaleza.Riesgo)}>Riesgo</SelectItem>
                  </SelectContent>
                </Select>
              )}
            />
          </FormRow>
        </FormCampo>

        <FormCampo span={6}>
          <FormRow
            label="Categoría"
            hint={
              articulo.categoriaId == null && articulo.categoria != null
                ? `Legacy: «${articulo.categoria}». Asigna una categoría del catálogo.`
                : 'Del catálogo de categorías.'
            }
            error={form.formState.errors.categoriaId?.message}
          >
            <Controller
              name="categoriaId"
              control={form.control}
              render={({ field }) => (
                <CategoriaSelector
                  value={field.value ?? null}
                  onChange={field.onChange}
                  initialLabel={articulo.categoria}
                  disabled={!canEditar}
                />
              )}
            />
          </FormRow>
        </FormCampo>

        {puedeLigarProducto && (
          <FormCampo span={12}>
            <FormRow
              label="Producto A+W"
              hint="Solo vidrio crudo por medida: liga el artículo a su familia en A+W (1:1). Vacío = insumo sin medidas."
              error={form.formState.errors.productoAwId?.message}
            >
              <Controller
                name="productoAwId"
                control={form.control}
                render={({ field }) => (
                  <ProductoAwSelector
                    value={field.value ?? null}
                    onChange={(item) => field.onChange(item?.id ?? null)}
                    initialLabel={
                      productoLigado.data != null
                        ? `${productoLigado.data.referenciaExterna} · ${productoLigado.data.descripcion}`
                        : undefined
                    }
                    placeholder="Sin producto A+W"
                    disabled={!canEditar}
                  />
                )}
              />
            </FormRow>
          </FormCampo>
        )}

        <FormCampo span={12}>
          <div className="md:col-span-2">
            <FormRow
              label="Descripción larga"
              hint="Opcional."
              error={form.formState.errors.descripcionLarga?.message}
            >
              <Controller
                name="descripcionLarga"
                control={form.control}
                render={({ field }) => (
                  <Textarea
                    rows={3}
                    disabled={!canEditar}
                    value={field.value ?? ''}
                    onChange={(e) =>
                      field.onChange(e.target.value.length > 0 ? e.target.value : null)
                    }
                  />
                )}
              />
            </FormRow>
          </div>
        </FormCampo>

        <FormCampo span={6}>
          <FormRow
            label="Precio referencia"
            hint="Opcional. Vacío = no aplica."
            error={form.formState.errors.precioReferenciaMonto?.message}
          >
            <Controller
              name="precioReferenciaMonto"
              control={form.control}
              render={({ field }) => (
                <Input
                  type="number"
                  step="0.01"
                  min={0}
                  disabled={!canEditar}
                  value={field.value ?? ''}
                  onChange={(e) =>
                    field.onChange(e.target.value === '' ? null : Number(e.target.value))
                  }
                />
              )}
            />
          </FormRow>
        </FormCampo>

        <FormCampo span={6}>
          <FormRow
            label="Moneda referencia"
            hint="Opcional. Del catálogo de monedas."
            error={form.formState.errors.precioReferenciaMoneda?.message}
          >
            <Controller
              name="precioReferenciaMoneda"
              control={form.control}
              render={({ field }) => (
                <MonedaSelector
                  value={field.value}
                  onChange={field.onChange}
                  disabled={!canEditar}
                />
              )}
            />
          </FormRow>
        </FormCampo>

        <FormCampo span={12}>
          {canEditar && (
            <div className="md:col-span-2 flex flex-wrap items-center justify-end gap-2 border-t pt-3">
              <Button
                type="button"
                variant="ghost"
                onClick={() => form.reset(buildDefaults(articulo))}
                disabled={!dirty || actualizar.isPending}
              >
                Descartar cambios
              </Button>
              <Button type="submit" disabled={!dirty || actualizar.isPending}>
                {actualizar.isPending ? 'Guardando…' : 'Guardar cambios'}
              </Button>
            </div>
          )}
        </FormCampo>
      </FormSeccion>
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
          <span aria-hidden="true" className="text-danger-fg">
            *
          </span>
        )}
      </label>
      {children}
      {hint != null && error == null && <p className="text-xs text-ink-muted">{hint}</p>}
      {error != null && (
        <p role="alert" className="text-xs text-danger-fg">
          {error}
        </p>
      )}
    </div>
  );
}

function buildDefaults(a: ArticuloDetalle): ActualizarArticuloValues {
  return {
    nombre: a.nombre,
    unidadMedidaId: a.unidadMedidaId,
    naturaleza: a.naturaleza,
    descripcionLarga: a.descripcionLarga ?? null,
    categoriaId: a.categoriaId ?? null,
    precioReferenciaMonto: a.precioReferenciaMonto ?? null,
    precioReferenciaMoneda: a.precioReferenciaMoneda ?? null,
    productoAwId: a.productoAwId ?? null,
  };
}
