import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { FormGrid, FormCampo } from '@/components/erp/forms/FormGrid';
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
import { useNavigate } from '@tanstack/react-router';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { UppercaseInput } from '@/components/erp/forms/UppercaseInput';
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
  NuevoClienteContext,
  type NuevoClienteApi,
} from '@/modules/datos-maestros/components/nuevo-cliente-context';
import {
  CrearClienteSchema,
  type CrearClienteValues,
} from '@/modules/datos-maestros/schemas/cliente';
import { useCrearCliente } from '@/modules/datos-maestros/api';
import { ClasificacionClienteSelector } from './ClasificacionClienteSelector';
import { UsoCfdiSelector } from '@/components/erp/selectors/UsoCfdiSelector';
import { FormaPagoSelector } from '@/components/erp/selectors/FormaPagoSelector';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Link } from '@tanstack/react-router';
import { RegimenFiscalSelector } from '@/components/erp/selectors/RegimenFiscalSelector';
import { MonedaSelector } from '@/components/erp/selectors/MonedaSelector';

/**
 * <c>&lt;NuevoClienteProvider/&gt;</c> + Sheet asociado (P4 del patrón
 * cross-módulo, ADR-0048). Slide-from-right max-w-2xl con el alta
 * manual de cliente (<c>origen = Manual</c>; la auto-provisión desde
 * A+W no pasa por aquí).
 *
 * <para><b>Confirm al cerrar con isDirty</b>: imita
 * <c>NuevoProveedorProvider</c>. Post-submit: invalida la lista, toast
 * con clave, navega a <c>/admin/datos-maestros/clientes/$id</c> y
 * cierra el Sheet con <c>force: true</c>. Los defaults fiscales (uso
 * CFDI, forma/método de pago, moneda) se completan en el detalle.</para>
 */
export function NuevoClienteProvider({ children }: { children: ReactNode }) {
  const [abierto, setAbierto] = useState(false);
  const alCerrar = useCallback(() => setAbierto(false), []);
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(alCerrar);
  const cerrarConConfirm = useCallback(
    (opts?: { force?: boolean }) => solicitarCierre(opts?.force),
    [solicitarCierre],
  );

  const api = useMemo<NuevoClienteApi>(
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
    <NuevoClienteContext.Provider value={api}>
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
            <SheetTitle>Nuevo cliente</SheetTitle>
            <SheetDescription>
              Captura la clasificación y los datos fiscales completos para venta directa.
            </SheetDescription>
          </SheetHeader>

          <div className="contents">
            <div className="contents">
              {abierto && <NuevoClienteForm onClose={cerrarConConfirm} onDirtyChange={setDirty} />}
            </div>
          </div>
        </SheetContent>
      </Sheet>
      {dialogo}
    </NuevoClienteContext.Provider>
  );
}

interface NuevoClienteFormProps {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange: (dirty: boolean) => void;
}

function NuevoClienteForm({ onClose, onDirtyChange }: NuevoClienteFormProps) {
  const navigate = useNavigate();
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearCliente();

  const form = useForm<CrearClienteValues>({
    resolver: zodResolver(CrearClienteSchema),
    defaultValues: {
      clasificacionId: '',
      usoCfdiDefault: '',
      formaPagoDefault: '',
      metodoPagoDefault: 'PUE',
      monedaDefault: 'MXN',
      paisResidencia: null,
      clave: '',
      razonSocial: '',
      referenciaExterna: null,
      rfc: null,
      regimenFiscal: null,
      codigoPostalFiscal: null,
      email: null,
      telefono: null,
      numRegIdTrib: null,
      domicilioExtranjeroCalle: null,
      domicilioExtranjeroEstado: null,
      domicilioExtranjeroCodigoPostal: null,
    },
  });

  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  function onSubmit(values: CrearClienteValues) {
    crear.mutate(
      {
        payload: {
          clasificacionId: values.clasificacionId,
          paisResidencia: values.paisResidencia,
          clave: values.clave,
          razonSocial: values.razonSocial,
          referenciaExterna: values.referenciaExterna,
          rfc: values.rfc,
          regimenFiscal: values.regimenFiscal,
          codigoPostalFiscal: values.codigoPostalFiscal,
          usoCfdiDefault: values.usoCfdiDefault,
          formaPagoDefault: values.formaPagoDefault,
          metodoPagoDefault: values.metodoPagoDefault,
          monedaDefault: values.monedaDefault,
          esGenerico: values.rfc === 'XAXX010101000' || values.rfc === 'XEXX010101000',
          email: values.email,
          telefono: values.telefono,
          numRegIdTrib: values.numRegIdTrib,
          domicilioExtranjeroCalle: values.domicilioExtranjeroCalle,
          domicilioExtranjeroEstado: values.domicilioExtranjeroEstado,
          domicilioExtranjeroCodigoPostal: values.domicilioExtranjeroCodigoPostal,
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Cliente ${resp.clave} creado`);
          form.reset(values);
          onClose({ force: true });
          navigate({
            to: '/admin/datos-maestros/clientes/$id',
            params: { id: resp.id },
          });
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'CLIENTE_CLAVE_DUPLICADA') {
              form.setError('clave', {
                type: error.code,
                message: 'Ya existe un cliente con esa clave.',
              });
              return;
            }
            if (error.code === 'CLIENTE_REFERENCIA_DUPLICADA') {
              form.setError('referenciaExterna', {
                type: error.code,
                message: 'Ya existe un cliente con esa referencia externa.',
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
          toast.error('Error inesperado al crear el cliente.');
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
          <FormCampo span={12}>
            <Controller
              name="clasificacionId"
              control={form.control}
              render={({ field }) => (
                <ClasificacionClienteSelector value={field.value} onChange={field.onChange} />
              )}
            />
          </FormCampo>
          <FormCampo span={12}>
            {form.formState.errors.clasificacionId && (
              <p role="alert" className="text-xs text-danger-fg">
                {form.formState.errors.clasificacionId.message}
              </p>
            )}
          </FormCampo>
          <FormCampo span={12}>
            {crear.error && esApiError(crear.error) && crear.error.problem.clientesExistentes && (
              <div role="alert" className="rounded-md bg-warning-note-bg p-3 text-warning-note-fg">
                <p>Ya existe un cliente con ese RFC. Usa el existente:</p>
                {crear.error.problem.clientesExistentes.map((c) => (
                  <Link
                    key={c.id}
                    to="/admin/datos-maestros/clientes/$id"
                    params={{ id: c.id }}
                    className="block text-brand"
                  >
                    {c.clave} · {c.razonSocial}
                  </Link>
                ))}
              </div>
            )}
          </FormCampo>
          <FormCampo span={6}>
            <FormRow label="Uso de CFDI" error={form.formState.errors.usoCfdiDefault?.message}>
              <Controller
                name="usoCfdiDefault"
                control={form.control}
                render={({ field }) => (
                  <UsoCfdiSelector value={field.value} onChange={field.onChange} />
                )}
              />
            </FormRow>
          </FormCampo>
          <FormCampo span={6}>
            <FormRow label="Forma de pago" error={form.formState.errors.formaPagoDefault?.message}>
              <Controller
                name="formaPagoDefault"
                control={form.control}
                render={({ field }) => (
                  <FormaPagoSelector value={field.value} onChange={field.onChange} />
                )}
              />
            </FormRow>
          </FormCampo>
          <FormCampo span={6}>
            <FormRow label="Método de pago">
              <Controller
                name="metodoPagoDefault"
                control={form.control}
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger aria-label="Método de pago">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="PUE">Pago en una sola exhibición</SelectItem>
                      <SelectItem value="PPD">Pago en parcialidades o diferido</SelectItem>
                    </SelectContent>
                  </Select>
                )}
              />
            </FormRow>
          </FormCampo>
          <FormCampo span={6}>
            <FormRow
              label="País de residencia"
              hint="Para RFC extranjero, clave SAT de tres letras distinta de MEX."
              error={form.formState.errors.paisResidencia?.message}
            >
              <Controller
                name="paisResidencia"
                control={form.control}
                render={({ field }) => (
                  <UppercaseInput
                    value={field.value ?? ''}
                    onChange={(e) => field.onChange(e.target.value || null)}
                    maxLength={3}
                    aria-label="País de residencia"
                  />
                )}
              />
            </FormRow>
          </FormCampo>
          <FormCampo span={6}>
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
                  />
                )}
              />
            </FormRow>
          </FormCampo>
          <FormCampo span={6}>
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
                    aria-label="Núm. reg. id. trib. (Tax ID)"
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
              label="Domicilio extranjero — calle"
              hint="Opcional."
              error={form.formState.errors.domicilioExtranjeroCalle?.message}
            >
              <Input
                maxLength={200}
                aria-label="Domicilio extranjero — calle"
                {...form.register('domicilioExtranjeroCalle', {
                  setValueAs: (v) => (v === '' ? null : v),
                })}
              />
            </FormRow>
          </FormCampo>
          <FormCampo span={6}>
            <FormRow
              label="Estado / provincia extranjera"
              hint="Texto libre del país del receptor (ej. Texas)."
              error={form.formState.errors.domicilioExtranjeroEstado?.message}
            >
              <Input
                maxLength={100}
                aria-label="Estado / provincia extranjera"
                {...form.register('domicilioExtranjeroEstado', {
                  setValueAs: (v) => (v === '' ? null : v),
                })}
              />
            </FormRow>
          </FormCampo>
          <FormCampo span={6}>
            <FormRow
              label="Código postal extranjero"
              error={form.formState.errors.domicilioExtranjeroCodigoPostal?.message}
            >
              <Input
                maxLength={12}
                aria-label="Código postal extranjero"
                {...form.register('domicilioExtranjeroCodigoPostal', {
                  setValueAs: (v) => (v === '' ? null : v),
                })}
              />
            </FormRow>
          </FormCampo>
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
                placeholder="C-001"
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

          <FormCampo span={6}>
            <FormRow
              label="Referencia externa"
              hint="Opcional. Código del cliente en A+W (correlación). Inmutable."
              error={form.formState.errors.referenciaExterna?.message}
            >
              <Controller
                name="referenciaExterna"
                control={form.control}
                render={({ field }) => (
                  <Input
                    maxLength={50}
                    placeholder="AW-12345"
                    className="font-mono"
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
              label="RFC"
              hint="Requerido para el alta manual."
              error={form.formState.errors.rfc?.message}
            >
              <Controller
                name="rfc"
                control={form.control}
                render={({ field }) => (
                  <UppercaseInput
                    maxLength={13}
                    placeholder="ACM010101ABC"
                    className="font-mono"
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
            <FormGrid>
              <FormCampo span={6}>
                <FormRow
                  label="Régimen fiscal"
                  hint="Catálogo SAT c_RegimenFiscal."
                  error={form.formState.errors.regimenFiscal?.message}
                >
                  <Controller
                    name="regimenFiscal"
                    control={form.control}
                    render={({ field }) => (
                      <RegimenFiscalSelector value={field.value} onChange={field.onChange} />
                    )}
                  />
                </FormRow>
              </FormCampo>

              <FormCampo span={6}>
                <FormRow
                  label="Código postal fiscal"
                  hint="5 dígitos."
                  error={form.formState.errors.codigoPostalFiscal?.message}
                >
                  <Controller
                    name="codigoPostalFiscal"
                    control={form.control}
                    render={({ field }) => (
                      <Input
                        maxLength={5}
                        inputMode="numeric"
                        placeholder="76100"
                        className="font-mono"
                        value={field.value ?? ''}
                        onChange={(e) =>
                          field.onChange(e.target.value.length > 0 ? e.target.value : null)
                        }
                      />
                    )}
                  />
                </FormRow>
              </FormCampo>
            </FormGrid>
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
                    placeholder="contacto@cliente.com"
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
        </FormSeccion>
      </SheetBody>
      <SheetFooter>
        <Button type="button" variant="ghost" onClick={() => onClose()} disabled={crear.isPending}>
          Cancelar
        </Button>
        <Button type="submit" disabled={crear.isPending}>
          {crear.isPending ? 'Creando…' : 'Crear cliente'}
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
