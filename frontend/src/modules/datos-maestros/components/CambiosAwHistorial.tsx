import { Badge } from '@/components/ui/badge';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import {
  TIPO_CAMBIO_AW,
  type CambioMaestroAw,
} from '@/modules/datos-maestros/api/sincronizacion-aw';

/** Cambios recibidos de A+W para un registro (bitácora de ADM-06): antes → después y versión del evento. */
export function CambiosAwHistorial({
  cambios,
  isLoading,
  isError,
}: {
  cambios: CambioMaestroAw[] | undefined;
  isLoading: boolean;
  isError: boolean;
}) {
  return (
    <div className="mt-4">
      <h4 className="mb-1 text-sm font-semibold text-ink">Cambios recibidos de A+W</h4>
      {isLoading && <p className="text-sm text-ink-muted">Cargando…</p>}
      {isError && (
        <p role="alert" className="text-sm text-danger-fg">
          No se pudo cargar el historial.
        </p>
      )}
      {!isLoading && !isError && cambios?.length === 0 && (
        <p className="text-sm text-ink-muted">Sin cambios registrados desde A+W.</p>
      )}
      {!isLoading && !isError && (cambios?.length ?? 0) > 0 && (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Fecha</TableHead>
              <TableHead>Campo</TableHead>
              <TableHead>Antes</TableHead>
              <TableHead>Después</TableHead>
              <TableHead>Tipo</TableHead>
              <TableHead>Versión</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {cambios!.map((c) => (
              <TableRow key={c.id}>
                <TableCell>{new Date(c.ocurridoEnUtc).toLocaleString('es-MX')}</TableCell>
                <TableCell>{c.campo}</TableCell>
                <TableCell>{c.valorAntes ?? '—'}</TableCell>
                <TableCell>{c.valorDespues ?? '—'}</TableCell>
                <TableCell>
                  <Badge variant={TIPO_CAMBIO_AW[c.tipo].variante}>
                    {TIPO_CAMBIO_AW[c.tipo].texto}
                  </Badge>
                </TableCell>
                <TableCell className="font-mono">{c.versionEvento.slice(0, 8)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </div>
  );
}
