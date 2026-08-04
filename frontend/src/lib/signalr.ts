import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { useAuthStore } from '@/lib/auth/auth-store';

/**
 * <c>signalr.ts</c> — singleton del <c>HubConnection</c> al
 * <c>ComprasHub</c> del backend (ADR-0001).
 *
 * <para>Endpoint: <c>{VITE_API_BASE_URL}/hubs/compras</c>. Auth: el JWT
 * se pasa vía query param <c>access_token</c> (los browsers no
 * permiten setear headers en el handshake del WebSocket). El hub
 * agrupa por <c>empresa:{empresaId}</c> automáticamente desde el JWT;
 * el frontend solo necesita autenticarse, los grupos los maneja el
 * server.</para>
 *
 * <para><b>Strategy de conexión</b>:</para>
 * <list>
 *   <item>Singleton compartido — todos los <c>useCollaboration</c>
 *   usan el mismo <c>HubConnection</c>; conectar/desconectar es
 *   referenciado por la cantidad de hooks activos.</item>
 *   <item><b>Auto-reconnect</b> con backoff: [0, 2s, 10s, 30s] —
 *   estándar de SignalR JS, suficiente para flaps de red. Después
 *   del cuarto intento se desconecta y deja al caller decidir.</item>
 *   <item>Si el JWT expira, el hub devolverá 401 al reconectar; el
 *   <c>onclose</c> propaga el error y el caller decide
 *   (<c>useCollaboration</c> lo trata como "sin presencia" — no es
 *   crítico para operación).</item>
 * </list>
 *
 * <para><b>Tests</b>: el módulo expone <c>setHubConnectionForTests</c>
 * que permite inyectar un mock del <c>HubConnection</c> (vía vi.mock
 * de <c>@microsoft/signalr</c> o manual). En producción, <c>null</c>
 * obliga al code path real.</para>
 */

let hubConnection: HubConnection | null = null;
let hubConnectionPromise: Promise<HubConnection> | null = null;

const HUB_PATH = '/hubs/compras';

/**
 * Devuelve el HubConnection singleton, conectándolo si no lo está.
 * El caller espera el connect; idempotente — múltiples llamadas
 * concurrentes comparten la misma promesa.
 */
export async function getHubConnection(): Promise<HubConnection> {
  if (hubConnection != null && hubConnection.state === HubConnectionState.Connected) {
    return hubConnection;
  }
  if (hubConnectionPromise != null) {
    return hubConnectionPromise;
  }

  hubConnectionPromise = connectInternal();
  try {
    const conn = await hubConnectionPromise;
    hubConnection = conn;
    return conn;
  } finally {
    hubConnectionPromise = null;
  }
}

async function connectInternal(): Promise<HubConnection> {
  const baseUrl = import.meta.env.VITE_API_BASE_URL ?? '';
  const url = `${baseUrl}${HUB_PATH}`;

  const conn = new HubConnectionBuilder()
    .withUrl(url, {
      // <c>accessTokenFactory</c> se invoca en cada (re)conexión —
      // garantiza que un JWT recién renovado entra al próximo
      // intento sin recrear el HubConnection.
      accessTokenFactory: () => {
        const token = useAuthStore.getState().accessToken;
        return token ?? '';
      },
    })
    // Reconexión automática con backoff [0, 2s, 10s, 30s] (defaults
    // de SignalR JS). Para flaps cortos no requiere acción del
    // caller; tras el 4to intento dispara onclose.
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();

  await conn.start();
  return conn;
}

/**
 * Cierra el HubConnection y limpia el singleton. Llamar al logout o
 * cuando el último consumidor se desuscribe (no es crítico — los
 * heartbeats expiran solos en 90s server-side).
 */
export async function disconnectHub(): Promise<void> {
  const conn = hubConnection;
  hubConnection = null;
  hubConnectionPromise = null;
  if (conn != null && conn.state !== HubConnectionState.Disconnected) {
    await conn.stop();
  }
}

/**
 * Inyecta una conexión mock para tests. <c>null</c> resetea al code
 * path real. Solo se usa desde <c>vi.mock</c> en tests; production
 * code path nunca lo invoca.
 */
export function setHubConnectionForTests(
  conn: HubConnection | null,
): void {
  hubConnection = conn;
  hubConnectionPromise = null;
}
