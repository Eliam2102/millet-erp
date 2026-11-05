import { Link } from '@tanstack/react-router';
import { format, subDays } from 'date-fns';
import { Card, CardHeader, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { useQuery } from '@tanstack/react-query';
import { buildAuditoriaPath } from '@/modules/administracion/api/auditoria';
import type { ConsultarBitacoraResponse } from '@/modules/administracion/api/types';
import { nombreEntidad, textoActividad } from '@/lib/entidades';
import { apiRequest } from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';
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
  const sucursalId = useAuthStore((s) => s.currentSucursalId);
  const filtros = {
    desde,
    hasta,
    usuarioId,
    empresaId,
    sucursalId: sucursalId ?? undefined,
    offset: 0,
    limit: 3,
  };
  const query = useQuery({
    queryKey: ['inicio-recientes', filtros],
    queryFn: async () =>
      (await apiRequest<ConsultarBitacoraResponse>(buildAuditoriaPath(filtros))).data,
    staleTime: 0,
    gcTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
  });
  return (
    <section aria-labelledby="recientes-inicio">
      <Card className="rounded-xl bg-surface-card shadow-card-flat">
        <CardHeader className="flex-row items-center justify-between space-y-0 px-3 py-2">
          <h2 id="recientes-inicio" className="text-lg font-semibold">
            Lo último que hiciste
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
          <ul className="grid grid-cols-1 gap-2 px-3 pb-3 md:grid-cols-3">
            {query.data.items.map((evento) => {
              const etiqueta = evento.entidadEtiqueta
                ? textoActividad(evento.entidadEtiqueta, evento.entidad)
                : nombreEntidad(evento.entidad);
              const conId = etiqueta.match(/^(.*) ([a-f0-9]{7,8})$/i);
              const resumen = textoActividad(evento.resumen || evento.operacion, evento.entidad);
              const detalle = detalleActividad(resumen, etiqueta);
              return (
                <li key={evento.id} className="min-w-0">
                  <Link
                    to="/admin/auditoria"
                    search={{}}
                    title={detalle}
                    className="flex h-full min-w-0 flex-col gap-1 rounded-md border border-line-row bg-surface-subtle px-3 py-2 transition-colors hover:border-line-control hover:bg-surface-card focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand"
                  >
                    <span className="flex min-w-0 items-baseline gap-2">
                      <span className="min-w-0 flex-1 truncate text-sm font-medium text-ink">
                        {conId ? conId[1] : etiqueta}
                        {conId && (
                          <span className="ml-1 font-mono text-xs text-ink-muted">{conId[2]}</span>
                        )}
                      </span>
                      <time
                        dateTime={evento.timestamp}
                        className="shrink-0 whitespace-nowrap text-xs text-ink-muted"
                      >
                        {formatRelative(evento.timestamp)}
                      </time>
                    </span>
                    <span className="line-clamp-2 break-words text-xs text-ink-secondary">
                      {detalle}
                    </span>
                  </Link>
                </li>
              );
            })}
          </ul>
        )}
      </Card>
    </section>
  );
}

/**
 * Quita del resumen la etiqueta que ya se muestra como título
 * («Modificó Linea requisicion 01a1a39c — Notas: …» → «Modificó · Notas: …»)
 * y muestra los valores nulos como «vacío».
 */
function detalleActividad(resumen: string, etiqueta: string): string {
  let texto = resumen;
  const i = etiqueta ? texto.indexOf(etiqueta) : -1;
  if (i > 0) {
    const verbo = texto.slice(0, i).trim();
    const resto = texto.slice(i + etiqueta.length).replace(/^\s*[—–-]\s*/, '').trim();
    texto = resto ? `${verbo} · ${resto}` : verbo;
  }
  return texto.replace(/\bnull\b/g, 'vacío');
}
