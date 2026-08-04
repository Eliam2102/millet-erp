import { useEffect, useState } from 'react';
import { getHubConnection } from '@/lib/signalr';
import { useAuthStore } from '@/lib/auth/auth-store';

/**
 * <c>useCollaboration(entidad, id)</c> — hook de presencia colaborativa
 * (otros usuarios viendo/editando la misma entidad). Doc 05 §9.2 +
 * UF8-PR1 (Camino A confirmado por owner Rev. 4).
 *
 * <para><b>Conexión al ComprasHub</b>: usa el singleton
 * <c>signalr.ts</c>, llama <c>ViewingResource(entidad, id)</c> al
 * montar, suscribe el evento <c>userPresence</c> y se desuscribe
 * limpiamente en unmount con <c>LeaveResource()</c>. Heartbeat de
 * 30s para refrescar el TTL server-side (que es de 90s; doble
 * margen para tolerar pérdida de un beat).</para>
 *
 * <para><b>Modo</b>: por default el hook anuncia "Viewing". Si el
 * caller quiere indicar "Editing" (form abierto, mutation en curso),
 * pasa <c>modo: 'editing'</c> en las opciones — el hook llama
 * <c>EditingResource</c> en lugar de <c>ViewingResource</c>. El
 * cambio de modo se aplica re-llamando al hub (el server hace
 * upsert; no hay inconsistencia transitoria).</para>
 *
 * <para><b>Backend payload</b>: el hub emite un solo evento
 * <c>userPresence</c> con <c>{ entidad, entidadId, users: [{ userId,
 * userNombre, modo: 'Editing' | 'Viewing', sinceUtc, lastSeenUtc }] }</c>.
 * El hook filtra por la entidad+id observada y particiona en dos
 * arrays para el contrato del FE (<c>viendo</c> / <c>editando</c>).</para>
 *
 * <para>Si <c>id == null</c> o <c>id == ''</c> (entidad todavía no
 * creada — caso típico de P4 "Nueva requisición"), el hook NO se
 * conecta al hub: devuelve presencia vacía. Útil para no spamear el
 * hub durante drafts.</para>
 */

export interface PresenceUser {
  /** OID del usuario (Entra ID). Mirror del Guid del backend. */
  userId: string;
  /** Nombre amigable para tooltip y label. */
  nombre: string;
  /** URL del avatar (opcional). El backend no lo provee aún; se
   * resuelve client-side desde el directorio si está disponible. */
  avatarUrl?: string;
}

export interface CollaborationPresence {
  /** Otros usuarios mirando la misma entidad sin editar. */
  viendo: PresenceUser[];
  /** Otros usuarios con un form abierto sobre la entidad. */
  editando: PresenceUser[];
}

export interface UseCollaborationOptions {
  /** <c>'viewing'</c> (default) o <c>'editing'</c>. El caller cambia
   * a editing cuando entra al modo "form abierto" (LineaInlineForm,
   * NuevaRequisicion). */
  modo?: 'viewing' | 'editing';
}

const PRESENCIA_VACIA: CollaborationPresence = Object.freeze({
  viendo: [],
  editando: [],
});

/** Heartbeat 30s — el TTL server-side es 90s (doc 05 §9.2 + ADR-0012
 * Capa 2). Triple ventana tolera la pérdida de 2 beats. */
const HEARTBEAT_MS = 30_000;

interface UserPresencePayload {
  entidad: string;
  entidadId: string;
  users: Array<{
    userId: string;
    userNombre: string;
    modo: 'Editing' | 'Viewing';
    sinceUtc: string;
    lastSeenUtc: string;
  }>;
}

export function useCollaboration(
  entidad: string,
  id: string | null | undefined,
  options: UseCollaborationOptions = {},
): CollaborationPresence {
  const modo = options.modo ?? 'viewing';
  const currentUserId = useAuthStore((s) => s.user?.id) ?? '';
  const [presencia, setPresencia] =
    useState<CollaborationPresence>(PRESENCIA_VACIA);

  // Reset cuando id transiciona a null/empty — patrón "derived state"
  // de React docs (setState durante render con prev tracker) en
  // lugar de setState dentro de useEffect (antipatrón cascading
  // renders, regla react-hooks/set-state-in-effect).
  const [trackedId, setTrackedId] = useState<string | null | undefined>(id);
  if (id !== trackedId) {
    setTrackedId(id);
    if (id == null || id === '') {
      setPresencia(PRESENCIA_VACIA);
    }
  }

  useEffect(() => {
    if (id == null || id === '') {
      return;
    }

    let cancelled = false;
    let heartbeatId: ReturnType<typeof setInterval> | null = null;

    function aplicarPayload(payload: UserPresencePayload) {
      if (payload.entidad !== entidad || payload.entidadId !== id) {
        // Ignorar eventos de otras entidades (mismo hub, mismo grupo
        // empresa pero distinta RQ/cliente/etc.).
        return;
      }
      // Filtrar al usuario actual del listado — siempre ve a "los
      // demás", no a sí mismo.
      const otros = payload.users.filter((u) => u.userId !== currentUserId);
      setPresencia({
        viendo: otros
          .filter((u) => u.modo === 'Viewing')
          .map((u) => ({ userId: u.userId, nombre: u.userNombre })),
        editando: otros
          .filter((u) => u.modo === 'Editing')
          .map((u) => ({ userId: u.userId, nombre: u.userNombre })),
      });
    }

    void (async () => {
      try {
        const conn = await getHubConnection();
        if (cancelled) return;

        // Suscribir el evento ANTES de llamar al método server-side
        // para no perder el broadcast inicial post-Track.
        conn.on('userPresence', aplicarPayload);

        // Snapshot inicial: GetPresence no broadcastea, solo retorna
        // la lista actual del recurso.
        const snapshot = await conn.invoke<UserPresencePayload['users']>(
          'GetPresence',
          entidad,
          id,
        );
        if (cancelled) {
          conn.off('userPresence', aplicarPayload);
          return;
        }
        aplicarPayload({ entidad, entidadId: id, users: snapshot });

        // Anunciar al server que estamos aquí — esto SÍ broadcastea
        // a otros usuarios (incluyéndose a sí mismo en el group). El
        // filtrado de "self" lo hace aplicarPayload.
        const metodo = modo === 'editing' ? 'EditingResource' : 'ViewingResource';
        await conn.invoke(metodo, entidad, id);
        if (cancelled) {
          conn.off('userPresence', aplicarPayload);
          return;
        }

        // Heartbeat 30s para refrescar TTL.
        heartbeatId = setInterval(() => {
          if (conn.state === 'Connected') {
            void conn.invoke('Heartbeat').catch(() => {
              /* network blip; auto-reconnect del hub se encarga */
            });
          }
        }, HEARTBEAT_MS);
      } catch {
        // Si la conexión falla (red, hub caído, JWT inválido), el
        // soft-lock no es crítico — caemos a presencia vacía y
        // dejamos al usuario operar. El error queda silente; es UX
        // best-effort, no flujo de negocio.
        if (!cancelled) setPresencia(PRESENCIA_VACIA);
      }
    })();

    return () => {
      cancelled = true;
      if (heartbeatId != null) clearInterval(heartbeatId);
      // Cleanup async sin bloquear el unmount — el server expira el
      // TTL solo si no llega LeaveResource a tiempo.
      void (async () => {
        try {
          const conn = await getHubConnection();
          conn.off('userPresence', aplicarPayload);
          await conn.invoke('LeaveResource').catch(() => {
            /* tolerable — el TTL del server limpia */
          });
        } catch {
          // Conexión no disponible; el server eventualmente expira.
        }
      })();
    };
  }, [entidad, id, modo, currentUserId]);

  return presencia;
}
