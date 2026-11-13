import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { CheckCircle2, Plus, TriangleAlert } from 'lucide-react';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { BarraFiltros, PaginacionTabla, EmptyState, ErrorState, TableSkeleton,
} from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from '@/components/ui/table';
import { esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { useIncidencias, type EstadoIncidencia, type Incidencia } from '../api/incidencias';
import { RegistrarIncidenciaSheet } from '../components/incidencias/RegistrarIncidenciaSheet';
import { ResolverIncidenciaSheet } from '../components/incidencias/ResolverIncidenciaSheet';
import { TabsContabilidad } from '../components/TabsContabilidad';
import { ESTADOS_INCIDENCIA, fechaHora, type IncidenciasSearch } from '../lib/incidencias';
import { monto } from '../lib/polizas';
import { usePaginacionLocal } from '../lib/use-paginacion-local';

const VISTAS = Object.entries(ESTADOS_INCIDENCIA).map(([estado, ui]) => ({
  estado: estado as EstadoIncidencia,
  label: ui.texto,
}));

interface Props {
  search: IncidenciasSearch;
  onSearch: (next: IncidenciasSearch) => void;
}

/**
 * Incidencias posteriores (M-321, P1): lo hallado después de autorizar una póliza o cerrar un periodo. Registrar y resolver
 * usan el permiso de captura de pólizas; consultar, el de lectura.
 */
export function IncidenciasPage({ search, onSearch }: Props) {
  const puedeCapturar = useHasPermission(PermisosCanonicos.ContabilidadPolizasCapturar);
  const [nueva, setNueva] = useState(false);
  const [resolviendo, setResolviendo] = useState<Incidencia | null>(null);
  const incidencias = useIncidencias(search.estado);
  const items = incidencias.data ?? [];
  const pagina = usePaginacionLocal(items, search.estado ?? '');

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        titulo={
          <>
            <TriangleAlert className="mr-2 inline-block size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            Incidencias posteriores
          </>
        }
        descripcion={
          <>
            Lo que se encontró después de autorizar una póliza o cerrar un periodo. Una incidencia abierta aparece
            como pendiente en el cierre de su periodo hasta que se liga su póliza de ajuste o su reapertura acotada.
          </>
        }
        acciones={
          puedeCapturar ? (
            <div className="ml-auto" data-print="hidden">
              <Button onClick={() => setNueva(true)}>
                <Plus className="mr-1 size-3.5" aria-hidden="true" />
                Registrar incidencia
              </Button>
            </div>
          ) : undefined
        }
      />

      <ListadoModulo
        tabs={
          <TabsContabilidad
            estados={VISTAS}
            valor={search.estado}
            onChange={(estado) => onSearch({ ...search, estado })}
          />
        }
        filtros={<BarraFiltros total={`${items.length} incidencias`} />}
        pie={
          <PaginacionTabla regionId={pagina.regionId}
            total={items.length}
            offset={pagina.offset}
            limit={pagina.limit}
            onChange={pagina.onChange}
          />
        }
      >
        {incidencias.isLoading ? (
          <TableSkeleton rows={5} />
        ) : incidencias.isError ? (
          <ErrorState
            title="No se pudieron cargar las incidencias"
            problem={esApiError(incidencias.error) ? incidencias.error.problem : undefined}
            onRetry={() => void incidencias.refetch()}
          />
        ) : items.length === 0 ? (
          <EmptyState
            title={
              search.estado
                ? 'Ninguna incidencia en este estado.'
                : 'No hay incidencias registradas. Registra la primera con «Registrar incidencia».'
            }
          />
        ) : (
          <div className="overflow-x-auto">
            <Table className="w-full min-w-270 text-sm">
              <TableHeader className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
                <TableRow>
                  <TableHead className="h-9 px-3">Periodo</TableHead>
                  <TableHead className="px-3">Póliza</TableHead>
                  <TableHead className="px-3">Hallazgo</TableHead>
                  <TableHead className="px-3">Impacto</TableHead>
                  <TableHead className="px-3 text-right">Importe</TableHead>
                  <TableHead className="px-3">Estado</TableHead>
                  <TableHead className="px-3">Registró</TableHead>
                  <TableHead className="px-3">Resolución</TableHead>
                  <TableHead className="px-3">
                    <span className="sr-only">Acciones</span>
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {pagina.visibles.map((i) => (
                  <TableRow key={i.id} className="border-b border-line-row align-top">
                    <TableCell className="whitespace-nowrap px-3 py-2.5 font-mono text-xs">
                      {i.periodo}
                    </TableCell>
                    <TableCell className="whitespace-nowrap px-3 py-2.5">
                      {i.polizaId ? (
                        <Link
                          to="/contabilidad/polizas/$id"
                          params={{ id: i.polizaId }}
                          className="font-medium text-brand hover:underline"
                        >
                          {i.polizaFolio ?? 'Ver póliza'}
                        </Link>
                      ) : (
                        <span className="text-ink-muted">Periodo cerrado</span>
                      )}
                    </TableCell>
                    <TableCell className="max-w-72 whitespace-normal px-3 py-2.5">
                      {i.hallazgo}
                    </TableCell>
                    <TableCell className="max-w-60 whitespace-normal px-3 py-2.5 text-ink-secondary">
                      {i.impacto}
                    </TableCell>
                    <TableCell className="whitespace-nowrap px-3 py-2.5 text-right tabular-nums">
                      {i.importeImpacto === null ? '—' : monto(i.importeImpacto)}
                    </TableCell>
                    <TableCell className="px-3 py-2.5">
                      <Badge variant={ESTADOS_INCIDENCIA[i.estado].tono}>
                        {ESTADOS_INCIDENCIA[i.estado].texto}
                      </Badge>
                    </TableCell>
                    <TableCell className="px-3 py-2.5 text-xs text-ink-secondary">
                      {i.registradaPor}
                      <div className="text-ink-muted">{fechaHora(i.registradaEn)}</div>
                    </TableCell>
                    <TableCell className="max-w-56 whitespace-normal px-3 py-2.5 text-xs text-ink-secondary">
                      {i.estado === 'Resuelta' ? (
                        <>
                          {i.resolucion === 'PolizaAjuste'
                            ? `Póliza de ajuste ${i.polizaAjusteFolio ?? ''}`.trim()
                            : 'Reapertura acotada'}
                          {i.notaResolucion && <div>{i.notaResolucion}</div>}
                          <div className="text-ink-muted">
                            {i.resueltaPor}
                            {i.resueltaEn ? ` · ${fechaHora(i.resueltaEn)}` : ''}
                          </div>
                        </>
                      ) : (
                        '—'
                      )}
                    </TableCell>
                    <TableCell className="px-3 py-2.5 text-right" data-print="hidden">
                      {puedeCapturar && i.estado === 'Abierta' && (
                        <Button variant="outline" size="sm" onClick={() => setResolviendo(i)}>
                          <CheckCircle2 className="mr-1 size-3.5" aria-hidden="true" />
                          Resolver
                        </Button>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </ListadoModulo>

      <RegistrarIncidenciaSheet open={nueva} onOpenChange={setNueva} />
      <ResolverIncidenciaSheet
        incidencia={resolviendo}
        onOpenChange={(o) => !o && setResolviendo(null)}
      />
    </div>
  );
}
