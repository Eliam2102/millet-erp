import {
  Table,
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
} from '@/components/ui/table';
import type { DatosEmbarqueCe, EmbarqueCe } from '../api/types';
export function EmbarqueCeResumen({
  datos,
  bloqueo,
}: {
  datos: EmbarqueCe | DatosEmbarqueCe;
  bloqueo?: string | null;
}) {
  return (
    <section
      className="space-y-2 rounded-lg bg-surface-card p-4 text-sm shadow-card-flat"
      aria-label="Datos de embarque del expediente"
    >
      <h2 className="font-semibold">
        Tomado del expediente <span className="font-mono">{datos.folioExpediente}</span>
      </h2>
      <dl className="grid grid-cols-2 gap-3">
        <div>
          <dt className="text-xs text-ink-muted">Pedimento de exportación</dt>
          <dd className="font-mono text-xs">
            {datos.numeroPedimento ?? 'Pendiente'} · {datos.clavePedimento ?? 'Pendiente'}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-ink-muted">Incoterm</dt>
          <dd>{datos.incoterm ?? 'Pendiente'}</dd>
        </div>
      </dl>
      {datos.aduana && (
        <p>
          Aduana: {datos.aduana} · {datos.aduanaNombre}
        </p>
      )}
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Contenedor</TableHead>
            <TableHead>BL o guía</TableHead>
            <TableHead>Embarque</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {datos.contenedores?.map((c, i) => (
            <TableRow key={`${c.numero}-${i}`}>
              <TableCell className="font-mono text-xs">{c.numero}</TableCell>
              <TableCell className="font-mono text-xs">{c.blGuia}</TableCell>
              <TableCell>{c.fechaEmbarqueReal}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      {datos.clavePedimento === 'RT' && <p>RT · Sin CCE.</p>}
      {bloqueo && (
        <p role="alert" className="rounded-md bg-warning-note-bg px-3 py-2.5 text-warning-note-fg">
          {bloqueo}
        </p>
      )}
      <p className="text-xs text-ink-muted">
        Estos datos se conservan en la factura. La inclusión del pedimento en el XML está pendiente
        de validación fiscal.
      </p>
    </section>
  );
}
