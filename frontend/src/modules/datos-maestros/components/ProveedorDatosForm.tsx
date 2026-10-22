import { FormCampo } from '@/components/erp/forms/FormGrid';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { useEffect, type ReactNode } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { UppercaseInput } from '@/components/erp/forms/UppercaseInput';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { applyServerErrors, esApiError, codigoSoporte } from '@/lib/api';
import {
  ActualizarProveedorSchema,
  type ActualizarProveedorValues,
} from '@/modules/datos-maestros/schemas/proveedor';
import { useActualizarProveedor } from '@/modules/datos-maestros/api';
import {
  OrigenProveedor,
  TipoPersonaProveedor,
  type ProveedorDetalle,
} from '@/modules/datos-maestros/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Form de datos del proveedor (sección única del detalle, sin tabs).
 * PATCH parcial: la <c>clave</c> es read-only — el backend la marca
 * inmutable (natural-key del catálogo cross-empresa).
 *
 * <para>Sin permiso <c>compartido.catalogos.administrar</c> el form
 * queda read-only sin botón Guardar. El submit usa la convención
 * <c>limpiarX</c> de la API: nullable vacío + flag = setear a null.</para>
 */
export interface ProveedorDatosFormProps {
  proveedor: ProveedorDetalle;
}

export function ProveedorDatosForm({ proveedor }: ProveedorDatosFormProps) {
  const canEditar = useHasPermission(PermisosCanonicos.CompartidoCatalogosAdministrar);
  const actualizar = useActualizarProveedor();

  const form = useForm<ActualizarProveedorValues>({
    resolver: zodResolver(ActualizarProveedorSchema),
    defaultValues: buildDefaults(proveedor),
  });

  // Si cambia el proveedor cargado (otra row), resincronizar.
  useEffect(() => {
    form.reset(buildDefaults(proveedor));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [proveedor.id]);

  function onSubmit(values: ActualizarProveedorValues) {
    const nombreComercialVacio =
      values.nombreComercial == null || values.nombreComercial.length === 0;
    const emailVacio = values.email == null || values.email.length === 0;
    const telefonoVacio = values.telefono == null || values.telefono.length === 0;
    const condicionesVacio = values.condicionesPagoDias == null;

    actualizar.mutate(
      {
        id: proveedor.id,
        payload: {
          razonSocial: values.razonSocial,
          calle: values.calle,
          numeroExterior: values.numeroExterior,
          numeroInterior: values.numeroInterior,
          colonia: values.colonia,
          ciudad: values.ciudad,
          municipio: values.municipio,
          estado: values.estado,
          codigoPostal: values.codigoPostal,
          pais: values.pais,
          taxId: extranjero ? values.taxId : undefined,

          rfc: values.rfc,
          tipoPersona: values.tipoPersona,
          nombreComercial: nombreComercialVacio ? null : values.nombreComercial,
          email: emailVacio ? null : values.email,
          telefono: telefonoVacio ? null : values.telefono,
          condicionesPagoDias: condicionesVacio ? null : values.condicionesPagoDias,
          limpiarNombreComercial: nombreComercialVacio,
          limpiarEmail: emailVacio,
          limpiarTelefono: telefonoVacio,
          limpiarCondicionesPago: condicionesVacio,
        },
        // Key fresca por submit: este form es multi-submit (queda montado tras
        // guardar), así que una key estable daría 422 al 2º guardado con un body
        // distinto (ADR-0020; patrón AccionesOC/SheetNuevaOC).
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success('Proveedor actualizado');
          form.reset(values);
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'PROVEEDOR_RFC_DUPLICADO') {
              form.setError('rfc', {
                type: error.code,
                message: error.problem.detail ?? 'Ya existe un proveedor con ese RFC.',
              });
              return;
            }
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
          toast.error('Error inesperado al actualizar el proveedor.');
        },
      },
    );
  }

  const dirty = form.formState.isDirty;
  const extranjero = proveedor.origen === OrigenProveedor.Extranjero;

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      className="  gap-4 rounded-md border bg-surface-card p-4  max-w-3xl"
    >
      <FormSeccion titulo="Datos del registro">
        <FormCampo span={6}>
          <FormRow label="Clave" hint="No editable.">
            <Input value={proveedor.clave} readOnly aria-readonly className="font-mono" />
          </FormRow>
        </FormCampo>

        <FormCampo span={6}>
          <FormRow
            label="RFC"
            required={!extranjero}
            hint={
              extranjero
                ? 'Opcional en proveedores extranjeros: vacío = RFC genérico XEXX010101000.'
                : undefined
            }
            error={form.formState.errors.rfc?.message}
          >
            <UppercaseInput
              maxLength={13}
              disabled={!canEditar}
              className="font-mono"
              {...form.register('rfc')}
            />
          </FormRow>
        </FormCampo>

        <FormCampo span={12}>
          <div className="md:col-span-2">
            <FormRow
              label="Razón social"
              required
              error={form.formState.errors.razonSocial?.message}
            >
              <Input maxLength={254} disabled={!canEditar} {...form.register('razonSocial')} />
            </FormRow>
          </div>
        </FormCampo>

        <FormCampo span={6}>
          <FormRow label="Tipo persona" required error={form.formState.errors.tipoPersona?.message}>
            <Controller
              name="tipoPersona"
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
                    <SelectItem value={String(TipoPersonaProveedor.Moral)}>Moral</SelectItem>
                    <SelectItem value={String(TipoPersonaProveedor.Fisica)}>Física</SelectItem>
                  </SelectContent>
                </Select>
              )}
            />
          </FormRow>
        </FormCampo>

        <FormCampo span={6}>
          <FormRow
            label="Condiciones de pago (días)"
            hint="Vacío = no aplica."
            error={form.formState.errors.condicionesPagoDias?.message}
          >
            <Controller
              name="condicionesPagoDias"
              control={form.control}
              render={({ field }) => (
                <Input
                  type="number"
                  min={0}
                  max={365}
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

        <FormCampo span={12}>
          <div className="md:col-span-2">
            <FormRow
              label="Nombre comercial"
              hint="Opcional."
              error={form.formState.errors.nombreComercial?.message}
            >
              <Controller
                name="nombreComercial"
                control={form.control}
                render={({ field }) => (
                  <Input
                    maxLength={254}
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
          <FormRow label="Email" hint="Opcional." error={form.formState.errors.email?.message}>
            <Controller
              name="email"
              control={form.control}
              render={({ field }) => (
                <Input
                  type="email"
                  maxLength={254}
                  disabled={!canEditar}
                  value={field.value ?? ''}
                  onChange={(e) =>
                    field.onChange(e.target.value.length > 0 ? e.target.value : null)
                  }
                />
              )}
            />
          </FormRow>
        </FormCampo>

        <FormCampo span={6}>
          <FormRow
            label="Teléfono"
            hint="Opcional."
            error={form.formState.errors.telefono?.message}
          >
            <Controller
              name="telefono"
              control={form.control}
              render={({ field }) => (
                <Input
                  maxLength={50}
                  disabled={!canEditar}
                  value={field.value ?? ''}
                  onChange={(e) =>
                    field.onChange(e.target.value.length > 0 ? e.target.value : null)
                  }
                />
              )}
            />
          </FormRow>
        </FormCampo>

        <FormCampo span={12}>
          <h3 className="text-sm font-semibold">Domicilio</h3>
        </FormCampo>
        <FormCampo span={6}>
          <label htmlFor="proveedor-calle" className="text-xs font-medium text-ink-strong">
            Calle
          </label>
          <Input
            id="proveedor-calle"
            maxLength={254}
            disabled={!canEditar}
            {...form.register('calle')}
          />
          {form.formState.errors.calle?.message && (
            <p role="alert" className="text-xs text-danger-fg">
              {form.formState.errors.calle?.message}
            </p>
          )}
        </FormCampo>
        <FormCampo span={6}>
          <label htmlFor="proveedor-numeroExterior" className="text-xs font-medium text-ink-strong">
            Número exterior
          </label>
          <Input
            id="proveedor-numeroExterior"
            maxLength={20}
            disabled={!canEditar}
            {...form.register('numeroExterior')}
          />
          {form.formState.errors.numeroExterior?.message && (
            <p role="alert" className="text-xs text-danger-fg">
              {form.formState.errors.numeroExterior?.message}
            </p>
          )}
        </FormCampo>
        <FormCampo span={6}>
          <label htmlFor="proveedor-numeroInterior" className="text-xs font-medium text-ink-strong">
            Número interior
          </label>
          <Input
            id="proveedor-numeroInterior"
            maxLength={20}
            disabled={!canEditar}
            {...form.register('numeroInterior')}
          />
          {form.formState.errors.numeroInterior?.message && (
            <p role="alert" className="text-xs text-danger-fg">
              {form.formState.errors.numeroInterior?.message}
            </p>
          )}
        </FormCampo>
        <FormCampo span={6}>
          <label htmlFor="proveedor-colonia" className="text-xs font-medium text-ink-strong">
            Colonia
          </label>
          <Input
            id="proveedor-colonia"
            maxLength={254}
            disabled={!canEditar}
            {...form.register('colonia')}
          />
          {form.formState.errors.colonia?.message && (
            <p role="alert" className="text-xs text-danger-fg">
              {form.formState.errors.colonia?.message}
            </p>
          )}
        </FormCampo>
        <FormCampo span={6}>
          <label htmlFor="proveedor-ciudad" className="text-xs font-medium text-ink-strong">
            Ciudad
          </label>
          <Input
            id="proveedor-ciudad"
            maxLength={100}
            disabled={!canEditar}
            {...form.register('ciudad')}
          />
          {form.formState.errors.ciudad?.message && (
            <p role="alert" className="text-xs text-danger-fg">
              {form.formState.errors.ciudad?.message}
            </p>
          )}
        </FormCampo>
        <FormCampo span={6}>
          <label htmlFor="proveedor-municipio" className="text-xs font-medium text-ink-strong">
            Municipio
          </label>
          <Input
            id="proveedor-municipio"
            maxLength={100}
            disabled={!canEditar}
            {...form.register('municipio')}
          />
          {form.formState.errors.municipio?.message && (
            <p role="alert" className="text-xs text-danger-fg">
              {form.formState.errors.municipio?.message}
            </p>
          )}
        </FormCampo>
        <FormCampo span={6}>
          <label htmlFor="proveedor-estado" className="text-xs font-medium text-ink-strong">
            Estado
          </label>
          <Input
            id="proveedor-estado"
            maxLength={100}
            disabled={!canEditar}
            {...form.register('estado')}
          />
          {form.formState.errors.estado?.message && (
            <p role="alert" className="text-xs text-danger-fg">
              {form.formState.errors.estado?.message}
            </p>
          )}
        </FormCampo>
        <FormCampo span={6}>
          <label htmlFor="proveedor-codigoPostal" className="text-xs font-medium text-ink-strong">
            Código postal
          </label>
          <Input
            id="proveedor-codigoPostal"
            maxLength={20}
            disabled={!canEditar}
            {...form.register('codigoPostal')}
          />
          {form.formState.errors.codigoPostal?.message && (
            <p role="alert" className="text-xs text-danger-fg">
              {form.formState.errors.codigoPostal?.message}
            </p>
          )}
        </FormCampo>
        <FormCampo span={6}>
          <label htmlFor="proveedor-pais" className="text-xs font-medium text-ink-strong">
            País (ISO de tres letras)
          </label>
          <Input
            id="proveedor-pais"
            maxLength={3}
            disabled={!canEditar}
            {...form.register('pais')}
          />
          {form.formState.errors.pais?.message && (
            <p role="alert" className="text-xs text-danger-fg">
              {form.formState.errors.pais?.message}
            </p>
          )}
        </FormCampo>
        {extranjero && (
          <FormCampo span={6}>
            <label htmlFor="proveedor-taxId" className="text-xs font-medium text-ink-strong">
              Tax ID (identificación fiscal extranjera)
            </label>
            <Input
              id="proveedor-taxId"
              maxLength={40}
              disabled={!canEditar}
              {...form.register('taxId')}
            />
            {form.formState.errors.taxId?.message && (
              <p role="alert" className="text-xs text-danger-fg">
                {form.formState.errors.taxId?.message}
              </p>
            )}
            <p className="text-xs text-ink-muted">Opcional. Aparece en la orden de compra</p>
          </FormCampo>
        )}
        <FormCampo span={12}>
          {canEditar && (
            <div className="md:col-span-2 flex flex-wrap items-center justify-end gap-2 border-t pt-3">
              <Button
                type="button"
                variant="ghost"
                onClick={() => form.reset(buildDefaults(proveedor))}
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

function buildDefaults(p: ProveedorDetalle): ActualizarProveedorValues {
  return {
    razonSocial: p.razonSocial,
    calle: p.calle ?? '',
    numeroExterior: p.numeroExterior ?? '',
    numeroInterior: p.numeroInterior ?? '',
    colonia: p.colonia ?? '',
    ciudad: p.ciudad ?? '',
    municipio: p.municipio ?? '',
    estado: p.estado ?? '',
    codigoPostal: p.codigoPostal ?? '',
    pais: p.pais ?? (p.origen === OrigenProveedor.Nacional ? 'MEX' : ''),
    taxId: p.taxId ?? '',

    rfc: p.rfc,
    origen: p.origen ?? null,
    tipoPersona: p.tipoPersona,
    nombreComercial: p.nombreComercial ?? null,
    email: p.email ?? null,
    telefono: p.telefono ?? null,
    condicionesPagoDias: p.condicionesPagoDias ?? null,
  };
}
