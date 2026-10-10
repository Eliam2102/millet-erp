import { Link } from '@tanstack/react-router';
import { format, subDays } from 'date-fns';
import { Card, CardHeader, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { useAuditoria } from '@/modules/administracion/api/auditoria';
import { formatRelative, hoyLocalISO, parseDateOnlyLocal } from '@/lib/datetime';

/** Solo se monta con permiso de auditoría e identidad/empresa presentes. */
export function RecientesInicio({
  usuarioId,
  empresaId,
}: {
  usuarioId: string;
  empresaId: string;
}) {
  const hasta = hoyLocalISO();
  const desde = format(subDays(parseDateOnlyLocal(hasta), 6), 'yyyy-MM-dd');
  const query = useAuditoria({ desde, hasta, usuarioId, empresaId, offset: 0, limit: 4 });
  return (
    <section aria-labelledby="recientes-inicio">
      <Card className="rounded-xl bg-surface-card shadow-card-flat">
        <CardHeader>
          <h2 id="recientes-inicio" className="text-lg font-semibold">
            Recientes
          </h2>
          <p className="text-xs text-ink-muted">Tu actividad de los últimos 7 días.</p>
        </CardHeader>
        {query.isPending ? (
          <CardContent>
            <p role="status" className="text-xs text-ink-muted">
              Cargando actividad…
            </p>
          </CardContent>
        ) : query.isError ? (
          <CardContent className="space-y-2">
            <p role="status" className="text-xs text-danger-fg">
              No se pudo cargar tu actividad.
            </p>
            <Button
              variant="outline"
              size="sm"
              disabled={query.isFetching}
              onClick={() => {
                void query.refetch();
              }}
            >
              Reintentar actividad
            </Button>
          </CardContent>
        ) : query.data.items.length === 0 ? (
          <CardContent>
            <p className="text-xs text-ink-muted">Sin actividad reciente.</p>
          </CardContent>
        ) : (
          <ul>
            {query.data.items.map((evento) => (
              <li key={evento.id} className="border-t border-line-row">
                <Link
                  to="/admin/auditoria"
                  search={{}}
                  className="grid min-h-16 grid-cols-[minmax(0,1fr)_auto] items-center gap-3 rounded-md px-4 py-3 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand hover:bg-surface-subtle"
                >
                  <span className="min-w-0">
                    <span className="block truncate font-mono text-xs font-medium text-brand">
                      {evento.entidadEtiqueta || evento.entidad}
                    </span>
                    <span
                      className="block truncate text-xs text-ink-secondary"
                      title={evento.resumen || evento.operacion}
                    >
                      {evento.resumen || evento.operacion}
                    </span>
                  </span>
                  <time dateTime={evento.timestamp} className="text-xs text-ink-muted">
                    {formatRelative(evento.timestamp)}
                  </time>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </section>
  );
}
