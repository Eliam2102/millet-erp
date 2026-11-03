import { FormCampo } from '@/components/erp/forms/FormGrid';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { erroresDeClasificacion } from '@/modules/datos-maestros/lib/clasificacion-fiscal';
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
import { useNavigate } from '@tanstack/react-router';
import { Controller, useForm, useWatch } from 'react-hook-form';
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
import {
  SheetBody,
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetFooter,
} from '@/components/ui/sheet';
import { applyServerErrors, esApiError, useFormIdempotencyKey, codigoSoporte } from '@/lib/api';
import {
  NuevoProveedorContext,
  type NuevoProveedorApi,
} from '@/modules/datos-maestros/components/nuevo-proveedor-context';
import {
  CrearProveedorSchema,
  type CrearProveedorValues,
} from '@/modules/datos-maestros/schemas/proveedor';
import { useCrearProveedor } from '@/modules/datos-maestros/api';
import {
  ClasificacionFiscalCampos,
  type ClasificacionFiscalValores,
  type ErroresClasificacionFiscal,
} from '@/modules/datos-maestros/components/ClasificacionFiscalCampos';
import {
  EstatusCatalogo,
  OrigenProveedor,
  TipoPersonaProveedor,
} from '@/modules/datos-maestros/api/types';

/**
 * <c>&lt;NuevoProveedorProvider/&gt;</c> + Sheet asociado (P4 del
 * patrón cross-módulo). Slide-from-right max-w-2xl con form de alta
 * de proveedor.
 *
 * <para><b>Confirm al cerrar con isDirty</b>: imita
 * <c>NuevaEmpresaProvider</c>. Apertura siempre vía
 * <c>useNuevoProveedor().abrir()</c>.</para>
 *
 * <para><b>Clasificación fiscal</b>: origen, régimen y retención viajan en el
 * mismo POST del alta (una sola transacción). Un proveedor extranjero no
 * captura RFC: el backend guarda el genérico XEXX010101000.</para>
 *
 * <para><b>Post-submit</b>: invalida la lista, toast con clave, navega
 * a <c>/admin/datos-maestros/proveedores/$id</c> y cierra el Sheet
 * con <c>force: true</c>.</para>
 */
export function NuevoProveedorProvider({ children }: { children: ReactNode }) {
  const [abierto, setAbierto] = useState(false);
  const alCerrar = useCallback(() => setAbierto(false), []);
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(alCerrar);
  const cerrarConConfirm = useCallback(
    (opts?: { force?: boolean }) => solicitarCierre(opts?.force),
    [solicitarCierre],
  );

  const api = useMemo<NuevoProveedorApi>(
    () => ({
      abrir: () => {
        setDirty(false);
        setAbierto(true);
      },
      cerrar: cerrarConConfirm,
      setDirty,
    }),
    [cerrarConConfirm, setDirty],
  );

  return (
    <NuevoProveedorContext.Provider value={api}>
      {children}
      <Sheet
        open={abierto}
        onOpenChange={(open) => {
          if (!open) {
            cerrarConConfirm();
            return;
          }
          setAbierto(true);
        }}
      >
        <SheetContent size="lg" side="right">
          <SheetHeader>
            <SheetTitle>Nuevo proveedor</SheetTitle>
            <SheetDescription>
              Alta del proveedor en el catálogo cross-empresa. Datos adicionales (condiciones de
              pago, moneda, etc.) se ajustan después en el detalle.
            </SheetDescription>
          </SheetHeader>

          <div className="contents">
            <div className="contents">
              {abierto && (
                <NuevoProveedorForm onClose={cerrarConConfirm} onDirtyChange={setDirty} />
              )}
            </div>
          </div>
        </SheetContent>
      </Sheet>
      {dialogo}
    </NuevoProveedorContext.Provider>
  );
}

interface NuevoProveedorFormProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange: (dirty: boolean) => void;
}

function NuevoProveedorForm({ onClose, onDirtyChange }: NuevoProveedorFormProps) {
  const navigate = useNavigate();
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearProveedor();
  const [fiscal, setFiscal] = useState<ClasificacionFiscalValores>({
    origen: null,
    regimenFiscalCodigo: null,
    conceptoRetencion: null,
  });
  const [erroresFiscal, setErroresFiscal] = useState<ErroresClasificacionFiscal>({});
  const extranjero = fiscal.origen === OrigenProveedor.Extranjero;

  const form = useForm<CrearProveedorValues>({
    resolver: zodResolver(CrearProveedorSchema),
    defaultValues: {
      pais: '',
      clave: '',
      razonSocial: '',
      rfc: '',
      origen: null,
      tipoPersona: TipoPersonaProveedor.Moral,
      nombreComercial: null,
      email: null,
      telefono: null,
      condicionesPagoDias: null,
      claveAw: null,
    },
  });

  const tipoPersona = useWatch({ control: form.control, name: 'tipoPersona' });
  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  function onSubmit(values: CrearProveedorValues) {
    crear.mutate(
      {
        payload: {
          clave: values.clave,
          razonSocial: values.razonSocial,
          calle: values.calle,
          numeroExterior: values.numeroExterior,
          numeroInterior: values.numeroInterior,
          colonia: values.colonia,
          ciudad: values.ciudad,
          municipio: values.municipio,
          estado: values.estado,
          codigoPostal: values.codigoPostal,
          pais: values.pais || (fiscal.origen === OrigenProveedor.Nacional ? 'MEX' : undefined),
          taxId: extranjero ? values.taxId : undefined,

          rfc: extranjero ? null : values.rfc,
          tipoPersona: values.tipoPersona,
          nombreComercial: values.nombreComercial,
          email: values.email,
          telefono: values.telefono,
          condicionesPagoDias: values.condicionesPagoDias,
          claveAw: values.claveAw,
          monedaPreferidaId: null,
          origen: fiscal.origen,
          regimenFiscalCodigo: fiscal.regimenFiscalCodigo,
          conceptoRetencion: fiscal.conceptoRetencion,
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Proveedor ${resp.clave} creado`);
          if (resp.estatus === EstatusCatalogo.EnRevision) {
            toast.warning(`${resp.clave} quedó en revisión: posible duplicado`, {
              description:
                'RFC genérico con la misma razón social que otro proveedor existente. Revísalo antes de operar con él.',
            });
          }
          form.reset(values);
          onClose({ force: true });
          navigate({
            to: '/admin/datos-maestros/proveedores/$id',
            params: { id: resp.id },
          });
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'PROVEEDOR_CLAVE_DUPLICADA') {
              form.setError('clave', {
                type: error.code,
                message: 'Ya existe un proveedor con esa clave.',
              });
              return;
            }
            if (
              error.code === 'PROVEEDOR_REGIMEN_REQUERIDO' ||
              error.code === 'PROVEEDOR_REGIMEN_NO_EXISTE' ||
              error.code === 'PROVEEDOR_REGIMEN_INVALIDO' ||
              error.code === 'PROVEEDOR_REGIMEN_NO_APLICA'
            ) {
              setErroresFiscal(
                erroresDeClasificacion(error.code, error.problem.detail ?? error.problem.title),
              );
              return;
            }
            if (error.code === 'PROVEEDOR_CLAVE_AW_DUPLICADA') {
              form.setError('claveAw', {
                type: error.code,
                message: error.problem.detail ?? 'Ya existe un proveedor con esa clave de A+W.',
              });
              return;
            }
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
          toast.error('Error inesperado al crear el proveedor.');
        },
      },
    );
  }

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      className="flex min-h-0 flex-1 flex-col overflow-hidden"
    >
      <SheetBody>
        <FormSeccion titulo="Datos del registro">
          <FormCampo span={6}>
            <FormRow
              label="Clave"
              required
              hint="Identificador corto. Inmutable después de creado."
              error={form.formState.errors.clave?.message}
            >
              <Input
                autoFocus
                maxLength={20}
                placeholder="P-001"
                className="font-mono"
                {...form.register('clave')}
              />
            </FormRow>
          </FormCampo>

          <FormCampo span={6}>
            <FormRow
              label="Razón social"
              required
              error={form.formState.errors.razonSocial?.message}
            >
              <Input
                maxLength={254}
                placeholder="Acme S.A. de C.V."
                {...form.register('razonSocial')}
              />
            </FormRow>
          </FormCampo>

          <FormCampo span={12}>
            {extranjero ? (
              <p className="rounded-md bg-surface-subtle p-3 text-xs text-ink-muted">
                Un proveedor extranjero no tiene RFC: se guarda el RFC genérico XEXX010101000.
              </p>
            ) : (
              <FormRow
                label="RFC"
                required
                hint="12 caracteres (persona moral) o 13 (persona física)."
                error={form.formState.errors.rfc?.message}
              >
                <UppercaseInput
                  maxLength={13}
                  placeholder="ACM010101ABC"
                  className="font-mono"
                  {...form.register('rfc')}
                />
              </FormRow>
            )}
          </FormCampo>

          <FormCampo span={6}>
            <FormRow
              label="Tipo persona"
              required
              error={form.formState.errors.tipoPersona?.message}
            >
              <Controller
                name="tipoPersona"
                control={form.control}
                render={({ field }) => (
                  <Select
                    value={String(field.value)}
                    onValueChange={(v) => {
                      field.onChange(Number(v));
                      // El régimen depende del tipo de persona: se vuelve a elegir.
                      setFiscal((f) => ({ ...f, regimenFiscalCodigo: null }));
                    }}
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
            <FormRow label="Email" hint="Opcional." error={form.formState.errors.email?.message}>
              <Controller
                name="email"
                control={form.control}
                render={({ field }) => (
                  <Input
                    type="email"
                    maxLength={254}
                    placeholder="contacto@proveedor.com"
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
                    placeholder="+52 55 1234 5678"
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
              label="Clave de A+W"
              hint="Clave del proveedor en A+W. La usa la aplicación de Almacenes."
              error={form.formState.errors.claveAw?.message}
            >
              <Controller
                name="claveAw"
                control={form.control}
                render={({ field }) => (
                  <Input
                    type="number"
                    min={1}
                    step={1}
                    inputMode="numeric"
                    aria-label="Clave de A+W"
                    className="font-mono"
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
            <fieldset className="space-y-3 border-t pt-3">
              <legend className="text-sm font-semibold">Clasificación fiscal (opcional)</legend>
              <ClasificacionFiscalCampos
                idBase="nuevo-proveedor"
                valores={fiscal}
                onChange={(v) => {
                  setFiscal(v);
                  // El zod del form necesita el origen para decidir si el RFC es obligatorio.
                  form.setValue('origen', v.origen);
                  if (v.origen === OrigenProveedor.Extranjero) {
                    form.setValue('rfc', '');
                    form.clearErrors('rfc');
                  }
                  setErroresFiscal({});
                  onDirtyChange(true);
                }}
                errores={erroresFiscal}
                disabled={crear.isPending}
                tipoPersona={tipoPersona}
              />
            </fieldset>
          </FormCampo>
          <FormCampo span={12}>
            <h3 className="text-sm font-semibold">Domicilio</h3>
          </FormCampo>
          <FormCampo span={6}>
            <label htmlFor="proveedor-calle" className="text-xs font-medium text-ink-strong">
              Calle
            </label>
            <Input id="proveedor-calle" maxLength={254} {...form.register('calle')} />
            {form.formState.errors.calle?.message && (
              <p role="alert" className="text-xs text-danger-fg">
                {form.formState.errors.calle?.message}
              </p>
            )}
          </FormCampo>
          <FormCampo span={6}>
            <label
              htmlFor="proveedor-numeroExterior"
              className="text-xs font-medium text-ink-strong"
            >
              Número exterior
            </label>
            <Input
              id="proveedor-numeroExterior"
              maxLength={20}
              {...form.register('numeroExterior')}
            />
            {form.formState.errors.numeroExterior?.message && (
              <p role="alert" className="text-xs text-danger-fg">
                {form.formState.errors.numeroExterior?.message}
              </p>
            )}
          </FormCampo>
          <FormCampo span={6}>
            <label
              htmlFor="proveedor-numeroInterior"
              className="text-xs font-medium text-ink-strong"
            >
              Número interior
            </label>
            <Input
              id="proveedor-numeroInterior"
              maxLength={20}
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
            <Input id="proveedor-colonia" maxLength={254} {...form.register('colonia')} />
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
            <Input id="proveedor-ciudad" maxLength={100} {...form.register('ciudad')} />
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
            <Input id="proveedor-municipio" maxLength={100} {...form.register('municipio')} />
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
            <Input id="proveedor-estado" maxLength={100} {...form.register('estado')} />
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
            <Input id="proveedor-codigoPostal" maxLength={20} {...form.register('codigoPostal')} />
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
            <Input id="proveedor-pais" maxLength={3} {...form.register('pais')} />
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
              <Input id="proveedor-taxId" maxLength={40} {...form.register('taxId')} />
              {form.formState.errors.taxId?.message && (
                <p role="alert" className="text-xs text-danger-fg">
                  {form.formState.errors.taxId?.message}
                </p>
              )}
              <p className="text-xs text-ink-muted">Opcional. Aparece en la orden de compra</p>
            </FormCampo>
          )}
        </FormSeccion>
      </SheetBody>
      <SheetFooter>
        <Button type="button" variant="ghost" onClick={() => onClose()} disabled={crear.isPending}>
          Cancelar
        </Button>
        <Button type="submit" disabled={crear.isPending}>
          {crear.isPending ? 'Creando…' : 'Crear proveedor'}
        </Button>
      </SheetFooter>
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
