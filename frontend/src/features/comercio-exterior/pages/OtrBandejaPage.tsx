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
import { Info } from 'lucide-react';
import { Breadcrumbs, ErrorState } from '@/components/erp';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { errorUsuario } from '@/lib/error-usuario';
import { useOtrEnvios, useOtrAccion, type OtrEnvio } from '../api/otr';
import { motivoVinculacionOtr } from '../lib/motivo-vinculacion-otr';
import { VincularOtrSheet } from '../components/OtrPanel';
const columnas =
  'grid grid-cols-[120px_minmax(240px,1.6fr)_112px_136px_260px_172px] items-center [&>td]:min-w-0 [&>th]:min-w-0';
export function OtrBandejaPage() {
  const q = useOtrEnvios();
  const revalidar = useOtrAccion();
  const gestionarTodas = useHasPermission(P.ComercioExteriorDocumentosGestionarTodasSucursales);
  const [seleccion, setSeleccion] = useState<OtrEnvio>();
  const [texto, setTexto] = useState('');
  const [pagina, setPagina] = useState(0);
  const [estado, setEstado] = useState<number | undefined>(1);
  const gestionar = useHasPermission(P.ComercioExteriorOtrGestionar);
  const encontrados = q.data?.filter((e) =>
    `${e.idOrigen} ${e.contenido.cliente}`
      .toLocaleLowerCase('es-MX')
      .includes(texto.toLocaleLowerCase('es-MX')),
  );
  const conteos = encontrados
    ? {
        1: encontrados.filter((e) => e.estado === 1).length,
        2: encontrados.filter((e) => e.estado === 2).length,
        3: encontrados.filter((e) => e.estado === 3).length,
      }
    : undefined;
  const filas = encontrados?.filter((e) => estado === undefined || e.estado === estado) ?? [];
  return (
    <main className="flex h-full min-h-0 flex-col gap-5 overflow-auto text-sm">
      <div className="shrink-0">
        <Breadcrumbs items={[{ label: 'Comercio Exterior' }, { label: 'Bandeja OTR' }]} />
        <PageHeader
          titulo="Bandeja OTR"
          descripcion="Vincula cada envío de contenedor a un expediente de exportación del mismo cliente."
        />
      </div>
      <p role="note" className="flex shrink-0 items-start gap-2 text-xs text-ink-muted">
        <Info aria-hidden="true" className="size-4 shrink-0" />
        A+W OTR organiza transporte. No crea requisiciones, órdenes de compra ni proveedores.
      </p>
      {revalidar.isError && (
        <p role="alert" className="shrink-0 text-danger-fg">
          {errorUsuario(revalidar.error, 'No se pudo revalidar la sucursal.')}
        </p>
      )}
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
                conteos={conteos}
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
                <Table
                  aria-label="Envíos OTR"
                  className="grid h-full min-w-[1040px] grid-rows-[auto_1fr] text-sm"
                >
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
                        <TableCell className="break-words px-3 font-mono text-xs">
                          {e.idOrigen}
                        </TableCell>
                        <TableCell className="min-w-0 px-3">
                          <p className="truncate" title={e.contenido.cliente}>
                            {e.contenido.cliente}
                          </p>
                          <p
                            className="truncate text-xs text-ink-muted"
                            title={e.contenido.direccionEntrega}
                          >
                            {e.contenido.direccionEntrega}
                          </p>
                        </TableCell>
                        <TableCell className="break-words px-3 font-mono text-xs">
                          {e.contenido.contenedor.numero ?? (
                            <span className="text-ink-muted">Sin número</span>
                          )}
                        </TableCell>
                        <TableCell className="break-words px-3 text-xs tabular-nums">
                          {e.versionAplicada}
                        </TableCell>
                        <TableCell className="px-3">
                          <Badge variant={estadosOtr[e.estado].tono}>
                            {estadosOtr[e.estado].texto}
                          </Badge>
                          {e.motivoVinculacion && (
                            <p
                              id={`motivo-${e.id}`}
                              className="mt-1 break-words text-xs text-ink-secondary"
                            >
                              {motivoVinculacionOtr(e.motivoVinculacion)}
                            </p>
                          )}
                        </TableCell>
                        <TableCell className="px-3">
                          {gestionar &&
                          gestionarTodas &&
                          e.estado === 1 &&
                          e.motivoVinculacion === 'CE_OTR_SUCURSAL_DESCONOCIDA' ? (
                            <Button
                              variant="outline"
                              disabled={revalidar.isPending}
                              title={
                                revalidar.isPending
                                  ? 'Revalidando sucursal.'
                                  : 'Buscar la equivalencia vigente de la sucursal A+W.'
                              }
                              aria-describedby={`motivo-${e.id}`}
                              onClick={() => {
                                void revalidar
                                  .mutateAsync({ envio: e, ruta: 'revalidar-sucursal' })
                                  .catch(() => {});
                              }}
                            >
                              Revalidar sucursal
                            </Button>
                          ) : gestionar && e.estado === 1 ? (
                            <Button
                              variant="outline"
                              disabled={!!e.motivoVinculacion}
                              title={motivoVinculacionOtr(e.motivoVinculacion) ?? undefined}
                              aria-describedby={e.motivoVinculacion ? `motivo-${e.id}` : undefined}
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
      {seleccion && <VincularOtrSheet envio={seleccion} onClose={() => setSeleccion(undefined)} />}
    </main>
  );
}
