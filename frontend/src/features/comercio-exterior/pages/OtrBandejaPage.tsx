import { useState } from 'react';
import { TriangleAlert } from 'lucide-react';
import { Breadcrumbs, ErrorState } from '@/components/erp';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useOtrEnvios, type OtrEnvio } from '../api/otr';
import { OtrDetalle, VincularOtrSheet } from '../components/OtrPanel';
const columnas = 'grid grid-cols-[150px_minmax(0,1.6fr)_160px_64px_130px_160px] items-center';
export function OtrBandejaPage() {
  const q = useOtrEnvios();
  const [seleccion, setSeleccion] = useState<OtrEnvio>();
  const [texto, setTexto] = useState('');
  const [pagina, setPagina] = useState(0);
  const gestionar = useHasPermission(P.ComercioExteriorOtrGestionar);
  const filas =
    q.data?.filter(
      (e) =>
        e.estado === 1 &&
        `${e.idOrigen} ${e.contenido.cliente}`
          .toLocaleLowerCase('es-MX')
          .includes(texto.toLocaleLowerCase('es-MX')),
    ) ?? [];
  const incidencias =
    q.data?.filter((e) => e.salidas.some((s) => s.estado === 4 || s.sinAcuse)) ?? [];
  return (
    <main className="flex h-full min-h-0 flex-col gap-4 overflow-auto px-6 pt-5 text-sm">
      <Breadcrumbs items={[{ label: 'Comercio Exterior' }, { label: 'Bandeja OTR' }]} />
      <header>
        <h1 className="text-3xl font-semibold">Bandeja OTR</h1>
        <p className="text-ink-muted">
          Vincule cada envío de contenedor a un expediente de exportación del mismo cliente.
        </p>
      </header>
      <p
        role="note"
        className="flex items-start gap-2 rounded-md bg-warning-note-bg px-3 py-2.5 text-warning-note-fg"
      >
        <TriangleAlert aria-hidden="true" className="size-4 shrink-0" />
        A+W OTR organiza transporte. No crea requisiciones, órdenes de compra ni proveedores.
      </p>
      {q.isError ? (
        <ErrorState
          title="No se pudieron cargar los envíos OTR"
          onRetry={() => {
            void q.refetch();
          }}
        />
      ) : (
        <section
          className="flex min-h-0 flex-1 flex-col rounded-t-lg bg-surface-card shadow-card-flat"
          aria-label="Envíos OTR por vincular"
        >
          <h2 className="px-4 pt-4 text-md font-semibold">Envíos OTR por vincular</h2>
          <div className="flex items-center gap-3 border-b border-line-divider px-4 py-2.5">
            <Input
              aria-label="Buscar envío OTR o cliente"
              placeholder="Envío OTR o cliente"
              value={texto}
              onChange={(e) => {
                setTexto(e.target.value);
                setPagina(0);
              }}
            />
            <span className="shrink-0 text-xs text-ink-muted">{filas.length} por vincular</span>
          </div>
          <div className="flex-1 overflow-auto">
            {filas.length > 0 && (
              <table className="grid h-full min-w-[960px] grid-rows-[36px_1fr] text-sm">
                <thead className="h-9 bg-surface-subtle text-left text-2xs uppercase text-ink-muted">
                  <tr className={`${columnas} h-9`}>
                    {[
                      'Envío OTR',
                      'Cliente y entrega',
                      'Contenedor',
                      'Versión',
                      'Estado',
                      'Acciones',
                    ].map((x) => (
                      <th key={x} className="px-3">
                        {x}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="grid auto-rows-[minmax(48px,1fr)]">
                  {filas.slice(pagina * 10, pagina * 10 + 10).map((e) => (
                    <tr className={`${columnas} min-h-12 border-b border-line-row`} key={e.id}>
                      <td className="px-3 font-mono text-xs">{e.idOrigen}</td>
                      <td className="min-w-0 truncate px-3">
                        {e.contenido.cliente}
                        <p className="text-xs text-ink-muted">{e.contenido.direccionEntrega}</p>
                      </td>
                      <td className="px-3 font-mono text-xs">
                        {e.contenido.contenedor.numero ?? '[SIN NÚMERO]'}
                      </td>
                      <td className="px-3 tabular-nums">{e.versionAplicada}</td>
                      <td className="px-3">
                        <Badge variant="warning">Por vincular</Badge>
                        {e.motivoVinculacion && (
                          <p className="text-xs text-ink-muted">{e.motivoVinculacion}</p>
                        )}
                      </td>
                      <td className="px-3">
                        {gestionar ? (
                          <Button
                            variant="outline"
                            disabled={!!e.motivoVinculacion}
                            title={e.motivoVinculacion ?? undefined}
                            onClick={() => setSeleccion(e)}
                          >
                            Vincular envío
                          </Button>
                        ) : (
                          <span className="text-xs text-ink-muted">
                            Requiere permiso de gestión OTR.
                          </span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
            {!q.data && (
              <p role="status" className="p-4">
                Cargando envíos OTR…
              </p>
            )}
            {q.data && filas.length === 0 && (
              <p className="p-4 text-ink-muted">No hay envíos OTR por vincular.</p>
            )}
          </div>
          <footer className="flex h-11 items-center justify-between gap-3 border-t border-line-divider px-4 text-xs text-ink-muted">
            <span>
              {filas.length ? pagina * 10 + 1 : 0}–{Math.min((pagina + 1) * 10, filas.length)} de{' '}
              {filas.length}
            </span>
            <div className="flex gap-2">
              <Button
                size="sm"
                variant="ghost"
                disabled={pagina === 0}
                title={pagina === 0 ? 'Primera página.' : undefined}
                onClick={() => setPagina((p) => p - 1)}
              >
                Anterior
              </Button>
              <Button
                size="sm"
                variant="ghost"
                disabled={(pagina + 1) * 10 >= filas.length}
                title={(pagina + 1) * 10 >= filas.length ? 'Última página.' : undefined}
                onClick={() => setPagina((p) => p + 1)}
              >
                Siguiente
              </Button>
            </div>
          </footer>
        </section>
      )}
      {!q.isError && q.data && (
        <section aria-label="Salidas fallidas o sin acuse" className="space-y-3 pb-5">
          <h2 className="text-md font-semibold">Salidas fallidas o sin acuse</h2>
          {incidencias.length === 0 && (
            <p className="text-ink-muted">Sin salidas fallidas ni acuses vencidos.</p>
          )}
          {incidencias.map((envio) => (
            <OtrDetalle
              key={envio.id}
              abierto={false}
              envio={{
                ...envio,
                salidas: envio.salidas.filter((s) => s.estado === 4 || s.sinAcuse),
              }}
            />
          ))}
        </section>
      )}
      {seleccion && <VincularOtrSheet envio={seleccion} onClose={() => setSeleccion(undefined)} />}
    </main>
  );
}
