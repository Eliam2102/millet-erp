import { useEffect, useState } from 'react';
import { Loader2 } from 'lucide-react';

function transcurrido(desde: string | null, ahora: number) {
  const s = desde ? Math.max(0, Math.floor((ahora - Date.parse(desde)) / 1000)) : 0;
  const mm = String(Math.floor(s / 60)).padStart(2, '0');
  return `${mm}:${String(s % 60).padStart(2, '0')}`;
}

/** Aviso de corrida en curso: no hay total conocido, así que barra sin porcentaje y tiempo transcurrido. */
export function CorridaEnCursoBanner({
  iniciadaEnUtc,
  pendiente,
}: {
  iniciadaEnUtc: string | null;
  pendiente: boolean;
}) {
  const [ahora, setAhora] = useState(() => Date.now());
  useEffect(() => {
    const t = setInterval(() => setAhora(Date.now()), 1000);
    return () => clearInterval(t);
  }, []);

  return (
    <div className="space-y-2 rounded-lg border border-brand-soft-border bg-brand-soft p-4">
      <p role="status" className="flex items-center gap-2 font-medium text-ink">
        <Loader2 aria-hidden className="h-4 w-4 animate-spin motion-reduce:animate-none" />
        {pendiente ? 'En cola: esperando a que arranque la lectura.' : 'Sincronizando con A+W'}
      </p>
      <span aria-hidden className="text-xs tabular-nums text-ink-muted">
        {transcurrido(iniciadaEnUtc, ahora)} transcurridos
      </span>
      <div aria-hidden className="h-1 overflow-hidden rounded-full bg-surface-card">
        <div className="h-full w-full animate-pulse rounded-full bg-brand motion-reduce:animate-none" />
      </div>
      <p className="text-xs text-ink-muted">
        Puedes salir de esta página: la corrida sigue en segundo plano y los números se actualizan
        solos.
      </p>
    </div>
  );
}
