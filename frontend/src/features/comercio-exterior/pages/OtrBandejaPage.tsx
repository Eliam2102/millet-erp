import { KpiCard } from '@/components/erp/display/KpiCard';
import { estadosOtr } from '../lib/estados-ui';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { TabsEstado, BarraFiltros, PaginacionTabla } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
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
  const [estado, setEstado] = useState<number | undefined>(1);
  const gestionar = useHasPermission(P.ComercioExteriorOtrGestionar);
  const filas =
    q.data?.filter(
      (e) =>
        (estado === undefined || e.estado === estado) &&
        `${e.idOrigen} ${e.contenido.cliente}`
          .toLocaleLowerCase('es-MX')
          .includes(texto.toLocaleLowerCase('es-MX')),
    ) ?? [];
  const incidencias =
    q.data?.filter((e) => e.salidas.some((s) => s.estado === 4 || s.sinAcuse)) ?? [];
  return (
    <main className="flex h-full min-h-0 flex-col gap-5 overflow-auto text-sm">
      <Breadcrumbs items={[{ label: 'Comercio Exterior' }, { label: 'Bandeja OTR' }]} />
      <PageHeader
        titulo="Bandeja OTR"
        descripcion="Vincule cada envío de contenedor a un expediente de exportación del mismo cliente."
      />
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <Button
          variant="ghost"
          className="h-auto items-stretch p-0 text-left [&>div]:w-full"
          onClick={() => {
            setEstado(1);
            setTexto('');
            setPagina(0);
          }}
        >
          <KpiCard
            etiqueta="Por vincular"
            valor={q.data ? q.data.filter((e) => e.estado === 1).length : '—'}
            tono="warning"
          />
        </Button>
        <KpiCard
          etiqueta="Envíos con salidas fallidas o sin acuse"
          valor={q.data ? incidencias.length : '—'}
          tono="danger"
          href="#incidencias-otr"
        />
      </div>
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
        <div className="flex min-h-80 flex-1 flex-col">
          <ListadoModulo
            tabs={
              <TabsEstado
                opciones={[
                  { valor: 1, label: 'Por vincular' },
                  { valor: 2, label: 'Vinculados' },
                  { valor: 3, label: 'Cancelados' },
                ]}
                valor={estado}
                conteos={undefined}
                onChange={(v) => {
                  setEstado(v);
                  setPagina(0);
                }}
              />
            }
            filtros={
              <BarraFiltros
                busqueda={
                  <Input
                    aria-label="Buscar envío OTR o cliente"
                    placeholder="Envío OTR o cliente"
                    value={texto}
                    onChange={(e) => {
                      setTexto(e.target.value);
                      setPagina(0);
                    }}
                  />
                }
                total={`${filas.length} envíos`}
              />
            }
            pie={
              <PaginacionTabla
                total={filas.length}
                offset={pagina * 10}
                limit={10}
                tamanos={[]}
                onChange={(offset) => setPagina(offset / 10)}
              />
            }
          >
            <div className="h-full min-h-0 overflow-auto">
              {filas.length > 0 && (
                <Table className="grid h-full min-w-0 grid-rows-[auto_1fr] text-sm">
                  <TableHeader className="h-9 bg-surface-subtle text-left text-2xs uppercase text-ink-muted">
                    <TableRow className={`${columnas} h-9`}>
                      {[
                        'Envío OTR',
                        'Cliente y entrega',
                        'Contenedor',
                        'Versión',
                        'Estado',
                        'Acciones',
                      ].map((x) => (
                        <TableHead key={x} className="px-3">
                          {x}
                        </TableHead>
                      ))}
                    </TableRow>
                  </TableHeader>
                  <TableBody className="grid auto-rows-fr">
                    {filas.slice(pagina * 10, pagina * 10 + 10).map((e) => (
                      <TableRow
                        className={`${columnas} min-h-12 border-b border-line-row`}
                        key={e.id}
                      >
                        <TableCell className="px-3 font-mono text-xs">{e.idOrigen}</TableCell>
                        <TableCell className="min-w-0 truncate px-3">
                          {e.contenido.cliente}
                          <p className="text-xs text-ink-muted">{e.contenido.direccionEntrega}</p>
                        </TableCell>
                        <TableCell className="px-3 font-mono text-xs">
                          {e.contenido.contenedor.numero ?? <span className="text-ink-muted">Sin número</span>}
                        </TableCell>
                        <TableCell className="px-3 tabular-nums">{e.versionAplicada}</TableCell>
                        <TableCell className="px-3">
                          <Badge variant={estadosOtr[e.estado].tono}>
                            {estadosOtr[e.estado].texto}
                          </Badge>
                          {e.motivoVinculacion && (
                            <p className="text-xs text-ink-muted">{e.motivoVinculacion}</p>
                          )}
                        </TableCell>
                        <TableCell className="px-3">
                          {gestionar && e.estado === 1 ? (
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
                              {e.estado === 2
                                ? 'Envío vinculado.'
                                : e.estado === 3
                                  ? 'Envío cancelado.'
                                  : 'Requiere permiso de gestión OTR.'}
                            </span>
                          )}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
              {!q.data && (
                <p role="status" className="p-4">
                  Cargando envíos OTR…
                </p>
              )}
              {q.data && filas.length === 0 && (
                <p className="p-4 text-ink-muted">No hay envíos OTR en esta vista.</p>
              )}
            </div>
          </ListadoModulo>
        </div>
      )}
      {!q.isError && q.data && (
        <section
          id="incidencias-otr"
          aria-label="Salidas fallidas o sin acuse"
          className="space-y-3 pb-5"
        >
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
