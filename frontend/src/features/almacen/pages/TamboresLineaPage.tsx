import { Link } from '@tanstack/react-router';
import { PageHeader } from '@/components/erp/PageHeader';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { Badge } from '@/components/ui/badge';
import { Table, TableBody, TableCell, TableContainer, TableHead, TableHeader, TableRow,
} from '@/components/ui/table';
import { useTamboresLinea, type LineaTambores, type TamborLinea,
} from '@/features/almacen/api/useTamboresLinea';
import { esApiError } from '@/lib/api';

const ESTADO: Record<TamborLinea['estado'], { variant: 'success' | 'neutral'; texto: string }> = {
  ACTIVO: { variant: 'success', texto: 'Activo' },
  GASTADO: { variant: 'neutral', texto: 'Gastado' },
};

const material = (t: TamborLinea) => (
  <>
    <span className="font-mono text-xs">{t.articuloClave ?? t.articuloId.slice(0, 8)}</span>
    {t.articuloNombre ? ` · ${t.articuloNombre}` : ''}
  </>
);

/**
 * M-172 (AC-030) — Tambores por línea: por cada línea de producción, el tambor o cubeta activo de cada material y el
 * historial con inicio, fin y folio de la salida. Un tambor queda gastado cuando sale otro del mismo material a la
 * misma línea; no se captura sobrante.
 */
export function TamboresLineaPage() {
  const query = useTamboresLinea();
  const lineas = query.data ?? [];

  return (
    <div className="space-y-5">
      <PageHeader
        titulo="Tambores por línea"
        descripcion="Silicón, Ködispace y demás materiales que se surten por tambor o cubeta a una línea de producción: el activo y su historial."
      />
      {query.isError ? (
        <ListadoModulo>
          <ErrorState
            title="No se pudieron cargar las líneas"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        </ListadoModulo>
      ) : query.isLoading ? (
        <ListadoModulo>
          <TableSkeleton
            rows={4}
            columns={[{ width: 'w-32' }, { width: 'w-20' }, { width: 'w-24' }, { width: 'w-24' }]}
          />
        </ListadoModulo>
      ) : lineas.length === 0 ? (
        <ListadoModulo>
          <EmptyState
            title="Sin líneas de producción"
            description="No hay líneas de producción en tus sucursales."
          />
        </ListadoModulo>
      ) : (
        lineas.map((l) => <SeccionLinea key={l.id} linea={l} />)
      )}
    </div>
  );
}

function SeccionLinea({ linea }: { linea: LineaTambores }) {
  const activos = linea.tambores.filter((t) => t.estado === 'ACTIVO');
  return (
    <section aria-labelledby={`linea-${linea.id}`} className="space-y-2">
      <h2 id={`linea-${linea.id}`} className="text-sm font-semibold">
        {linea.nombre}{' '}
        <span className="font-mono text-xs font-normal text-ink-muted">
          {linea.clave} · {linea.proceso}
        </span>
      </h2>
      <ListadoModulo
        filtros={
          <p className="text-xs text-ink-muted">
            {activos.length === 0 ? (
              'Sin tambor activo.'
            ) : (
              <>
                Activo:{' '}
                {activos.map((t, i) => (
                  <span key={t.id}>
                    {i > 0 && ' · '}
                    {material(t)} desde <DateTimeDisplay value={t.fechaInicio} variant="corta" />
                  </span>
                ))}
              </>
            )}
          </p>
        }
      >
        {linea.tambores.length === 0 ? (
          <EmptyState
            title="Aún no hay tambores"
            description="Aparecen al surtir un tambor o cubeta a esta línea."
          />
        ) : (
          <TableContainer>
            <Table paginada>
              <TableHeader>
                <TableRow>
                  <TableHead>Material</TableHead>
                  <TableHead>Estado</TableHead>
                  <TableHead className="text-right">Kg</TableHead>
                  <TableHead>Inicio</TableHead>
                  <TableHead>Fin</TableHead>
                  <TableHead>Folio de salida</TableHead>
                  <TableHead>Reemplazado por</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {linea.tambores.map((t) => (
                  <TableRow key={t.id} className="hover:bg-surface-subtle">
                    <TableCell>{material(t)}</TableCell>
                    <TableCell>
                      <Badge variant={ESTADO[t.estado].variant}>{ESTADO[t.estado].texto}</Badge>
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {t.kgPresentacion ?? '—'}
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      <DateTimeDisplay value={t.fechaInicio} variant="corta" />
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      {t.fechaFin ? <DateTimeDisplay value={t.fechaFin} variant="corta" /> : '—'}
                    </TableCell>
                    <TableCell>
                      <Link
                        to="/almacen/salidas/$id"
                        params={{ id: t.movimientoId }}
                        className="font-mono text-xs font-medium text-brand"
                      >
                        {t.folio}
                      </Link>
                    </TableCell>
                    <TableCell className="font-mono text-xs">{t.folioReemplazo ?? '—'}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </ListadoModulo>
    </section>
  );
}
