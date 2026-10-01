import { useMemo, useState } from 'react';
import { ChevronRight, RotateCcw, ShieldAlert } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '@/components/ui/collapsible';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  useActualizarPermisosOverride,
  usePermisos,
  usePermisosEfectivosUsuario,
  useRestablecerPermisosOverride,
  useRol,
} from '@/modules/identidad/api';
import type {
  EfectoPermiso,
  PermisoOverrideItem,
  PermisoResponse,
} from '@/modules/identidad/api/types';
import { cn } from '@/lib/utils';

/**
 * Panel "Permisos personalizados" (ADR-0053): excepciones por
 * <c>(usuario, empresa)</c> sobre la base del rol,
 * <c>efectivos = (rol ∪ Conceder) \ Denegar</c>.
 *
 * <para>Componente independiente (no depende de ninguna ruta): se monta
 * en el detalle del empleado (Administración → Empleados → Roles y
 * accesos) y opcionalmente en el detalle de usuario. Solo se renderiza
 * con <c>identidad.usuarios.gestionar-permisos</c>; la lectura de
 * efectivos usa <c>identidad.usuarios.leer</c>.</para>
 *
 * <para>Cada permiso es un interruptor que muestra el estado EFECTIVO
 * (ON = la persona lo tiene). Internamente se guarda una excepción solo
 * cuando difiere del rol: apagar un permiso del rol = Denegado, encender
 * uno que el rol no trae = Concedido; volver al valor del rol quita la
 * excepción. Cada módulo tiene un interruptor de grupo (estado mixto) y
 * "Restablecer grupo". Todo opera sobre un borrador; "Guardar cambios"
 * envía el set completo (PUT batch atómico); "Restablecer al rol" borra
 * todas las excepciones (DELETE). Al cambiar el rol del usuario en la
 * empresa las excepciones se borran. Solo el rol super-admin está
 * protegido (no admite excepciones).</para>
 */
export interface PermisosPersonalizadosPanelProps {
  usuarioId: string;
  empresaId: string;
}

type Estado = 'heredado' | 'concedido' | 'denegado';

const ESTADO_A_EFECTO: Record<Exclude<Estado, 'heredado'>, EfectoPermiso> = {
  concedido: 'Conceder',
  denegado: 'Denegar',
};

/** Interruptor accesible (no hay Switch en la UI compartida). */
function Interruptor({
  checked,
  onClick,
  disabled,
  label,
  title,
}: {
  checked: boolean | 'mixed';
  onClick: () => void;
  disabled?: boolean;
  label: string;
  title?: string;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      title={title}
      disabled={disabled}
      onClick={onClick}
      className={cn(
        'relative inline-flex h-5 w-9 shrink-0 items-center rounded-full border transition-colors',
        'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50',
        checked === true && 'border-primary bg-primary',
        checked === false && 'border-input bg-muted',
        checked === 'mixed' && 'border-primary/60 bg-primary/40',
      )}
    >
      <span
        aria-hidden="true"
        className={cn(
          'inline-block h-4 w-4 rounded-full bg-background shadow transition-transform',
          checked === true && 'translate-x-4',
          checked === false && 'translate-x-0.5',
          checked === 'mixed' && 'translate-x-2',
        )}
      />
    </button>
  );
}

export function PermisosPersonalizadosPanel({
  usuarioId,
  empresaId,
}: PermisosPersonalizadosPanelProps) {
  const puedeGestionar = useHasPermission(
    PermisosCanonicos.IdentidadUsuariosGestionarPermisos,
  );
  const permisosDelActor = useAuthStore((s) => s.permisos);
  const actorId = useAuthStore((s) => s.user?.id);

  const efectivos = usePermisosEfectivosUsuario(
    puedeGestionar ? usuarioId : null,
    puedeGestionar ? empresaId : null,
  );
  const rolId = efectivos.data?.rolId ?? null;
  const rol = useRol(rolId);
  const catalogo = usePermisos(true);
  const keyFor = useBodyScopedIdempotencyKey();
  const guardar = useActualizarPermisosOverride();
  const restablecer = useRestablecerPermisosOverride();

  // Estado del servidor: permisoId → estado (solo excepciones) + motivo.
  const servidor = useMemo(() => {
    const estados = new Map<string, Estado>();
    const motivos = new Map<string, string | null>();
    for (const p of efectivos.data?.permisos ?? []) {
      if (p.origen === 'Concedido') estados.set(p.permisoId, 'concedido');
      else if (p.origen === 'Denegado') estados.set(p.permisoId, 'denegado');
      else continue;
      motivos.set(p.permisoId, p.motivo);
    }
    return { estados, motivos };
  }, [efectivos.data]);

  const servidorKey = useMemo(
    () =>
      [...servidor.estados.entries()]
        .map(([id, e]) => `${id}:${e}`)
        .sort()
        .join('|'),
    [servidor],
  );

  // "Derive state during render": se resincroniza el borrador cuando
  // cambia lo que devuelve el servidor (mismo patrón que MatrizPermisos).
  const [borrador, setBorrador] = useState<Map<string, Estado>>(
    () => new Map(servidor.estados),
  );
  const [prevKey, setPrevKey] = useState(servidorKey);
  if (prevKey !== servidorKey) {
    setPrevKey(servidorKey);
    setBorrador(new Map(servidor.estados));
  }
  const [confirmarReset, setConfirmarReset] = useState(false);

  const baseDelRol = useMemo(
    () => new Set(rol.data?.permisoIds ?? []),
    [rol.data],
  );

  if (!puedeGestionar) return null;

  const estadoDe = (permisoId: string): Estado =>
    borrador.get(permisoId) ?? 'heredado';

  let concedidos = 0;
  let denegados = 0;
  for (const e of borrador.values()) {
    if (e === 'concedido') concedidos += 1;
    else if (e === 'denegado') denegados += 1;
  }

  const dirty = (() => {
    if (borrador.size !== servidor.estados.size) return true;
    for (const [id, e] of borrador) {
      if (servidor.estados.get(id) !== e) return true;
    }
    return false;
  })();

  const esMismoUsuario = actorId != null && actorId === usuarioId;
  const rolProtegido = efectivos.data?.rolEsSuperAdmin === true;
  const sinRol = efectivos.data != null && efectivos.data.rolId == null;
  const deshabilitado = esMismoUsuario || rolProtegido || sinRol;
  const hayExcepciones = servidor.estados.size > 0;
  const ocupado = guardar.isPending || restablecer.isPending;

  /** Aplica varios cambios al borrador en una sola actualización. */
  function aplicar(cambios: ReadonlyArray<readonly [string, Estado]>) {
    setBorrador((prev) => {
      const next = new Map(prev);
      for (const [permisoId, estado] of cambios) {
        if (estado === 'heredado') next.delete(permisoId);
        else next.set(permisoId, estado);
      }
      return next;
    });
  }

  function notificarError(error: unknown, fallback: string) {
    if (esApiError(error)) {
      // 403 (sin alcance/escalada/auto-edición), 409 (conflicto) y 422
      // (usuario inactivo, sin rol, super-admin) llegan como Problem Details.
      toast.error(error.problem.title, {
        description: error.problem.detail ?? (error.traceId ? `Código: ${error.traceId}` : undefined),
      });
      return;
    }
    toast.error(fallback);
  }

  function handleGuardar() {
    const overrides: PermisoOverrideItem[] = [...borrador.entries()]
      .filter(([, estado]) => estado !== 'heredado')
      .map(([permisoId, estado]) => ({
        permisoId,
        efecto: ESTADO_A_EFECTO[estado as Exclude<Estado, 'heredado'>],
        motivo: servidor.motivos.get(permisoId) ?? null,
      }));
    const payload = { overrides };
    guardar.mutate(
      { usuarioId, empresaId, payload, idempotencyKey: keyFor(payload) },
      {
        onSuccess: () => toast.success('Permisos personalizados actualizados'),
        onError: (error) =>
          notificarError(error, 'Error inesperado al guardar los permisos personalizados.'),
      },
    );
  }

  function handleRestablecer() {
    restablecer.mutate(
      { usuarioId, empresaId, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          setConfirmarReset(false);
          toast.success('Permisos restablecidos al rol');
        },
        onError: (error) => {
          setConfirmarReset(false);
          notificarError(error, 'Error inesperado al restablecer los permisos.');
        },
      },
    );
  }

  const cargando = efectivos.isLoading || catalogo.isLoading || (rolId != null && rol.isLoading);
  const fallo = efectivos.isError ? efectivos : catalogo.isError ? catalogo : rol.isError ? rol : null;
  const grupos = catalogo.data?.grupos ?? [];

  return (
    <section className="space-y-3" aria-label="Permisos personalizados">
      <header className="space-y-1">
        <h3 className="text-base font-semibold">Permisos personalizados</h3>
        <p className="text-xs text-muted-foreground">
          Excepciones sobre el rol
          {efectivos.data?.rolCodigo != null && (
            <>
              {' '}
              <span className="font-mono">{efectivos.data.rolCodigo}</span>
            </>
          )}{' '}
          en esta empresa: efectivos = (rol + concedidos) − denegados. Si se
          cambia o revoca el rol del usuario en esta empresa, estas excepciones
          se borran.
        </p>
      </header>

      {deshabilitado && (
        <div
          role="status"
          className="flex items-center gap-2 rounded-md border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-xs text-amber-900 dark:text-amber-200"
        >
          <ShieldAlert className="h-4 w-4 shrink-0" aria-hidden="true" />
          <span>
            {esMismoUsuario
              ? 'No puedes editar tus propios permisos personalizados; solicita el cambio a otro administrador.'
              : rolProtegido
                ? 'Los permisos del rol super-admin se gestionan por bootstrap; no admiten excepciones.'
                : 'El usuario no tiene un rol en esta empresa; asigna uno antes de personalizar permisos.'}
          </span>
        </div>
      )}

      {cargando ? (
        <TableSkeleton rows={4} columns={[{ width: 'w-full' }, { width: 'w-32' }]} />
      ) : fallo != null ? (
        <ErrorState
          problem={esApiError(fallo.error) ? fallo.error.problem : undefined}
          onRetry={() => fallo.refetch()}
        />
      ) : (
        <>
          <div className="space-y-2">
            {grupos.map((g) => (
              <ModuloCollapsible
                key={g.modulo}
                modulo={g.modulo}
                items={g.items}
                estadoDe={estadoDe}
                baseDelRol={baseDelRol}
                permisosDelActor={permisosDelActor}
                onAplicar={aplicar}
                disabled={deshabilitado || ocupado}
              />
            ))}
          </div>

          <div className="flex flex-wrap items-center justify-end gap-2 border-t pt-3">
            <p className="mr-auto text-xs text-muted-foreground" aria-live="polite">
              {concedidos} concedido(s) / {denegados} denegado(s) respecto al rol.
              {dirty && (
                <span className="ml-1 text-amber-700">Hay cambios sin guardar.</span>
              )}
            </p>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={!hayExcepciones || ocupado || esMismoUsuario}
              onClick={() => setConfirmarReset(true)}
            >
              Restablecer al rol
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              disabled={!dirty || ocupado}
              onClick={() => setBorrador(new Map(servidor.estados))}
            >
              Descartar cambios
            </Button>
            <Button
              type="button"
              size="sm"
              disabled={!dirty || deshabilitado || ocupado}
              onClick={handleGuardar}
            >
              {guardar.isPending ? 'Guardando…' : 'Guardar cambios'}
            </Button>
          </div>
        </>
      )}

      <AlertDialog
        open={confirmarReset}
        onOpenChange={(open) => {
          if (!open) setConfirmarReset(false);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Restablecer al rol</AlertDialogTitle>
            <AlertDialogDescription>
              Se borrarán las {servidor.estados.size} excepción(es) de este
              usuario en la empresa y volverá a tener exactamente los permisos
              de su rol. ¿Continuar?
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={restablecer.isPending}>Cancelar</AlertDialogCancel>
            <AlertDialogAction onClick={handleRestablecer} disabled={restablecer.isPending}>
              {restablecer.isPending ? 'Restableciendo…' : 'Restablecer'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </section>
  );
}

interface ModuloCollapsibleProps {
  modulo: string;
  items: readonly PermisoResponse[];
  estadoDe: (permisoId: string) => Estado;
  baseDelRol: ReadonlySet<string>;
  permisosDelActor: readonly string[];
  onAplicar: (cambios: ReadonlyArray<readonly [string, Estado]>) => void;
  disabled: boolean;
}

/** Estado guardable que deja un permiso con el valor efectivo `encendido`. */
function estadoParaEfectivo(delRol: boolean, encendido: boolean): Estado {
  if (encendido === delRol) return 'heredado';
  return encendido ? 'concedido' : 'denegado';
}

function ModuloCollapsible({
  modulo,
  items,
  estadoDe,
  baseDelRol,
  permisosDelActor,
  onAplicar,
  disabled,
}: ModuloCollapsibleProps) {
  const [open, setOpen] = useState(false);
  const [omitidos, setOmitidos] = useState(0);
  const conExcepcion = items.filter((p) => estadoDe(p.id) !== 'heredado').length;

  const efectivo = (p: PermisoResponse) => {
    const e = estadoDe(p.id);
    return e === 'heredado' ? baseDelRol.has(p.id) : e === 'concedido';
  };
  const encendidos = items.filter(efectivo).length;
  const estadoGrupo: boolean | 'mixed' =
    encendidos === items.length ? true : encendidos === 0 ? false : 'mixed';

  function alternarGrupo() {
    if (estadoGrupo === true) {
      // Apagar: deniega lo que el rol incluye y descarta lo concedido.
      setOmitidos(0);
      onAplicar(items.map((p) => [p.id, estadoParaEfectivo(baseDelRol.has(p.id), false)] as const));
      return;
    }
    // Encender: concede solo lo que el actor posee; quita denegaciones.
    let sinPosesion = 0;
    const cambios: Array<readonly [string, Estado]> = [];
    for (const p of items) {
      if (efectivo(p)) continue;
      const delRol = baseDelRol.has(p.id);
      if (delRol || permisosDelActor.includes(p.codigo)) {
        cambios.push([p.id, estadoParaEfectivo(delRol, true)]);
      } else {
        sinPosesion += 1;
      }
    }
    setOmitidos(sinPosesion);
    onAplicar(cambios);
  }

  function restablecerGrupo() {
    setOmitidos(0);
    onAplicar(items.map((p) => [p.id, 'heredado'] as const));
  }

  return (
    <Collapsible open={open} onOpenChange={setOpen} className="rounded-md border bg-card">
      <div className="flex items-center gap-2 px-3 py-2">
        <CollapsibleTrigger
          className="flex flex-1 items-center justify-between gap-2 text-left text-sm font-medium hover:underline"
          aria-label={`Permisos personalizados de ${modulo}`}
        >
          <span className="capitalize">{modulo}</span>
          <span className="flex items-center gap-2 text-xs text-muted-foreground">
            {conExcepcion > 0 ? `${conExcepcion} excepción(es)` : `${items.length} permisos`}
            <ChevronRight
              className={cn('h-4 w-4 transition-transform', open && 'rotate-90')}
              aria-hidden="true"
            />
          </span>
        </CollapsibleTrigger>
        <Interruptor
          checked={estadoGrupo}
          disabled={disabled}
          label={`Todos los permisos de ${modulo}`}
          title={
            estadoGrupo === 'mixed'
              ? 'Estado mixto: al activarlo se encienden todos los permisos del grupo'
              : undefined
          }
          onClick={alternarGrupo}
        />
        <Button
          type="button"
          variant="ghost"
          size="icon"
          className="h-7 w-7"
          aria-label={`Restablecer grupo ${modulo}`}
          title="Restablecer grupo al rol"
          disabled={disabled || conExcepcion === 0}
          onClick={restablecerGrupo}
        >
          <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
        </Button>
      </div>
      {omitidos > 0 && (
        <p
          role="status"
          className="mx-3 mb-2 rounded-md border border-amber-500/30 bg-amber-500/10 px-3 py-1.5 text-xs text-amber-900 dark:text-amber-200"
        >
          Se omitieron {omitidos} permiso(s) que no puedes conceder porque tú no los posees.
        </p>
      )}
      <CollapsibleContent>
        <ul className="divide-y border-t" role="list">
          {items.map((p) => {
            const estado = estadoDe(p.id);
            const delRol = baseDelRol.has(p.id);
            const actorLoTiene = permisosDelActor.includes(p.codigo);
            const encendido = estado === 'heredado' ? delRol : estado === 'concedido';
            const sinPosesion = !encendido && !delRol && !actorLoTiene;
            return (
              <li
                key={p.id}
                className={cn(
                  'flex flex-wrap items-center gap-2 border-l-2 px-3 py-2',
                  estado === 'concedido' && 'border-l-emerald-500 bg-emerald-500/5',
                  estado === 'denegado' && 'border-l-rose-500 bg-rose-500/5',
                  estado === 'heredado' && 'border-l-transparent',
                )}
              >
                <div className="min-w-0 flex-1 space-y-0.5">
                  <div className="font-mono text-xs">{p.codigo}</div>
                  <div className="text-xs text-muted-foreground">
                    {p.descripcion} ·{' '}
                    {delRol ? 'El rol lo incluye' : 'El rol no lo incluye'}
                    {estado === 'concedido' && (
                      <span className="ml-1 font-medium text-emerald-700">· Personalizado: añadido</span>
                    )}
                    {estado === 'denegado' && (
                      <span className="ml-1 font-medium text-rose-700">· Personalizado: quitado</span>
                    )}
                    {sinPosesion && (
                      <span className="ml-1 text-amber-700">
                        · No puedes encenderlo: solo puedes conceder permisos que tú posees
                      </span>
                    )}
                  </div>
                </div>
                {estado !== 'heredado' && (
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="h-7 w-7"
                    aria-label={`Volver al valor del rol de ${p.codigo}`}
                    title="Volver al valor del rol"
                    disabled={disabled}
                    onClick={() => onAplicar([[p.id, 'heredado']])}
                  >
                    <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
                  </Button>
                )}
                <Interruptor
                  checked={encendido}
                  label={`Permiso ${p.codigo}`}
                  disabled={disabled || sinPosesion}
                  title={sinPosesion ? 'Solo puedes conceder permisos que tú posees' : undefined}
                  onClick={() => onAplicar([[p.id, estadoParaEfectivo(delRol, !encendido)]])}
                />
              </li>
            );
          })}
        </ul>
      </CollapsibleContent>
    </Collapsible>
  );
}
