import { Button } from '@/components/ui/button';
import type { ResultadoAprobacionItem } from '@/features/compras/lib/resultados-masivos';

interface ResumenAprobacionMasivaProps {
  resultados: readonly ResultadoAprobacionItem[];
  onCerrar: () => void;
}

/**
 * Resumen persistente del último lote de aprobación (aprobadas y errores con
 * folio y mensaje del API). No depende de que la fila siga en la lista, por
 * eso vive en el estado de la página y se cierra con «Entendido» o al cambiar
 * la selección.
 */
export function ResumenAprobacionMasiva({ resultados, onCerrar }: ResumenAprobacionMasivaProps) {
  if (resultados.length === 0) return null;

  const aprobadas = resultados.filter((r) => r.ok);
  const errores = resultados.filter((r) => !r.ok);
  const titulo = `${aprobadas.length} ${aprobadas.length === 1 ? 'aprobada' : 'aprobadas'} · ${errores.length} ${errores.length === 1 ? 'con error' : 'con errores'}`;

  return (
    <section
      role="status"
      aria-live="polite"
      aria-label="Resumen de aprobación"
      data-component="resumen-aprobacion-masiva"
      className="space-y-2 rounded-md border bg-surface-card p-3 text-sm"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="font-medium">{titulo}</p>
        <Button type="button" variant="ghost" size="sm" onClick={onCerrar}>
          Entendido
        </Button>
      </div>
      {aprobadas.length > 0 && (
        <div>
          <p className="text-success-fg">Aprobadas</p>
          <ul className="ml-4 list-disc font-mono text-xs">
            {aprobadas.map((r) => (
              <li key={r.id}>{r.folio}</li>
            ))}
          </ul>
        </div>
      )}
      {errores.length > 0 && (
        <div>
          <p className="text-danger-fg">Con error</p>
          <ul className="ml-4 list-disc text-xs">
            {errores.map((r) => (
              <li key={r.id}>
                <span className="font-mono">{r.folio}</span>:{' '}
                <span className="text-danger-fg">{r.mensaje ?? 'Error al aprobar'}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </section>
  );
}
