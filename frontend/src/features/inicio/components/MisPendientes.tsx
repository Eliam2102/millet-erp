import { Card, CardHeader, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import type { useResumenInicio } from '../api/useResumenInicio';
import { FilaPendiente } from './FilaPendiente';

export function MisPendientes({ resumen }: { resumen: ReturnType<typeof useResumenInicio> }) {
  const { filas, errores, cargando } = resumen;
  const vacio = !filas.length && !errores.length && !cargando;
  return (
    <section aria-labelledby="requiere-accion" className="min-w-0">
      <Card className="rounded-xl bg-surface-card text-ink shadow-card-flat">
        <CardHeader>
          <h2 id="requiere-accion" className="text-lg font-semibold">
            Requiere tu acción
          </h2>
        </CardHeader>
        {!!filas.length && (
          <ul>
            {filas.map((item) => (
              <FilaPendiente key={item.fila.id} item={item} />
            ))}
          </ul>
        )}
        {vacio && (
          <CardContent>
            <p className="text-sm text-ink-muted">No tienes pendientes</p>
          </CardContent>
        )}
        {cargando && (
          <CardContent>
            <p role="status" className="text-xs text-ink-muted">
              Cargando pendientes…
            </p>
          </CardContent>
        )}
        {!!errores.length && (
          <CardContent className="space-y-2 pt-3">
            {errores.map((item) => (
              <div key={item.fila.id} className="flex flex-wrap items-center justify-between gap-2">
                <p role="status" className="text-xs text-danger-fg">
                  {item.fila.titulo}: no se pudo cargar.
                </p>
                <Button
                  variant="outline"
                  size="sm"
                  disabled={item.isFetching}
                  aria-label={`Reintentar ${item.fila.titulo}`}
                  onClick={() => {
                    void item.refetch();
                  }}
                >
                  Reintentar
                </Button>
              </div>
            ))}
          </CardContent>
        )}
      </Card>
    </section>
  );
}
