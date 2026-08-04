import { useEffect, type ReactNode } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { applyServerErrors, esApiError } from '@/lib/api';
import {
  ActualizarClienteSchema,
  type ActualizarClienteValues,
} from '@/modules/datos-maestros/schemas/cliente';
import { useActualizarCliente } from '@/modules/datos-maestros/api';
import type { ClienteDetalle } from '@/modules/datos-maestros/api/types';
import { MonedaSelector } from '@/components/erp/selectors/MonedaSelector';
import { RegimenFiscalSelector } from '@/components/erp/selectors/RegimenFiscalSelector';
import { UsoCfdiSelector } from '@/components/erp/selectors/UsoCfdiSelector';
import { FormaPagoSelector } from '@/components/erp/selectors/FormaPagoSelector';
import { ClaveSatSelector } from '@/components/erp/selectors/ClaveSatSelector';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Form de datos del cliente (sección única del detalle, sin tabs).
 * PATCH parcial (ADR-0048): <c>clave</c> y <c>referenciaExterna</c>
 * son read-only — inmutables en backend (la referencia correlaciona
 * con A+W). Es la vía para COMPLETAR RFC/régimen/CP de clientes
 * auto-provisionados antes de timbrar.
 *
 * <para>Permiso de mutación: el granular
 * <c>datos_maestros.clientes.gestionar</c> (mismo que exigen los
 * endpoints). El submit usa la convención <c>limpiarX</c>: nullable
 * vacío + flag = setear a null.</para>
 */
export interface ClienteDatosFormProps {
  cliente: ClienteDetalle;
}

const SIN_ASIGNAR = '__sin_asignar__';

export function ClienteDatosForm({ cliente }: ClienteDatosFormProps) {
  const canEditar = useHasPermission(
    PermisosCanonicos.DatosMaestrosClientesGestionar,
  );
  const actualizar = useActualizarCliente();

  const form = useForm<ActualizarClienteValues>({
    resolver: zodResolver(ActualizarClienteSchema),
    defaultValues: buildDefaults(cliente),
  });

  // Si cambia el cliente cargado (otra row), resincronizar.
  useEffect(() => {
    form.reset(buildDefaults(cliente));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cliente.id]);

  function onSubmit(values: ActualizarClienteValues) {
    const rfcVacio = values.rfc == null || values.rfc.length === 0;
    const regimenVacio =
      values.regimenFiscal == null || values.regimenFiscal.length === 0;
    const cpVacio =
      values.codigoPostalFiscal == null ||
      values.codigoPostalFiscal.length === 0;
    const usoCfdiVacio =
      values.usoCfdiDefault == null || values.usoCfdiDefault.length === 0;
    const formaPagoVacia =
      values.formaPagoDefault == null || values.formaPagoDefault.length === 0;
    const metodoPagoVacio = values.metodoPagoDefault == null;
    const emailVacio = values.email == null || values.email.length === 0;
    const telefonoVacio =
      values.telefono == null || values.telefono.length === 0;
    const numRegVacio =
      values.numRegIdTrib == null || values.numRegIdTrib.length === 0;
    const paisVacio =
      values.paisResidencia == null || values.paisResidencia.length === 0;
    const calleVacia =
      values.domicilioExtranjeroCalle == null ||
      values.domicilioExtranjeroCalle.length === 0;
    const estadoExtVacio =
      values.domicilioExtranjeroEstado == null ||
      values.domicilioExtranjeroEstado.length === 0;
    const cpExtVacio =
      values.domicilioExtranjeroCodigoPostal == null ||
      values.domicilioExtranjeroCodigoPostal.length === 0;

    actualizar.mutate(
      {
        id: cliente.id,
        payload: {
          razonSocial: values.razonSocial,
          monedaDefault: values.monedaDefault,
          esGenerico: values.esGenerico,
          rfc: rfcVacio ? null : values.rfc,
          regimenFiscal: regimenVacio ? null : values.regimenFiscal,
          codigoPostalFiscal: cpVacio ? null : values.codigoPostalFiscal,
          usoCfdiDefault: usoCfdiVacio ? null : values.usoCfdiDefault,
          formaPagoDefault: formaPagoVacia ? null : values.formaPagoDefault,
          metodoPagoDefault: metodoPagoVacio ? null : values.metodoPagoDefault,
          email: emailVacio ? null : values.email,
          telefono: telefonoVacio ? null : values.telefono,
          limpiarRfc: rfcVacio,
          limpiarRegimenFiscal: regimenVacio,
          limpiarCodigoPostalFiscal: cpVacio,
          limpiarUsoCfdiDefault: usoCfdiVacio,
          limpiarFormaPagoDefault: formaPagoVacia,
          limpiarMetodoPagoDefault: metodoPagoVacio,
          limpiarEmail: emailVacio,
          limpiarTelefono: telefonoVacio,
          // Receptor extranjero (CCE): vacío = limpiar.
          numRegIdTrib: numRegVacio ? null : values.numRegIdTrib,
          paisResidencia: paisVacio ? null : values.paisResidencia,
          domicilioExtranjeroCalle: calleVacia
            ? null
            : values.domicilioExtranjeroCalle,
          domicilioExtranjeroEstado: estadoExtVacio
            ? null
            : values.domicilioExtranjeroEstado,
          domicilioExtranjeroCodigoPostal: cpExtVacio
            ? null
            : values.domicilioExtranjeroCodigoPostal,
          limpiarNumRegIdTrib: numRegVacio,
          limpiarPaisResidencia: paisVacio,
          limpiarDomicilioExtranjeroCalle: calleVacia,
          limpiarDomicilioExtranjeroEstado: estadoExtVacio,
          limpiarDomicilioExtranjeroCodigoPostal: cpExtVacio,
        },
        // Key fresca por submit: este form es multi-submit (queda montado tras
        // guardar), así que una key estable daría 422 al 2º guardado con un body
        // distinto (ADR-0020; patrón AccionesOC/SheetNuevaOC).
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success('Cliente actualizado');
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
          toast.error('Error inesperado al actualizar el cliente.');
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
      <FormRow label="Clave" hint="No editable.">
        <Input
          value={cliente.clave}
          readOnly
          aria-readonly
          className="font-mono"
        />
      </FormRow>

      <FormRow
        label="Referencia externa"
        hint="Correlación con A+W. No editable."
      >
        <Input
          value={cliente.referenciaExterna ?? '—'}
          readOnly
          aria-readonly
          className="font-mono"
        />
      </FormRow>

      <div className="md:col-span-2">
        <FormRow
          label="Razón social"
          required
          error={form.formState.errors.razonSocial?.message}
        >
          <Input
            maxLength={254}
            disabled={!canEditar}
            {...form.register('razonSocial')}
          />
        </FormRow>
      </div>

      <FormRow
        label="RFC"
        hint="Requerido para timbrar."
        error={form.formState.errors.rfc?.message}
      >
        <Controller
          name="rfc"
          control={form.control}
          render={({ field }) => (
            <Input
              maxLength={13}
              disabled={!canEditar}
              className="font-mono"
              value={field.value ?? ''}
              onChange={(e) =>
                field.onChange(
                  e.target.value.length > 0 ? e.target.value : null,
                )
              }
            />
          )}
        />
      </FormRow>

      <FormRow
        label="Régimen fiscal"
        hint="Catálogo SAT c_RegimenFiscal. Requerido para timbrar."
        error={form.formState.errors.regimenFiscal?.message}
      >
        <Controller
          name="regimenFiscal"
          control={form.control}
          render={({ field }) => (
            <RegimenFiscalSelector
              value={field.value}
              onChange={field.onChange}
              disabled={!canEditar}
            />
          )}
        />
      </FormRow>

      <FormRow
        label="Código postal fiscal"
        hint="Del domicilio fiscal. Requerido para timbrar."
        error={form.formState.errors.codigoPostalFiscal?.message}
      >
        <Controller
          name="codigoPostalFiscal"
          control={form.control}
          render={({ field }) => (
            <Input
              maxLength={5}
              inputMode="numeric"
              disabled={!canEditar}
              className="font-mono"
              value={field.value ?? ''}
              onChange={(e) =>
                field.onChange(
                  e.target.value.length > 0 ? e.target.value : null,
                )
              }
            />
          )}
        />
      </FormRow>

      <FormRow
        label="Uso CFDI default"
        hint="Opcional. Catálogo SAT c_UsoCFDI."
        error={form.formState.errors.usoCfdiDefault?.message}
      >
        <Controller
          name="usoCfdiDefault"
          control={form.control}
          render={({ field }) => (
            <UsoCfdiSelector
              value={field.value}
              onChange={field.onChange}
              disabled={!canEditar}
            />
          )}
        />
      </FormRow>

      <FormRow
        label="Forma de pago default"
        hint="Opcional. Catálogo SAT c_FormaPago."
        error={form.formState.errors.formaPagoDefault?.message}
      >
        <Controller
          name="formaPagoDefault"
          control={form.control}
          render={({ field }) => (
            <FormaPagoSelector
              value={field.value}
              onChange={field.onChange}
              disabled={!canEditar}
            />
          )}
        />
      </FormRow>

      <FormRow
        label="Método de pago default"
        hint="Opcional. PUE o PPD."
        error={form.formState.errors.metodoPagoDefault?.message}
      >
        <Controller
          name="metodoPagoDefault"
          control={form.control}
          render={({ field }) => (
            <Select
              value={field.value ?? SIN_ASIGNAR}
              onValueChange={(v) =>
                field.onChange(v === SIN_ASIGNAR ? null : v)
              }
              disabled={!canEditar}
            >
              <SelectTrigger>
                <SelectValue placeholder="— sin asignar —" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={SIN_ASIGNAR}>— sin asignar —</SelectItem>
                <SelectItem value="PUE">PUE · Pago en una exhibición</SelectItem>
                <SelectItem value="PPD">PPD · Pago en parcialidades</SelectItem>
              </SelectContent>
            </Select>
          )}
        />
      </FormRow>

      <FormRow
        label="Moneda default"
        required
        hint="Del catálogo de monedas."
        error={form.formState.errors.monedaDefault?.message}
      >
        <Controller
          name="monedaDefault"
          control={form.control}
          render={({ field }) => (
            <MonedaSelector
              value={field.value}
              onChange={(codigo) => field.onChange(codigo ?? '')}
              disabled={!canEditar}
            />
          )}
        />
      </FormRow>

      <FormRow
        label="Cliente genérico"
        hint="Mostrador / público en general."
        error={form.formState.errors.esGenerico?.message}
      >
        <Controller
          name="esGenerico"
          control={form.control}
          render={({ field }) => (
            <label className="flex h-9 items-center gap-2 text-sm">
              <Checkbox
                checked={field.value}
                onCheckedChange={(v) => field.onChange(v === true)}
                disabled={!canEditar}
                aria-label="Cliente genérico"
              />
              <span className="text-muted-foreground">
                Es genérico (público en general)
              </span>
            </label>
          )}
        />
      </FormRow>

      <FormRow
        label="Email"
        hint="Opcional."
        error={form.formState.errors.email?.message}
      >
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
                field.onChange(
                  e.target.value.length > 0 ? e.target.value : null,
                )
              }
            />
          )}
        />
      </FormRow>

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
                field.onChange(
                  e.target.value.length > 0 ? e.target.value : null,
                )
              }
            />
          )}
        />
      </FormRow>

      <div className="md:col-span-2 border-t pt-3 text-sm font-medium text-muted-foreground">
        Receptor extranjero (Comercio Exterior)
        <p className="text-xs font-normal">
          Solo para clientes de exportación. Prellenar el encabezado del CCE.
          Fuente A+W cuando esté disponible; si no, captúralos aquí.
        </p>
      </div>

      <FormRow
        label="Núm. reg. id. trib. (Tax ID)"
        hint="Registro de identificación tributaria extranjero del receptor."
        error={form.formState.errors.numRegIdTrib?.message}
      >
        <Controller
          name="numRegIdTrib"
          control={form.control}
          render={({ field }) => (
            <Input
              maxLength={40}
              disabled={!canEditar}
              value={field.value ?? ''}
              onChange={(e) =>
                field.onChange(e.target.value.length > 0 ? e.target.value : null)
              }
            />
          )}
        />
      </FormRow>

      <FormRow
        label="País de residencia"
        hint="Clave SAT c_Pais (ISO alfa-3, ej. USA)."
        error={form.formState.errors.paisResidencia?.message}
      >
        <Controller
          name="paisResidencia"
          control={form.control}
          render={({ field }) => (
            <ClaveSatSelector
              catalogo="pais"
              value={field.value ?? null}
              initialLabel={field.value ?? null}
              onChange={(item) => field.onChange(item?.codigo ?? null)}
              disabled={!canEditar}
              placeholder="Buscar país…"
            />
          )}
        />
      </FormRow>

      <div className="md:col-span-2">
        <FormRow
          label="Domicilio extranjero — calle"
          hint="Opcional."
          error={form.formState.errors.domicilioExtranjeroCalle?.message}
        >
          <Input
            maxLength={200}
            disabled={!canEditar}
            {...form.register('domicilioExtranjeroCalle', {
              setValueAs: (v) => (v === '' ? null : v),
            })}
          />
        </FormRow>
      </div>

      <FormRow
        label="Estado / provincia extranjera"
        hint="Texto libre del país del receptor (ej. Texas)."
        error={form.formState.errors.domicilioExtranjeroEstado?.message}
      >
        <Input
          maxLength={100}
          disabled={!canEditar}
          {...form.register('domicilioExtranjeroEstado', {
            setValueAs: (v) => (v === '' ? null : v),
          })}
        />
      </FormRow>

      <FormRow
        label="Código postal extranjero"
        error={form.formState.errors.domicilioExtranjeroCodigoPostal?.message}
      >
        <Input
          maxLength={12}
          disabled={!canEditar}
          {...form.register('domicilioExtranjeroCodigoPostal', {
            setValueAs: (v) => (v === '' ? null : v),
          })}
        />
      </FormRow>

      {canEditar && (
        <div className="md:col-span-2 flex flex-wrap items-center justify-end gap-2 border-t pt-3">
          <Button
            type="button"
            variant="ghost"
            onClick={() => form.reset(buildDefaults(cliente))}
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

function buildDefaults(c: ClienteDetalle): ActualizarClienteValues {
  return {
    razonSocial: c.razonSocial,
    rfc: c.rfc ?? null,
    regimenFiscal: c.regimenFiscal ?? null,
    codigoPostalFiscal: c.codigoPostalFiscal ?? null,
    usoCfdiDefault: c.usoCfdiDefault ?? null,
    formaPagoDefault: c.formaPagoDefault ?? null,
    metodoPagoDefault:
      c.metodoPagoDefault === 'PUE' || c.metodoPagoDefault === 'PPD'
        ? c.metodoPagoDefault
        : null,
    monedaDefault: c.monedaDefault,
    esGenerico: c.esGenerico,
    email: c.email ?? null,
    telefono: c.telefono ?? null,
    numRegIdTrib: c.numRegIdTrib ?? null,
    paisResidencia: c.paisResidencia ?? null,
    domicilioExtranjeroCalle: c.domicilioExtranjeroCalle ?? null,
    domicilioExtranjeroEstado: c.domicilioExtranjeroEstado ?? null,
    domicilioExtranjeroCodigoPostal: c.domicilioExtranjeroCodigoPostal ?? null,
  };
}
