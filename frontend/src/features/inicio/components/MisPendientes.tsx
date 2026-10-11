import { useState } from 'react';
import { Card, CardHeader, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import type { useResumenInicio } from '../api/useResumenInicio';
import { FilaPendiente } from './FilaPendiente';
import { filtrarPendientes, type FiltroPendientes } from '../prioridad';

export function MisPendientes({ resumen }: { resumen: ReturnType<typeof useResumenInicio> }) {
  const { filas, errores, cargando } = resumen;
  const [tab, setTab] = useState<FiltroPendientes>('Todo');
  const filtradas = filtrarPendientes(filas, tab);
  const vacio = !filas.length && !errores.length && !cargando;
  return (
    <section aria-labelledby="requiere-accion" className="min-h-0 min-w-0">
      <Card className="flex h-full min-h-0 flex-col rounded-xl bg-surface-card text-ink shadow-card-flat">
        <CardHeader className="px-4 py-3">
          <h2 id="requiere-accion" className="text-lg font-semibold">
            Requiere tu acción
          </h2>
        </CardHeader>
        <div role="tablist" aria-label="Filtrar pendientes" className="flex gap-1 border-b border-line-divider px-4 pb-2">
          {(['Todo', 'Autorizar', 'Revisar', 'Vencido'] as const).map(t => <Button key={t} role="tab" aria-selected={tab === t}
            variant={tab === t ? 'secondary' : 'ghost'} size="sm" onClick={() => setTab(t)}>
            {t} · {filtrarPendientes(filas, t).length}
          </Button>)}
        </div>
        <div role="tabpanel" aria-label={tab} className="min-h-0 flex-1 overflow-y-auto">
        {!!filtradas.length && (
          <ul>
            {filtradas.map((item) => (
              <FilaPendiente key={item.fila.id} item={item} />
            ))}
          </ul>
        )}
        {!vacio && !filtradas.length && !cargando && !errores.length && <CardContent><p className="text-xs text-ink-muted">Sin pendientes en esta vista.</p></CardContent>}
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
        </div>
      </Card>
    </section>
  );
}
