import { useEffect, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { AlertCircle, AlertTriangle, Check, CheckCircle2, Loader2, Plus, RotateCcw, X } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  EmpleadoSchema,
  type EmpleadoValues,
} from '@/modules/administracion/schemas/empleado';
import {
  useActualizarEmpleado,
  useAltaColaborador,
  usePuestosDeSucursal,
  useValidarCorreoCorporativo,
} from '@/modules/administracion/api';
import { useRoles } from '@/modules/identidad/api/roles';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { ActualizarEmpleadoPayload } from '@/modules/administracion/api/types';
import type { EmpleadoListItem } from '@/features/catalogos/api';
import {
  DepartamentoSelector,
  EmpleadoSelector,
  PuestoSelector,
  SucursalSelector,
} from '@/components/erp';
import { cn } from '@/lib/utils';

export interface EmpleadoInlineFormProps {
  empleado?: EmpleadoListItem | null;
  sucursalIdInicial?: string;
  sucursalFija?: boolean;
  onCancel: () => void;
  onSaved?: () => void;
}

const VALORES_INICIALES: EmpleadoValues = {
  clave: '',
  nombre: '',
  email: '',
  puestoId: '',
  jefeDirectoId: '',
  sucursalId: '',
  departamentoId: '',
  usuarioId: '',
  codigoNomina: '',
};

const DOMINIOS_CORPORATIVOS = [
  'uzieltzaboutlook.onmicrosoft.com',
  'millet.mx',
  'millet.com.mx',
  'uzieltzaboutlook.com',
];

const DRAFT_STORAGE_KEY = 'millet_empleado_wizard_draft_v1';

export function EmpleadoInlineForm({
  empleado,
  sucursalIdInicial,
  sucursalFija = false,
  onCancel,
  onSaved,
}: EmpleadoInlineFormProps) {
  const esEditar = empleado != null;
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useAltaColaborador();
  const actualizar = useActualizarEmpleado();
  const canCrearUsuarios = useHasPermission(PermisosCanonicos.IdentidadUsuariosCrear);
  const canAsignar = useHasPermission(PermisosCanonicos.IdentidadAsignacionesAdministrar);
  const canDarAcceso = canCrearUsuarios && canAsignar;
  const roles = useRoles({ soloActivos: true, limit: 200 }, !esEditar && canDarAcceso);
  const [paso, setPaso] = useState(0);
  const [acceso, setAcceso] = useState<0 | 1 | 2>(0);
  const [emailContacto, setEmailContacto] = useState('');
  const [rolId, setRolId] = useState('');
  const [correoValidable, setCorreoValidable] = useState('');
  const [borradorCargado, setBorradorCargado] = useState(false);

  // Dominio y prefijo de correo corporativo para facilitar input
  const emailInicial = empleado?.email ?? '';
  const [emailPrefix, setEmailPrefix] = useState(() => {
    if (!emailInicial) return '';
    const at = emailInicial.indexOf('@');
    return at > 0 ? emailInicial.slice(0, at) : emailInicial;
  });
  const [emailDomain, setEmailDomain] = useState(() => {
    if (!emailInicial) return DOMINIOS_CORPORATIVOS[0];
    const at = emailInicial.indexOf('@');
    return at > 0 ? emailInicial.slice(at + 1) : DOMINIOS_CORPORATIVOS[0];
  });

  const form = useForm<EmpleadoValues>({
    resolver: zodResolver(EmpleadoSchema),
    defaultValues: esEditar
      ? {
          clave: empleado.clave,
          nombre: empleado.nombre,
          email: empleado.email ?? '',
          puestoId: empleado.puestoId ?? '',
          jefeDirectoId: empleado.jefeDirectoId ?? '',
          sucursalId: empleado.sucursalId ?? '',
          departamentoId: empleado.departamentoId ?? '',
          usuarioId: '',
          codigoNomina: '',
        }
      : { ...VALORES_INICIALES, sucursalId: sucursalIdInicial ?? '' },
  });

  // Cargar borrador de sessionStorage en modo alta
  useEffect(() => {
    if (esEditar) {
      form.setFocus('nombre');
      return;
    }
    form.setFocus('clave');

    try {
      const raw = sessionStorage.getItem(DRAFT_STORAGE_KEY);
      if (raw) {
        const parsed = JSON.parse(raw);
        if (parsed.values) {
          form.reset(parsed.values);
          if (typeof parsed.paso === 'number') setPaso(parsed.paso);
          if (typeof parsed.acceso === 'number') setAcceso(parsed.acceso);
          if (parsed.emailContacto) setEmailContacto(parsed.emailContacto);
          if (parsed.rolId) setRolId(parsed.rolId);
          if (parsed.emailPrefix !== undefined) setEmailPrefix(parsed.emailPrefix);
          if (parsed.emailDomain !== undefined) setEmailDomain(parsed.emailDomain);
          setBorradorCargado(true);
        }
      }
    } catch {
      // ignore JSON error
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Auto-guardado de borrador
  useEffect(() => {
    if (esEditar) return;
    const values = form.getValues();
    if (values.clave || values.nombre || values.email || values.sucursalId || values.departamentoId || values.puestoId) {
      sessionStorage.setItem(
        DRAFT_STORAGE_KEY,
        JSON.stringify({
          values,
          paso,
          acceso,
          emailContacto,
          rolId,
          emailPrefix,
          emailDomain,
        }),
      );
    }
  }, [esEditar, form, paso, acceso, emailContacto, rolId, emailPrefix, emailDomain]);

  const puestoSeleccionado = form.watch('puestoId');
  const sucursalSeleccionada = form.watch('sucursalId');
  const departamentoSeleccionado = form.watch('departamentoId');
  const correoCorporativo = form.watch('email');

  useEffect(() => {
    const timer = setTimeout(() => setCorreoValidable(correoCorporativo?.trim() ?? ''), 350);
    return () => clearTimeout(timer);
  }, [correoCorporativo]);

  const validacion = useValidarCorreoCorporativo(
    correoValidable, !esEditar && paso === 3 && acceso !== 0 && canDarAcceso,
  );

  // Rol sugerido por la asignación puntual (sucursal+puesto+departamento) —
  // con Parte E (un puesto en varios departamentos) el rol sugerido ya
  // NO es propiedad del puesto solo: cada asignación puede traer su
  // propia excepción (rolSugeridoId) y si no, hereda el del puesto
  // (rolSugeridoEfectivoId = asignación ?? puesto). Solo precarga el
  // select si el usuario aún no tocó el rol; sigue siendo editable en
  // todo momento (el hint junto al select avisa que es sugerencia, no
  // asignación — 01-04).
  const puestosDeSucursal = usePuestosDeSucursal(
    !esEditar ? sucursalSeleccionada || null : null,
    !esEditar ? departamentoSeleccionado || null : null,
  );
  const rolSugeridoEfectivoId = esEditar
    ? undefined
    : puestosDeSucursal.data?.items.find((p) => p.puestoId === puestoSeleccionado)
        ?.rolSugeridoEfectivoId;

  useEffect(() => {
    if (esEditar || !puestoSeleccionado || rolId) return;
    if (rolSugeridoEfectivoId) setRolId(rolSugeridoEfectivoId);
  }, [esEditar, puestoSeleccionado, rolSugeridoEfectivoId, rolId]);

  const rolSeleccionado = roles.data?.items.find((r) => r.id === rolId);

  const isPending = crear.isPending || actualizar.isPending;

  function handleEmailPrefixChange(val: string) {
    if (val.includes('@')) {
      const parts = val.split('@');
      const prefix = parts[0];
      const domain = parts.slice(1).join('@');
      setEmailPrefix(prefix);
      if (domain) setEmailDomain(domain);
      const full = prefix.trim() && domain.trim() ? `${prefix.trim()}@${domain.trim()}` : val.trim();
      form.setValue('email', full, { shouldValidate: true });
    } else {
      setEmailPrefix(val);
      const full = val.trim() ? `${val.trim()}@${emailDomain.trim()}` : '';
      form.setValue('email', full, { shouldValidate: true });
    }
  }

  function handleEmailDomainChange(domain: string) {
    setEmailDomain(domain);
    const full = emailPrefix.trim() ? `${emailPrefix.trim()}@${domain.trim()}` : '';
    form.setValue('email', full, { shouldValidate: true });
  }

  function limpiarBorrador() {
    sessionStorage.removeItem(DRAFT_STORAGE_KEY);
    form.reset({ ...VALORES_INICIALES, sucursalId: sucursalIdInicial ?? '' });
    setPaso(0);
    setAcceso(0);
    setEmailContacto('');
    setRolId('');
    setEmailPrefix('');
    setEmailDomain(DOMINIOS_CORPORATIVOS[0]);
    setBorradorCargado(false);
    toast.info('Borrador eliminado');
  }

  function onError(error: Error) {
    if (esApiError(error)) {
      if (error.code === 'EMPLEADO_CLAVE_DUPLICADA') {
        form.setError('clave', {
          type: error.code,
          message: 'Ya existe un empleado con esa clave en la empresa.',
        });
        return;
      }
      if (
        applyServerErrors(
          form as unknown as Parameters<typeof applyServerErrors>[0],
          error,
        )
      ) {
        return;
      }
      toast.error(error.problem.title, {
        description: error.traceId ? `Código: ${error.traceId}` : undefined,
      });
      return;
    }
    toast.error('Error inesperado al guardar el empleado.');
  }

  function onSubmit(values: EmpleadoValues) {
    if (esEditar && empleado != null) {
      actualizar.mutate(
        { id: empleado.id, payload: payloadPatch(values, empleado, emailContacto), idempotencyKey },
        {
          onSuccess: () => {
            toast.success('Empleado actualizado');
            onSaved?.();
          },
          onError,
        },
      );
      return;
    }

    if (!values.sucursalId || !values.departamentoId || !values.puestoId) {
      toast.error('Selecciona sucursal, departamento y puesto para el alta.');
      return;
    }
    if (acceso !== 0 && (!canDarAcceso || !values.email || !rolId)) {
      toast.error('Para dar acceso, indica correo corporativo y rol.');
      return;
    }
    if (acceso === 2 && !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(emailContacto.trim())) {
      toast.error('Indica un correo de contacto válido para una cuenta nueva.');
      return;
    }

    crear.mutate(
      {
        command: {
          id: '00000000-0000-0000-0000-000000000000',
          clave: values.clave,
          nombre: values.nombre,
          puestoId: values.puestoId,
          jefeDirectoId: values.jefeDirectoId || null,
          sucursalId: values.sucursalId,
          departamentoId: values.departamentoId,
          acceso,
          correoCorporativo: values.email || null,
          emailContacto: acceso === 2 ? emailContacto.trim() : null,
          rolId: acceso === 0 ? null : rolId,
          codigoNomina: values.codigoNomina || null,
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          sessionStorage.removeItem(DRAFT_STORAGE_KEY);
          toast.success(`Colaborador "${resp.empleado.nombre}" agregado`, {
            description: acceso === 2
              ? 'La cuenta queda en provisión; revisa su estado en Acceso.'
              : undefined,
          });
          form.reset({ ...VALORES_INICIALES, sucursalId: sucursalIdInicial ?? '' });
          form.setFocus('clave');
          onSaved?.();
        },
        onError,
      },
    );
  }

  function siguientePaso() {
    const values = form.getValues();
    if (paso === 0 && (!values.clave.trim() || !values.nombre.trim() || !values.sucursalId)) {
      toast.error('Indica clave, nombre y sucursal.');
      return;
    }
    if (paso === 1 && !values.departamentoId) {
      toast.error('Selecciona el departamento.');
      return;
    }
    if (paso === 2 && !values.puestoId) {
      toast.error('Selecciona el puesto.');
      return;
    }
    if (paso === 3 && acceso !== 0 && (!values.email || !rolId)) {
      toast.error('Indica correo corporativo y rol para dar acceso.');
      return;
    }
    if (
      paso === 3 &&
      acceso !== 0 &&
      (correoValidable !== values.email?.trim() ||
        validacion.isPending ||
        validacion.isError ||
        !validacion.data?.dominioPermitido ||
        (acceso === 1
          ? !validacion.data?.puedeVincularCuentaExistente
          : !validacion.data?.puedeCrearCuentaNueva))
    ) {
      if (validacion.data && !validacion.data.dominioPermitido) {
        toast.error(`El dominio del correo no está permitido. Dominios permitidos: ${DOMINIOS_CORPORATIVOS.join(', ')}`);
      } else {
        toast.error('El correo no está disponible para el modo de acceso elegido.');
      }
      return;
    }
    if (paso === 3 && acceso === 2 && !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(emailContacto.trim())) {
      toast.error('Indica un correo de contacto válido.');
      return;
    }
    setPaso((p) => Math.min(p + 1, 4));
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
        esEditar
          ? 'border-amber-400 bg-amber-50/40'
          : 'border-dashed border-primary/40 bg-primary/5',
      )}
      aria-label={
        esEditar ? `Editar empleado ${empleado?.clave}` : 'Agregar empleado'
      }
    >
      {!esEditar && borradorCargado && (
        <div className="flex items-center justify-between rounded-md border border-amber-500/30 bg-amber-500/10 px-3 py-1.5 text-xs text-amber-900 dark:text-amber-200">
          <span>Se restauró un borrador previo del formulario de alta de empleado.</span>
          <button
            type="button"
            onClick={limpiarBorrador}
            className="flex items-center gap-1 font-medium underline hover:text-amber-700"
          >
            <RotateCcw className="h-3 w-3" />
            Limpiar borrador
          </button>
        </div>
      )}

      {!esEditar && (
        <div className="rounded-md bg-muted/40 px-3 py-2 text-sm" aria-live="polite">
          <span className="font-medium">Paso {paso + 1} de 5:</span>{' '}
          {['Persona y sucursal', 'Departamento', 'Puesto', 'Acceso y rol', 'Confirmación'][paso]}
        </div>
      )}

      <div className="grid grid-cols-1 gap-2 md:grid-cols-12">
        <Field
          label="Clave"
          required
          error={form.formState.errors.clave?.message}
          className={cn('md:col-span-3', !esEditar && paso !== 0 && 'hidden')}
        >
          <Input
            maxLength={20}
            placeholder="EMP-001"
            className="font-mono"
            disabled={esEditar}
            title={
              esEditar
                ? 'La clave es inmutable (business key).'
                : 'Clave única del empleado en la empresa.'
            }
            {...form.register('clave')}
          />
        </Field>

        <Field
          label="Nombre"
          required
          error={form.formState.errors.nombre?.message}
          className={cn('md:col-span-5', !esEditar && paso !== 0 && 'hidden')}
        >
          <Input
            maxLength={254}
            placeholder="Juana Pérez"
            {...form.register('nombre')}
          />
        </Field>

        <Field
          label={esEditar ? 'Email' : 'Correo corporativo'}
          error={form.formState.errors.email?.message}
          className={cn('md:col-span-4', !esEditar && paso !== 0 && paso !== 3 && 'hidden')}
        >
          {esEditar ? (
            <Input
              maxLength={254}
              type="email"
              placeholder="juana.perez@millet.mx"
              {...form.register('email')}
            />
          ) : (
            <div className="flex items-center">
              <Input
                maxLength={100}
                placeholder="juana.perez"
                value={emailPrefix}
                onChange={(e) => handleEmailPrefixChange(e.target.value)}
                className="rounded-r-none border-r-0 focus:z-10"
              />
              <div className="flex h-9 items-center rounded-r-md border border-l-0 bg-muted px-2.5 text-xs text-muted-foreground">
                <span className="mr-1 font-semibold text-foreground">@</span>
                <select
                  value={DOMINIOS_CORPORATIVOS.includes(emailDomain) ? emailDomain : DOMINIOS_CORPORATIVOS[0]}
                  onChange={(e) => handleEmailDomainChange(e.target.value)}
                  className="cursor-pointer bg-transparent text-xs font-medium text-foreground outline-none"
                  aria-label="Dominio corporativo"
                >
                  {DOMINIOS_CORPORATIVOS.map((d) => (
                    <option key={d} value={d}>
                      {d}
                    </option>
                  ))}
                </select>
              </div>
            </div>
          )}
        </Field>

        <Field
          label={sucursalFija ? 'Sucursal (fijada)' : 'Sucursal'}
          className={cn('md:col-span-4', !esEditar && paso !== 0 && 'hidden')}
        >
          <Controller
            control={form.control}
            name="sucursalId"
            render={({ field }) => (
              <SucursalSelector
                value={field.value || null}
                onChange={(id) => {
                  const nuevo = id ?? '';
                  if (nuevo !== field.value) {
                    field.onChange(nuevo);
                    form.setValue('puestoId', '');
                    form.setValue('departamentoId', '');
                  }
                }}
                disabled={sucursalFija}
                className="w-full"
              />
            )}
          />
        </Field>

        <Field
          label="Departamento"
          error={form.formState.errors.departamentoId?.message}
          className={cn('md:col-span-4', !esEditar && paso !== 1 && 'hidden')}
        >
          <Controller
            control={form.control}
            name="departamentoId"
            render={({ field }) => (
              <DepartamentoSelector
                value={field.value || null}
                onChange={(id) => {
                  const nuevo = id ?? '';
                  if (nuevo !== field.value) {
                    field.onChange(nuevo);
                    form.setValue('puestoId', '');
                  }
                }}
                sucursalId={form.watch('sucursalId') || undefined}
                className="w-full"
              />
            )}
          />
        </Field>

        <Field label="Puesto" className={cn('md:col-span-4', !esEditar && paso !== 2 && 'hidden')}>
          <Controller
            control={form.control}
            name="puestoId"
            render={({ field }) => (
              <PuestoSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
                sucursalId={form.watch('sucursalId') || undefined}
                departamentoId={form.watch('departamentoId') || undefined}
                className="w-full"
              />
            )}
          />
          {!esEditar && paso === 2 && sucursalSeleccionada && departamentoSeleccionado && (
            <p className="mt-1 text-xs text-muted-foreground">
              Si el puesto que buscas no aparece para este departamento, puedes agregarlo/vincularlo en el menú de Sucursales. 
              <span className="font-medium text-amber-700 dark:text-amber-400"> Tu avance se guarda como borrador.</span>
            </p>
          )}
        </Field>

        <Field label="Jefe directo (autoriza viáticos N1)" className={cn('md:col-span-4', !esEditar && paso !== 0 && 'hidden')}>
          <Controller
            control={form.control}
            name="jefeDirectoId"
            render={({ field }) => (
              <EmpleadoSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
                excludeId={empleado?.id}
                className="w-full"
              />
            )}
          />
        </Field>

        <Field
          label="Código de nómina"
          error={form.formState.errors.codigoNomina?.message}
          className={cn('md:col-span-4', !esEditar && paso !== 0 && 'hidden')}
        >
          <Input
            maxLength={20}
            placeholder="A1234"
            className="font-mono"
            title="Referencia al código de nómina/SAP (Axxxx). Sin lógica en el ERP."
            {...form.register('codigoNomina')}
          />
        </Field>
        {esEditar && (
          <Field label="Actualizar correo de contacto" className="md:col-span-4">
            <Input
              type="email"
              value={emailContacto}
              onChange={(e) => setEmailContacto(e.target.value)}
              placeholder="Vacío: conservar el correo actual"
            />
          </Field>
        )}
      </div>

      {!esEditar && paso === 3 && (
        <div className="grid grid-cols-1 gap-3 border-t pt-3 md:grid-cols-3">
          <Field label="Acceso al ERP">
            <select
              aria-label="Acceso al ERP"
              className="h-9 w-full rounded-md border bg-background px-3 text-sm"
              value={acceso}
              onChange={(e) => setAcceso(Number(e.target.value) as 0 | 1 | 2)}
            >
              <option value={0}>Sin acceso</option>
              {canDarAcceso && <option value={1}>Ya tiene cuenta Microsoft</option>}
              {canDarAcceso && <option value={2}>Cuenta Microsoft nueva</option>}
            </select>
          </Field>
          {acceso !== 0 && (
            <Field label="Rol en la empresa" required>
              <select
                aria-label="Rol en la empresa"
                className="h-9 w-full rounded-md border bg-background px-3 text-sm"
                value={rolId}
                onChange={(e) => setRolId(e.target.value)}
              >
                <option value="">Selecciona un rol</option>
                {(roles.data?.items ?? []).filter((r) => r.activo).map((r) => (
                  <option key={r.id} value={r.id}>
                    {r.nombre}
                  </option>
                ))}
              </select>
              {rolSugeridoEfectivoId && rolId === rolSugeridoEfectivoId && (
                <Badge
                  variant="secondary"
                  className="mt-1 font-normal text-muted-foreground"
                >
                  Sugerido por el puesto — puedes cambiarlo
                </Badge>
              )}
            </Field>
          )}

          {acceso !== 0 && correoValidable && (
            <div className="rounded-md border p-2.5 text-xs md:col-span-3" role="status" aria-live="polite">
              {validacion.isPending ? (
                <div className="flex items-center gap-2 text-muted-foreground">
                  <Loader2 className="h-4 w-4 animate-spin text-primary shrink-0" />
                  <span>
                    Verificando disponibilidad de la cuenta Microsoft para <strong className="font-mono">{correoValidable}</strong>…
                  </span>
                </div>
              ) : validacion.isError ? (
                <div className="flex items-center gap-2 text-rose-600 font-medium">
                  <AlertCircle className="h-4 w-4 shrink-0" />
                  <span>No se pudo verificar el correo contra Microsoft Entra ID.</span>
                </div>
              ) : validacion.data ? (
                !validacion.data.dominioPermitido ? (
                  <div className="flex items-start gap-2 text-rose-600">
                    <AlertCircle className="h-4 w-4 mt-0.5 shrink-0" />
                    <div>
                      <p className="font-semibold">Dominio no permitido para cuenta corporativa.</p>
                      <p className="mt-0.5 text-[11px] text-muted-foreground">
                        El dominio del correo <strong>{correoValidable}</strong> no está dentro de los dominios autorizados ({DOMINIOS_CORPORATIVOS.join(', ')}).
                      </p>
                    </div>
                  </div>
                ) : acceso === 1 ? (
                  validacion.data.puedeVincularCuentaExistente ? (
                    <div className="flex items-center gap-2 font-medium text-emerald-600 dark:text-emerald-400">
                      <CheckCircle2 className="h-4 w-4 shrink-0" />
                      <span>
                        Cuenta Microsoft encontrada: <strong>{validacion.data.cuentaEntra?.nombreMostrado}</strong>
                      </span>
                    </div>
                  ) : (
                    <div className="flex items-center gap-2 font-medium text-amber-600 dark:text-amber-400">
                      <AlertTriangle className="h-4 w-4 shrink-0" />
                      <span>No se encontró una cuenta Microsoft activa con {correoValidable} para vincular.</span>
                    </div>
                  )
                ) : validacion.data.puedeCrearCuentaNueva ? (
                  <div className="flex items-center gap-2 font-medium text-emerald-600 dark:text-emerald-400">
                    <CheckCircle2 className="h-4 w-4 shrink-0" />
                    <span>
                      Correo corporativo <strong className="font-mono">{correoValidable}</strong> disponible para crear cuenta Microsoft nueva.
                    </span>
                  </div>
                ) : (
                  <div className="flex items-start gap-2 text-rose-600">
                    <AlertCircle className="h-4 w-4 mt-0.5 shrink-0" />
                    <div>
                      <p className="font-semibold">El correo no está disponible para una cuenta nueva.</p>
                      <p className="mt-0.5 text-[11px] text-muted-foreground">
                        {validacion.data.cuentaEntra
                          ? 'Ya existe una cuenta en Entra ID con este correo.'
                          : 'Ya existe un usuario en el ERP registrado con este correo.'}
                      </p>
                    </div>
                  </div>
                )
              ) : null}
            </div>
          )}

          {acceso === 2 && (
            <Field label="Correo personal de contacto" required>
              <Input
                type="email"
                value={emailContacto}
                onChange={(e) => setEmailContacto(e.target.value)}
                placeholder="contacto@ejemplo.com"
              />
            </Field>
          )}
          {acceso === 2 && (
            <p className="text-xs text-amber-700 md:col-span-3">
              Se enviará una contraseña temporal al correo de contacto cuando el entorno tenga Graph y correo habilitados.
            </p>
          )}
          {acceso === 1 && (
            <p className="text-xs text-amber-700 md:col-span-3">
              El ERP verificará la cuenta Microsoft al guardar; el resultado depende del proveedor configurado en este entorno.
            </p>
          )}
        </div>
      )}

      {!esEditar && paso === 4 && (
        <div className="rounded-md border bg-muted/20 p-3 text-sm space-y-1">
          <p className="font-medium">Revisa antes de crear</p>
          <p>
            {form.getValues('nombre')} · <span className="font-mono">{form.getValues('clave')}</span>
          </p>
          <p className="text-muted-foreground text-xs">Sucursal, departamento y puesto seleccionados.</p>
          <p>
            {acceso === 0
              ? 'Sin acceso al ERP'
              : `${acceso === 1 ? 'Cuenta Microsoft existente' : 'Cuenta Microsoft nueva'} · ${form.getValues('email')}`}
          </p>
          {acceso !== 0 && (
            <p className="text-xs text-muted-foreground">
              Rol a asignar:{' '}
              <span className="font-medium text-foreground">
                {rolSeleccionado?.nombre ?? '—'}
              </span>
            </p>
          )}
          {acceso === 2 && <p className="text-xs text-muted-foreground">La contraseña temporal se enviará a {emailContacto}.</p>}
        </div>
      )}

      <div className="flex items-center justify-end gap-2">
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
          {!esEditar && paso > 0 && (
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={isPending}
              onClick={() => setPaso((p) => p - 1)}
            >
              Anterior
            </Button>
          )}
          {!esEditar && paso < 4 && (
            <Button type="button" size="sm" disabled={isPending} onClick={siguientePaso}>
              Siguiente
            </Button>
          )}
          {(esEditar || paso === 4) && (
            <Button type="submit" size="sm" disabled={isPending}>
              {esEditar ? (
                <>
                  <Check className="mr-1 h-4 w-4" />
                  {isPending ? 'Guardando…' : 'Guardar cambios'}
                </>
              ) : (
                <>
                  <Plus className="mr-1 h-4 w-4" />
                  {isPending ? 'Agregando…' : 'Agregar empleado'}
                </>
              )}
            </Button>
          )}
        </div>
      </div>
    </form>
  );
}

function payloadPatch(
  values: EmpleadoValues,
  anterior: EmpleadoListItem,
  emailContacto: string,
): ActualizarEmpleadoPayload {
  const campo = (nuevo: string | undefined, previo: string | null) => {
    const v = nuevo?.trim() ?? '';
    return {
      valor: v === '' ? null : v,
      limpiar: v === '' && previo != null,
    };
  };

  const email = campo(values.email, anterior.email);
  const puesto = campo(values.puestoId, anterior.puestoId);
  const jefe = campo(values.jefeDirectoId, anterior.jefeDirectoId);
  const sucursal = campo(values.sucursalId, anterior.sucursalId);
  const departamento = campo(values.departamentoId, anterior.departamentoId);
  const codigoNomina = values.codigoNomina?.trim() || null;

  return {
    nombre: values.nombre,
    email: email.valor,
    limpiarEmail: email.limpiar,
    puestoId: puesto.valor,
    limpiarPuesto: puesto.limpiar,
    jefeDirectoId: jefe.valor,
    limpiarJefeDirecto: jefe.limpiar,
    sucursalId: sucursal.valor,
    limpiarSucursal: sucursal.limpiar,
    departamentoId: departamento.valor,
    limpiarDepartamento: departamento.limpiar,
    codigoNomina,
    emailContacto: emailContacto.trim() || null,
  };
}

interface FieldProps {
  label: string;
  required?: boolean;
  error?: string;
  className?: string;
  children: React.ReactNode;
}

function Field({ label, required, error, className, children }: FieldProps) {
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
