import { Link } from '@tanstack/react-router';
import { X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import type { ResultadoGeneracion as Resultado } from '../../api/recurrentes-types';
import { folioOBorrador } from '../../lib/polizas';
import { etiquetaMes } from '../../lib/recurrentes';
import { EstadoPolizaBadge } from '../polizas/EstadoPolizaBadge';

interface Props {
  resultado: Resultado;
  onCerrar: () => void;
  /** En el detalle de una plantilla no se repite su nombre en cada fila. */
  mostrarPlantilla?: boolean;
}

/** Resultado de «Generar pendientes»: pólizas y reversas generadas, y las pendientes con su motivo (p. ej. periodo no abierto). */
export function ResultadoGeneracion({ resultado, onCerrar, mostrarPlantilla = true }: Props) {
  const { generadas, pendientes } = resultado;
  return (
    <section aria-label="Resultado de la generación" className="rounded-lg bg-surface-card p-4 shadow-card-flat" data-print="hidden">
      <div className="mb-2 flex items-start gap-2">
        <h2 className="text-md font-semibold">Resultado de la generación</h2>
        <Button variant="ghost" size="icon" className="ml-auto" aria-label="Cerrar resultado" onClick={onCerrar}>
          <X className="size-4" aria-hidden="true" />
        </Button>
      </div>
      <p role="status" className="text-sm text-ink-secondary">
        {generadas.length === 0 && pendientes.length === 0
          ? 'No había pólizas por generar: todo lo que ya toca está al día.'
          : `${generadas.length} ${generadas.length === 1 ? 'póliza generada' : 'pólizas generadas'} · ${pendientes.length} ${pendientes.length === 1 ? 'pendiente' : 'pendientes'}.`}
      </p>

      {generadas.length > 0 && (
        <>
          <h3 className="mt-3 text-sm font-semibold">Generadas</h3>
          <ul className="mt-1 flex flex-col divide-y divide-line-row text-sm">
            {generadas.map((g) => (
              <li key={g.polizaId} className="flex flex-wrap items-center gap-x-3 gap-y-1 py-2">
                <Link to="/contabilidad/polizas/$id" params={{ id: g.polizaId }} className="font-mono text-xs font-medium text-brand hover:underline">
                  {folioOBorrador(g.folio)}
                </Link>
                <EstadoPolizaBadge estado={g.estado} relacion={g.esReversa ? 'Reversa' : null} />
                <span className="text-ink-secondary">{etiquetaMes(g.anio, g.mes)}</span>
                {mostrarPlantilla && <span className="text-xs text-ink-muted">{g.plantilla}</span>}
              </li>
            ))}
          </ul>
        </>
      )}

      {pendientes.length > 0 && (
        <>
          <h3 className="mt-3 text-sm font-semibold">Pendientes</h3>
          <ul className="mt-1 flex flex-col divide-y divide-line-row text-sm">
            {pendientes.map((x) => (
              <li key={`${x.plantillaId}-${x.anio}-${x.mes}-${x.esReversa}`} className="flex flex-col gap-0.5 py-2">
                <span className="flex flex-wrap items-center gap-x-3">
                  <span className="font-medium">{x.esReversa ? 'Reversa' : 'Póliza'} de {etiquetaMes(x.anio, x.mes)}</span>
                  {mostrarPlantilla && <span className="text-xs text-ink-muted">{x.plantilla}</span>}
                </span>
                <span className="text-xs text-warning-fg">{x.motivo}</span>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  );
}
