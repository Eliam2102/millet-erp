import { Button } from '@/components/ui/button';

/**
 * Resultado de una aprobación masiva ya enriquecido con el folio, porque el
 * API solo devuelve <c>id</c>. Lo arma la página al aprobar, no al refrescar.
 */
export interface ResultadoAprobacionItem {
  id: string;
  folio: string;
  ok: boolean;
  mensaje: string | null;
}

/**
 * Convierte la respuesta de <c>autorizaciones-masivas</c> en items con folio.
 * Si el id no está en el mapa (no debería pasar) muestra el id como folio.
 */
export function resultadosConFolio(
  resultados: readonly { id: string; ok: boolean; mensaje: string | null }[],
  folioPorId: ReadonlyMap<string, string>,
): ResultadoAprobacionItem[] {
  return resultados.map((x) => ({
    id: x.id,
    folio: folioPorId.get(x.id) ?? x.id,
    ok: x.ok,
    mensaje: x.mensaje,
  }));
}

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
export function ResumenAprobacionMasiva({
  resultados,
  onCerrar,
}: ResumenAprobacionMasivaProps) {
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
      className="space-y-2 rounded-md border bg-card p-3 text-sm"
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
                <span className="text-danger-fg">
                  {r.mensaje ?? 'Error al aprobar'}
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </section>
  );
}
