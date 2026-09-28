import { useState, useEffect } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { AlertCircle, CheckCircle2, Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { adminKeys } from '@/modules/administracion/api/keys';
import {
  useAccesoColaborador,
  useAccionAccesoColaborador,
  useDarAccesoColaborador,
  useValidarCorreoCorporativo,
} from '@/modules/administracion/api/empleados';
import { DOMINIOS_CORPORATIVOS } from '@/modules/administracion/components/EmpleadoInlineForm';
import { useRoles } from '@/modules/identidad/api/roles';
import { useReactivarUsuario } from '@/modules/identidad/api/usuarios';

interface Props {
  empleadoId: string;
  usuarioId: string | null;
  email: string | null;
  emailContacto?: string | null;
  empleadoActivo: boolean;
}

const ESTADOS = ['Acceso activo', 'Pendiente de primer acceso', 'Creando cuenta Microsoft', 'Error de provisión'];

export function EmpleadoAccesoPanel({ empleadoId, usuarioId, email, emailContacto, empleadoActivo }: Props) {
  const queryClient = useQueryClient();
  const puedeCrear = useHasPermission(PermisosCanonicos.IdentidadUsuariosCrear);
  const puedeAsignar = useHasPermission(PermisosCanonicos.IdentidadAsignacionesAdministrar);
  const puedeEditar = useHasPermission(PermisosCanonicos.IdentidadUsuariosEditar);
  const [tipo, setTipo] = useState<1 | 2>(1);

  const emailInicial = email ?? '';
  const [emailPrefix, setEmailPrefix] = useState(() => {
    if (!emailInicial) return '';
    const at = emailInicial.indexOf('@');
    return at > 0 ? emailInicial.slice(0, at) : emailInicial;
  });
  const [emailDomain, setEmailDomain] = useState(() => {
    if (!emailInicial) return DOMINIOS_CORPORATIVOS[0];
    const at = emailInicial.indexOf('@');
    const rawDomain = at > 0 ? emailInicial.slice(at + 1).toLowerCase() : '';
    const matched = DOMINIOS_CORPORATIVOS.find((d) => d.toLowerCase() === rawDomain);
    return matched ?? DOMINIOS_CORPORATIVOS[0];
  });

  const [contacto, setContacto] = useState(() => emailContacto ?? '');
  const [rolId, setRolId] = useState('');

  useEffect(() => {
    if (email) {
      const at = email.indexOf('@');
      const prefix = at > 0 ? email.slice(0, at) : email;
      const rawDomain = at > 0 ? email.slice(at + 1).toLowerCase() : '';
      const matched = DOMINIOS_CORPORATIVOS.find((d) => d.toLowerCase() === rawDomain);
      setEmailPrefix(prefix);
      setEmailDomain(matched ?? DOMINIOS_CORPORATIVOS[0]);
    } else {
      setEmailPrefix('');
      setEmailDomain(DOMINIOS_CORPORATIVOS[0]);
    }
    if (emailContacto !== undefined) {
      setContacto(emailContacto ?? '');
    }
  }, [email, emailContacto]);

  const activeDomain = DOMINIOS_CORPORATIVOS.includes(emailDomain) ? emailDomain : DOMINIOS_CORPORATIVOS[0];
  const correoFinal = emailPrefix.trim() ? `${emailPrefix.trim()}@${activeDomain}` : '';

  const [correoValidable, setCorreoValidable] = useState(correoFinal);

  useEffect(() => {
    const timer = setTimeout(() => {
      setCorreoValidable(correoFinal);
    }, 350);
    return () => clearTimeout(timer);
  }, [correoFinal]);

  const roles = useRoles({ soloActivos: true, limit: 200 }, usuarioId == null && puedeCrear && puedeAsignar);
  const acceso = useAccesoColaborador(usuarioId ? empleadoId : null);
  const dar = useDarAccesoColaborador();
  const reintentar = useAccionAccesoColaborador('reintentar');
  const reenviar = useAccionAccesoColaborador('reenviar');
  const reactivar = useReactivarUsuario();

  const validacionHabilitada =
    usuarioId == null && puedeCrear && puedeAsignar && !!correoValidable;

  const validacion = useValidarCorreoCorporativo(
    correoValidable,
    validacionHabilitada,
  );

  function handleEmailPrefixChange(val: string) {
    if (val.includes('@')) {
      const parts = val.split('@');
      const prefix = parts[0];
      const domain = parts.slice(1).join('@').toLowerCase();
      setEmailPrefix(prefix);
      const matchedDomain = DOMINIOS_CORPORATIVOS.find((d) => d.toLowerCase() === domain);
      if (matchedDomain) {
        setEmailDomain(matchedDomain);
      }
    } else {
      setEmailPrefix(val);
    }
  }

  function handleEmailDomainChange(domain: string) {
    setEmailDomain(domain);
  }

  function error(err: Error) {
    if (esApiError(err)) {
      if (err.code === 'ENTRA_DOMINIO_NO_PERMITIDO') {
        toast.error(`El dominio del correo no está permitido. Dominios permitidos: ${DOMINIOS_CORPORATIVOS.join(', ')}`);
        return;
      }
      if (err.code === 'ENTRA_CUENTA_NO_ENCONTRADA') {
        toast.error('No se encontró una cuenta en Microsoft Entra ID para ese correo.');
        return;
      }
      if (err.code === 'ENTRA_CUENTA_DESHABILITADA') {
        toast.error('La cuenta en Microsoft Entra ID se encuentra deshabilitada.');
        return;
      }
      if (err.code === 'ENTRA_UPN_EN_USO') {
        toast.error('Ya existe una cuenta en Microsoft Entra ID con ese correo.');
        return;
      }
      if (err.code === 'USUARIO_EMAIL_DUPLICADO') {
        toast.error('Ya existe un usuario en el ERP registrado con ese correo.');
        return;
      }
      if (err.code === 'COLABORADOR_YA_TIENE_ACCESO') {
        toast.error('Este colaborador ya cuenta con un usuario y acceso asignado.');
        return;
      }
      if (err.code === 'USUARIO_NO_REUTILIZABLE') {
        toast.error('La cuenta ya está vinculada a otro colaborador, inactiva o es técnica.');
        return;
      }
    }
    toast.error(esApiError(err) ? err.problem.title : 'No se pudo actualizar el acceso.');
  }

  if (!empleadoActivo) {
    return <p className="text-sm text-muted-foreground">Reactiva al empleado antes de gestionar su acceso. Su usuario permanece bloqueado.</p>;
  }

  if (usuarioId == null) {
    if (!puedeCrear || !puedeAsignar) {
      return <p className="text-sm text-muted-foreground">Sin acceso al ERP. No tienes permisos para crearlo.</p>;
    }
    return (
      <form
        className="grid gap-2.5 rounded-md border p-3 md:grid-cols-2"
        onSubmit={(event) => {
          event.preventDefault();
          const correo = correoFinal;
          if (!emailPrefix.trim() || !rolId || !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(correo)) {
            toast.error('Indica correo corporativo y rol válidos.');
            return;
          }
          if (!DOMINIOS_CORPORATIVOS.includes(emailDomain)) {
            toast.error(`El dominio del correo no está permitido. Dominios permitidos: ${DOMINIOS_CORPORATIVOS.join(', ')}`);
            return;
          }
          if (tipo === 2 && !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(contacto.trim())) {
            toast.error('Indica el correo personal para enviar la contraseña temporal.');
            return;
          }
          if (
            correoValidable !== correo ||
            validacion.isPending ||
            validacion.isError ||
            !validacion.data?.dominioPermitido ||
            (tipo === 1
              ? !validacion.data?.puedeVincularCuentaExistente
              : !validacion.data?.puedeCrearCuentaNueva)
          ) {
            if (validacion.data && !validacion.data.dominioPermitido) {
              toast.error(`El dominio del correo no está permitido. Dominios permitidos: ${DOMINIOS_CORPORATIVOS.join(', ')}`);
            } else if (tipo === 1) {
              if (validacion.data?.empleadoVinculado) {
                toast.error(`Esta cuenta ya está vinculada al colaborador ${validacion.data.empleadoVinculado.nombre} (${validacion.data.empleadoVinculado.clave}).`);
              } else if (validacion.data?.usuarioErp && !validacion.data.usuarioErp.activo) {
                toast.error('El usuario existe en el ERP pero se encuentra desactivado.');
              } else if (validacion.data?.cuentaEntra && !validacion.data.cuentaEntra.habilitada) {
                toast.error('La cuenta Microsoft se encuentra deshabilitada en el directorio.');
              } else if (!validacion.data?.cuentaEntra) {
                toast.error(`No se encontró una cuenta Microsoft para vincular con "${correo}".`);
              } else {
                toast.error('La cuenta Microsoft no puede ser vinculada al colaborador.');
              }
            } else {
              if (validacion.data?.cuentaEntra) {
                toast.error('Ya existe una cuenta en Microsoft con este correo.');
              } else if (validacion.data?.empleadoVinculado) {
                toast.error(`Ya existe un colaborador registrado con este correo (${validacion.data.empleadoVinculado.nombre}).`);
              } else if (validacion.data?.usuarioErp) {
                toast.error('Ya existe un usuario en el ERP registrado con este correo.');
              } else {
                toast.error('El correo no está disponible para una cuenta nueva.');
              }
            }
            return;
          }
          dar.mutate({
            empleadoId,
            acceso: tipo,
            correoCorporativo: correo,
            emailContacto: tipo === 2 ? contacto.trim() : null,
            rolId,
            idempotencyKey: crypto.randomUUID(),
          }, {
            onSuccess: () => {
              queryClient.invalidateQueries({ queryKey: adminKeys.empleados() });
              queryClient.invalidateQueries({ queryKey: ['catalogos', 'empleados'] });
              toast.success(tipo === 2
                ? 'Cuenta en provisión. Revisa su estado antes de confirmar el envío.'
                : 'Cuenta Microsoft vinculada al colaborador.');
            },
            onError: error,
          });
        }}
      >
        <div className="md:col-span-2 text-sm font-medium">Dar acceso al ERP</div>
        <label className="text-xs">Modo de acceso
          <select
            className="mt-1 h-9 w-full rounded-md border bg-background px-2 text-sm"
            value={tipo}
            onChange={(e) => setTipo(Number(e.target.value) as 1 | 2)}
          >
            <option value={1}>Ya tiene cuenta Microsoft</option>
            <option value={2}>Crear cuenta Microsoft</option>
          </select>
        </label>
        <label className="text-xs">Rol en la empresa
          <select
            className="mt-1 h-9 w-full rounded-md border bg-background px-2 text-sm"
            value={rolId}
            onChange={(e) => setRolId(e.target.value)}
            required
          >
            <option value="">Selecciona un rol</option>
            {(roles.data?.items ?? []).filter((r) => r.activo).map((r) => (
              <option key={r.id} value={r.id}>{r.nombre}</option>
            ))}
          </select>
        </label>

        <div className="md:col-span-2">
          <label className="text-xs font-medium">Correo corporativo</label>
          <div className="flex items-center w-full mt-1">
            <Input
              type="text"
              maxLength={100}
              placeholder="juana.perez"
              value={emailPrefix}
              onChange={(e) => handleEmailPrefixChange(e.target.value)}
              className="flex-1 min-w-0 rounded-r-none border-r-0 focus:z-10"
              required
            />
            <div className="flex h-9 shrink-0 items-center rounded-r-md border border-l-0 bg-muted px-3 text-xs text-muted-foreground">
              <span className="mr-1.5 font-semibold text-foreground">@</span>
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
          {emailPrefix.trim() ? (
            <p className="mt-1 text-xs text-muted-foreground">
              Correo resultante:{' '}
              <span className="font-mono font-medium text-foreground">
                {correoFinal}
              </span>
            </p>
          ) : (
            <p className="mt-1 text-xs text-muted-foreground">
              Indica el nombre de usuario del correo corporativo
            </p>
          )}
        </div>

        {correoValidable && (
          <div className="rounded-md border p-2.5 text-xs md:col-span-2" role="status" aria-live="polite">
            {validacion.isPending ? (
              <div className="flex items-center gap-2 text-muted-foreground">
                <Loader2 className="h-4 w-4 animate-spin text-primary shrink-0" />
                <span>
                  Verificando cuenta Microsoft en Entra ID para <strong className="font-mono">{correoValidable}</strong>…
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
              ) : tipo === 1 ? (
                !validacion.data.puedeVincularCuentaExistente ? (
                  <div className="flex items-start gap-2 text-rose-600">
                    <AlertCircle className="h-4 w-4 mt-0.5 shrink-0" />
                    <div>
                      <p className="font-semibold">No se puede vincular esta cuenta.</p>
                      <p className="mt-0.5 text-[11px] text-muted-foreground">
                        {validacion.data.empleadoVinculado
                          ? `Esta cuenta ya está vinculada al colaborador ${validacion.data.empleadoVinculado.nombre} (${validacion.data.empleadoVinculado.clave}).`
                          : validacion.data.usuarioErp && !validacion.data.usuarioErp.activo
                          ? 'El usuario existe en el ERP pero se encuentra desactivado.'
                          : validacion.data.cuentaEntra && !validacion.data.cuentaEntra.habilitada
                          ? 'La cuenta Microsoft se encuentra deshabilitada en el directorio.'
                          : !validacion.data.cuentaEntra
                          ? 'No se encontró una cuenta Microsoft para este correo. Debe existir previamente en Azure.'
                          : 'La cuenta no cumple las condiciones para vincularse.'}
                      </p>
                    </div>
                  </div>
                ) : (
                  <div className="flex items-center gap-2 font-medium text-emerald-600 dark:text-emerald-400">
                    <CheckCircle2 className="h-4 w-4 shrink-0" />
                    <span>
                      Cuenta Microsoft encontrada en Entra ID: <strong>{validacion.data.cuentaEntra?.nombreMostrado}</strong>
                    </span>
                  </div>
                )
              ) : (
                !validacion.data.puedeCrearCuentaNueva ? (
                  <div className="flex items-start gap-2 text-rose-600">
                    <AlertCircle className="h-4 w-4 mt-0.5 shrink-0" />
                    <div>
                      <p className="font-semibold">El correo no está disponible para cuenta nueva.</p>
                      <p className="mt-0.5 text-[11px] text-muted-foreground">
                        {validacion.data.cuentaEntra
                          ? 'Ya existe una cuenta en Microsoft con este correo.'
                          : validacion.data.empleadoVinculado
                          ? `Ya existe un colaborador registrado con este correo (${validacion.data.empleadoVinculado.nombre}).`
                          : validacion.data.usuarioErp
                          ? 'Ya existe un usuario en el ERP registrado con este correo.'
                          : 'El correo no está disponible para una cuenta nueva.'}
                      </p>
                    </div>
                  </div>
                ) : (
                  <div className="flex items-center gap-2 font-medium text-emerald-600 dark:text-emerald-400">
                    <CheckCircle2 className="h-4 w-4 shrink-0" />
                    <span>Correo disponible para provisión de nueva cuenta en Microsoft Entra ID</span>
                  </div>
                )
              )
            ) : null}
          </div>
        )}

        {tipo === 2 && (
          <label className="text-xs md:col-span-2">
            Correo personal de contacto
            <Input
              className="mt-1"
              type="email"
              value={contacto}
              onChange={(e) => setContacto(e.target.value)}
              placeholder="personal@ejemplo.com"
              required
            />
            <p className="mt-1 text-xs text-muted-foreground">
              La contraseña temporal generada se enviará a este correo personal.
            </p>
          </label>
        )}

        <div className="md:col-span-2 pt-1">
          <Button
            type="submit"
            size="sm"
            disabled={dar.isPending || validacion.isPending}
          >
            {dar.isPending ? 'Guardando acceso…' : 'Dar acceso'}
          </Button>
        </div>
      </form>
    );
  }

  if (acceso.isLoading) return <p className="text-sm text-muted-foreground">Cargando acceso…</p>;
  if (acceso.isError || !acceso.data) return <p className="text-sm text-destructive">No se pudo consultar el acceso.</p>;
  const estado = acceso.data;
  const pendiente = reintentar.isPending || reenviar.isPending || reactivar.isPending;
  return (
    <div className="space-y-2 rounded-md border p-3 text-sm">
      <p><span className="font-medium">{ESTADOS[estado.estadoAcceso]}</span> · {estado.email}</p>
      {!estado.usuarioActivo && <p className="text-amber-700">Usuario bloqueado: no puede entrar al ERP.</p>}
      {estado.motivoErrorProvision && <p className="text-destructive">{estado.motivoErrorProvision}</p>}
      {estado.emailContacto && <p className="text-muted-foreground">Correo de contacto: {estado.emailContacto}</p>}
      {estado.accesoEnviadoEn && <p className="text-muted-foreground">Solicitud de envío aceptada: {new Date(estado.accesoEnviadoEn).toLocaleString()}</p>}
      <div className="flex flex-wrap gap-2">
        {!estado.usuarioActivo && puedeEditar && (
          <Button
            size="sm"
            variant="outline"
            disabled={pendiente}
            onClick={() => reactivar.mutate({ id: usuarioId, idempotencyKey: crypto.randomUUID() }, {
              onSuccess: () => {
                acceso.refetch();
                queryClient.invalidateQueries({ queryKey: adminKeys.empleados() });
                queryClient.invalidateQueries({ queryKey: ['catalogos', 'empleados'] });
                toast.success('Usuario reactivado');
              },
              onError: error,
            })}
          >
            Reactivar acceso
          </Button>
        )}
        {estado.estadoAcceso === 3 && puedeCrear && (
          <Button
            size="sm"
            variant="outline"
            disabled={pendiente}
            onClick={() => reintentar.mutate({ empleadoId, idempotencyKey: crypto.randomUUID() }, {
              onSuccess: () => toast.success('Provisión reintentada'),
              onError: error,
            })}
          >
            Reintentar creación
          </Button>
        )}
        {estado.estadoAcceso === 1 && puedeCrear && estado.usuarioActivo && (
          <Button
            size="sm"
            variant="outline"
            disabled={pendiente}
            onClick={() => reenviar.mutate({ empleadoId, idempotencyKey: crypto.randomUUID() }, {
              onSuccess: () => toast.success('Solicitud de correo aceptada; la entrega queda por verificar.'),
              onError: error,
            })}
          >
            Reenviar acceso
          </Button>
        )}
      </div>
    </div>
  );
}
