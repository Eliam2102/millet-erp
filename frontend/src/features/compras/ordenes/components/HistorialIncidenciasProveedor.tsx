import { Link } from '@tanstack/react-router';
import { Badge } from '@/components/ui/badge';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { DateTimeDisplay, EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { esApiError } from '@/lib/api';
import { useIncidenciasProveedorHistorial } from '../api/useIncidenciasProveedorHistorial';
import { TIPO_INCIDENCIA_LABEL } from '../lib/incidencias-proveedor';

/**
 * F1-COM-07: historial de incidencias del proveedor filtrado en Partidas abiertas (abiertas y
 * cerradas) con el cumplimiento de sus compromisos. Base de COM-13.
 */
export function HistorialIncidenciasProveedor({
  proveedorId,
  proveedorNombre,
}: {
  proveedorId: string;
  proveedorNombre: string;
}) {
  const query = useIncidenciasProveedorHistorial(proveedorId);
  const r = query.data?.resumen;

  return (
    <section
      aria-label="Historial de incidencias del proveedor"
      className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat"
    >
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-md font-semibold">Incidencias de {proveedorNombre}</h2>
        {r && r.total > 0 && (
          <p className="text-xs text-ink-muted">
            {r.abiertas} abiertas ({r.vencidas} vencidas) · Cumplidas a tiempo {r.cumplidasATiempo} ·
            Cumplidas tarde {r.cumplidasTarde} · Total {r.total}
          </p>
        )}
      </div>
      {query.isLoading ? (
        <TableSkeleton rows={3} />
      ) : query.isError ? (
        <ErrorState
          title="No se pudo cargar el historial de incidencias"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => void query.refetch()}
        />
      ) : !query.data || query.data.items.length === 0 ? (
        <EmptyState
          title="Este proveedor no tiene incidencias registradas."
          description="Las incidencias se registran desde el detalle de cada orden de compra."
        />
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <Table className="w-full text-sm">
            <TableHeader className="bg-surface-muted/50 text-xs uppercase tracking-wide text-ink-muted">
              <TableRow>
                <TableHead className="px-3 py-2 text-left font-medium">Folio</TableHead>
                <TableHead className="px-3 py-2 text-left font-medium">Partida</TableHead>
                <TableHead className="px-3 py-2 text-left font-medium">Tipo</TableHead>
                <TableHead className="px-3 py-2 text-right font-medium">Cantidad</TableHead>
                <TableHead className="px-3 py-2 text-left font-medium">Compromiso</TableHead>
                <TableHead className="px-3 py-2 text-left font-medium">Responsable</TableHead>
                <TableHead className="px-3 py-2 text-left font-medium">Resultado</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody className="[&_tr]:h-12">
              {query.data.items.map((i) => (
                <TableRow key={i.id}>
                  <TableCell className="px-3 py-2">
                    <Link
                      className="font-mono text-xs font-medium text-brand"
                      to="/compras/ordenes/$id"
                      params={{ id: i.ordenCompraId }}
                    >
                      {i.folio}
                    </Link>
                  </TableCell>
                  <TableCell className="px-3 py-2">{i.partida}</TableCell>
                  <TableCell className="px-3 py-2">{TIPO_INCIDENCIA_LABEL[i.tipo]}</TableCell>
                  <TableCell className="px-3 py-2 text-right tabular-nums">{i.cantidad}</TableCell>
                  <TableCell className="px-3 py-2">
                    <DateTimeDisplay value={i.fechaCompromisoVigente} />
                    {i.reprogramaciones > 0 && (
                      <span className="block text-xs text-ink-muted">
                        Inicial <DateTimeDisplay value={i.fechaCompromisoInicial} /> ·{' '}
                        {i.reprogramaciones} cambio{i.reprogramaciones === 1 ? '' : 's'}
                      </span>
                    )}
                  </TableCell>
                  <TableCell className="px-3 py-2 text-ink-secondary">
                    {i.responsableNombre ?? 'Usuario no disponible'}
                  </TableCell>
                  <TableCell className="px-3 py-2">
                    <ResultadoBadge item={i} />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </section>
  );
}

function ResultadoBadge({
  item,
}: {
  item: { abierta: boolean; vencida: boolean; cumplidaATiempo: boolean | null };
}) {
  if (item.abierta)
    return item.vencida ? (
      <Badge variant="danger">Compromiso vencido</Badge>
    ) : (
      <Badge variant="warning">Abierta</Badge>
    );
  if (item.cumplidaATiempo === true) return <Badge variant="success">Cumplida a tiempo</Badge>;
  if (item.cumplidaATiempo === false) return <Badge variant="warning">Cumplida tarde</Badge>;
  return <Badge variant="neutral">Cerrada a mano</Badge>;
}
