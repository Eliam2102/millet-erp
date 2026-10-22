import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { Badge } from '@/components/ui/badge';
import { formatDateTime } from '@/lib/datetime';
import type { EstadoCuentaEnvioItem } from '@/features/cxc/api/types';

const ESTADOS: Record<string, { etiqueta: string; variante: 'success' | 'danger' | 'neutral' }> = {
  Enviado: { etiqueta: 'Enviado', variante: 'success' },
  Fallido: { etiqueta: 'Fallido', variante: 'danger' },
  Registrado: { etiqueta: 'Registrado (sin correo)', variante: 'neutral' },
};

/** Historial de envíos del estado de cuenta del cliente (F1-CXC-09 · C7.8). */
export function EnviosEstadoCuentaTabla({
  envios,
  isLoading,
}: {
  envios: EstadoCuentaEnvioItem[] | undefined;
  isLoading: boolean;
}) {
  return (
    <section
      data-print="hidden"
      aria-labelledby="envios-estado-cuenta"
      className="rounded-md border border-line bg-surface-card shadow-card-flat"
    >
      <h2 id="envios-estado-cuenta" className="px-4 pt-4 text-sm font-semibold">
        Envíos registrados
      </h2>
      {isLoading ? (
        <p role="status" className="p-4 text-sm text-ink-muted">
          Cargando envíos…
        </p>
      ) : !envios || envios.length === 0 ? (
        <p className="p-4 text-sm text-ink-muted">
          Aún no se ha enviado el estado de cuenta a este cliente.
        </p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Fecha</TableHead>
              <TableHead>Destinatario</TableHead>
              <TableHead>Usuario</TableHead>
              <TableHead>Estado</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {envios.map((e) => {
              const estado = ESTADOS[e.estado] ?? { etiqueta: e.estado, variante: 'neutral' as const };
              return (
                <TableRow key={e.id}>
                  <TableCell className="tabular-nums">{formatDateTime(e.fecha)}</TableCell>
                  <TableCell className="truncate">{e.destinatario}</TableCell>
                  <TableCell className="truncate">{e.usuarioNombre ?? 'Sin nombre'}</TableCell>
                  <TableCell>
                    <Badge variant={estado.variante} title={e.detalleError ?? undefined}>
                      {estado.etiqueta}
                    </Badge>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      )}
    </section>
  );
}
