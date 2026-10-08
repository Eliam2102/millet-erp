import { useEffect, useRef } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { RegimenFiscalSelector } from '@/components/erp/selectors/RegimenFiscalSelector';
import { applyServerErrors, esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import {
  ActualizarEmpresaSchema,
  type ActualizarEmpresaValues,
} from '@/modules/administracion/schemas/empresa';
import { useActualizarEmpresa } from '@/modules/administracion/api';
import type { EmpresaResponse } from '@/modules/administracion/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { hoyLocalISO } from '@/lib/datetime';
import { useImpuestosReferencia } from '@/modules/catalogos/api';

/**
 * Formulario fiscal compartido por el detalle legado y Mi empresa.
 * PATCH parcial: el RFC es read-only — el backend no permite cambiarlo
 * una vez creada la empresa (es el natural-key del dominio fiscal).
 *
 * <para>Sin permiso <c>admin.empresas.editar</c> el form se renderiza
 * read-only con los inputs deshabilitados; sin botón Guardar.</para>
 */
export interface EmpresaDatosFormProps {
  empresa: EmpresaResponse;
}

export function EmpresaDatosForm({ empresa }: EmpresaDatosFormProps) {
  const canEditar = useHasPermission(PermisosCanonicos.AdminEmpresasEditar);
  const canLeerImpuestos = useHasPermission(PermisosCanonicos.CompartidoCatalogosLeer);
  const impuestos = useImpuestosReferencia(hoyLocalISO(), false, canLeerImpuestos);
  const tasasIva = (impuestos.data ?? []).filter(
    (i) => i.clave === '002' && i.tipo === 'Traslado' && i.factor === 'Tasa',
  );
  const keyFor = useBodyScopedIdempotencyKey();
  const actualizar = useActualizarEmpresa();
  const versionEdicion = useRef(empresa.version);

  const form = useForm<ActualizarEmpresaValues>({
    resolver: zodResolver(ActualizarEmpresaSchema),
    defaultValues: buildDefaults(empresa),
  });

  // Una recarga en segundo plano no debe reemplazar una edición ni su versión.
  useEffect(() => {
    if (form.formState.isDirty) return;
    versionEdicion.current = empresa.version;
    form.reset(buildDefaults(empresa));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [empresa.id, empresa.version]);

  function onSubmit(values: ActualizarEmpresaValues) {
    if (!canEditar || actualizar.isPending) return;
    // Si el usuario borra el nombre comercial, lo enviamos como
    // <c>limpiarNombreComercial: true</c> (el PATCH backend distingue
    // "no se mandó" de "explícitamente vacío").
    const nombreComercialVacio =
      values.nombreComercial == null || values.nombreComercial.length === 0;
    const tasaIvaVacia = values.tasaIvaDefault == null;

    actualizar.mutate(
      {
        id: empresa.id,
        version: versionEdicion.current,
        payload: {
          razonSocial: values.razonSocial,
          regimenFiscal: values.regimenFiscal,
          nombreComercial: nombreComercialVacio ? null : values.nombreComercial,
          limpiarNombreComercial: nombreComercialVacio,
          tasaIvaDefault: tasaIvaVacia ? null : values.tasaIvaDefault,
          limpiarTasaIvaDefault: tasaIvaVacia,
          // PATCH: null = no tocar (el CP no tiene semántica de "limpiar" —
          // una vez capturado, corregirlo requiere otro valor válido).
          codigoPostal: values.codigoPostal,
          calle: values.calle,
          numeroExterior: values.numeroExterior,
          numeroInterior: values.numeroInterior,
          colonia: values.colonia,
          ciudad: values.ciudad,
          municipio: values.municipio,
          estado: values.estado,
          pais: values.pais,
          limpiarNumeroInterior: values.numeroInterior === '',
        },
        idempotencyKey: keyFor({ values, version: versionEdicion.current }),
      },
      {
        onSuccess: (actualizada) => {
          toast.success('Empresa actualizada');
          versionEdicion.current = actualizada.version;
          form.reset(buildDefaults(actualizada));
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'CONCURRENCY_CONFLICT') {
              toast.error('La empresa cambió mientras editabas.', {
                description:
                  'Recarga la página para revisar los datos actualizados antes de guardar.',
              });
              return;
            }
            if (
              applyServerErrors(form as unknown as Parameters<typeof applyServerErrors>[0], error)
            ) {
              return;
            }
            toast.error(error.problem.title, {
              description: error.traceId ? `Código: ${error.traceId}` : undefined,
            });
            return;
          }
          toast.error('Error inesperado al actualizar la empresa.');
        },
      },
    );
  }

  const dirty = form.formState.isDirty;

  return (
    <form
      onSubmit={(event) => void form.handleSubmit(onSubmit)(event)}
      noValidate
      className="grid grid-cols-1 gap-4 rounded-lg bg-surface-card p-4 shadow-card md:grid-cols-2"
    >
      <FormRow
        id="empresa-rfc"
        label="RFC"
        hint="El RFC no puede cambiarse después de crear la empresa."
      >
        <Input id="empresa-rfc" value={empresa.rfc} readOnly aria-readonly className="font-mono" />
      </FormRow>

      <FormRow label="Régimen fiscal" required error={form.formState.errors.regimenFiscal?.message}>
        {canLeerImpuestos ? (
          <Controller
            name="regimenFiscal"
            control={form.control}
            render={({ field }) => (
              <RegimenFiscalSelector
                value={field.value}
                onChange={(value) => field.onChange(value ?? '')}
                disabled={!canEditar || actualizar.isPending}
              />
            )}
          />
        ) : (
          <Input aria-label="Régimen fiscal" value={empresa.regimenFiscal} readOnly />
        )}
        {!canLeerImpuestos && (
          <p className="text-xs text-ink-muted">
            Se requiere permiso de lectura de catálogos para cambiar el régimen fiscal.
          </p>
        )}
      </FormRow>

      <div className="md:col-span-2">
        <FormRow
          id="empresa-razon-social"
          label="Razón social"
          required
          error={form.formState.errors.razonSocial?.message}
        >
          <Input
            maxLength={254}
            disabled={!canEditar || actualizar.isPending}
            id="empresa-razon-social"
            {...form.register('razonSocial')}
          />
        </FormRow>
      </div>

      <FormRow
        id="empresa-tasa"
        label="Tasa IVA default"
        hint="Fracción 0–1 (ej. 0.16). Fallback de IVA en captura manual de Facturación; el IVA del artículo tiene prioridad. Vacío = sin default."
        error={form.formState.errors.tasaIvaDefault?.message}
      >
        <div className="space-y-2">
          <Controller
            name="tasaIvaDefault"
            control={form.control}
            render={({ field }) => (
              <Input
                id="empresa-tasa"
                type="number"
                step="0.01"
                min={0}
                max={1}
                placeholder="0.16"
                disabled={!canEditar || actualizar.isPending}
                value={field.value ?? ''}
                onChange={(e) =>
                  field.onChange(e.target.value === '' ? null : Number(e.target.value))
                }
              />
            )}
          />
          {canEditar && tasasIva.length > 0 && (
            <label className="block text-xs text-ink-muted">
              Tomar una tasa IVA vigente del catálogo
              <select
                disabled={actualizar.isPending}
                className="mt-1 h-9 w-full rounded-md border bg-background px-2 text-sm text-foreground"
                defaultValue=""
                onChange={(event) => {
                  if (event.target.value !== '') {
                    form.setValue('tasaIvaDefault', Number(event.target.value), {
                      shouldDirty: true,
                      shouldValidate: true,
                    });
                  }
                }}
              >
                <option value="">Seleccionar referencia…</option>
                {tasasIva.map((i) => (
                  <option key={i.id} value={i.tasa}>
                    {i.nombre} · {i.tasa} · {i.fuente}
                  </option>
                ))}
              </select>
            </label>
          )}
        </div>
      </FormRow>

      <FormRow
        id="empresa-cp"
        label="Código postal fiscal"
        hint="CP del domicilio fiscal SAT — es el LugarExpedicion del CFDI 4.0. Sin él, la emisión de facturas falla."
        error={form.formState.errors.codigoPostal?.message}
      >
        <Controller
          name="codigoPostal"
          control={form.control}
          render={({ field }) => (
            <Input
              id="empresa-cp"
              maxLength={5}
              inputMode="numeric"
              placeholder="76120"
              className="font-mono"
              disabled={!canEditar || actualizar.isPending}
              value={field.value ?? ''}
              onChange={(e) => field.onChange(e.target.value.length > 0 ? e.target.value : null)}
            />
          )}
        />
      </FormRow>

      <div className="md:col-span-2">
        <FormRow
          id="empresa-nombre"
          label="Nombre comercial"
          hint="Opcional. Vacío = no aplica."
          error={form.formState.errors.nombreComercial?.message}
        >
          <Controller
            name="nombreComercial"
            control={form.control}
            render={({ field }) => (
              <Input
                id="empresa-nombre"
                maxLength={254}
                disabled={!canEditar || actualizar.isPending}
                value={field.value ?? ''}
                onChange={(e) => field.onChange(e.target.value.length > 0 ? e.target.value : null)}
              />
            )}
          />
        </FormRow>
      </div>

      <fieldset className="grid gap-4 md:col-span-2 md:grid-cols-2">
        <legend className="mb-3 text-md font-semibold text-ink">Domicilio fiscal</legend>
        {camposDomicilio.map(({ name, label, maxLength }) => (
          <FormRow
            key={name}
            id={`empresa-${name}`}
            label={label}
            error={form.formState.errors[name]?.message}
          >
            <Controller
              name={name}
              control={form.control}
              render={({ field }) => (
                <Input
                  id={`empresa-${name}`}
                  maxLength={maxLength}
                  disabled={!canEditar || actualizar.isPending}
                  value={field.value ?? ''}
                  onChange={field.onChange}
                  onBlur={field.onBlur}
                  ref={field.ref}
                />
              )}
            />
          </FormRow>
        ))}
      </fieldset>

      {canEditar && (
        <div className="md:col-span-2 flex flex-wrap items-center justify-end gap-2 border-t border-line-divider pt-3">
          <Button
            type="button"
            variant="ghost"
            onClick={() => {
              versionEdicion.current = empresa.version;
              form.reset(buildDefaults(empresa));
            }}
            disabled={!dirty || actualizar.isPending}
            title={
              actualizar.isPending
                ? 'Espera a que termine el guardado.'
                : !dirty
                  ? 'No hay cambios pendientes.'
                  : undefined
            }
          >
            Descartar cambios
          </Button>
          <Button
            type="submit"
            disabled={!dirty || actualizar.isPending}
            title={
              actualizar.isPending
                ? 'Espera a que termine el guardado.'
                : !dirty
                  ? 'No hay cambios pendientes.'
                  : undefined
            }
          >
            {actualizar.isPending ? 'Guardando…' : 'Guardar cambios'}
          </Button>
        </div>
      )}
    </form>
  );
}

interface FormRowProps {
  id?: string;
  label: string;
  required?: boolean;
  hint?: string;
  error?: string;
  children: React.ReactNode;
}

function FormRow({ id, label, required, hint, error, children }: FormRowProps) {
  return (
    <div className="space-y-1.5">
      <Label htmlFor={id} className="flex items-center gap-1 text-xs font-medium text-ink-strong">
        {label}
        {required && (
          <span aria-hidden="true" className="text-danger-fg">
            *
          </span>
        )}
      </Label>
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

function buildDefaults(empresa: EmpresaResponse): ActualizarEmpresaValues {
  return {
    razonSocial: empresa.razonSocial,
    regimenFiscal: empresa.regimenFiscal,
    nombreComercial: empresa.nombreComercial ?? null,
    tasaIvaDefault: empresa.tasaIvaDefault ?? null,
    codigoPostal: empresa.codigoPostal ?? null,
    calle: empresa.calle || undefined,
    numeroExterior: empresa.numeroExterior || undefined,
    numeroInterior: empresa.numeroInterior || undefined,
    colonia: empresa.colonia || undefined,
    ciudad: empresa.ciudad || undefined,
    municipio: empresa.municipio || undefined,
    estado: empresa.estado || undefined,
    pais: empresa.pais || undefined,
  };
}

const camposDomicilio = [
  { name: 'calle', label: 'Calle', maxLength: 254 },
  { name: 'numeroExterior', label: 'Número exterior', maxLength: 20 },
  { name: 'numeroInterior', label: 'Número interior (opcional)', maxLength: 20 },
  { name: 'colonia', label: 'Colonia', maxLength: 254 },
  { name: 'ciudad', label: 'Ciudad', maxLength: 100 },
  { name: 'municipio', label: 'Municipio', maxLength: 100 },
  { name: 'estado', label: 'Estado', maxLength: 100 },
  { name: 'pais', label: 'País', maxLength: 100 },
] as const;
