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
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { useNuevoExpediente } from '../components/nuevo-expediente-context';
import { EstadoExpedienteBadge } from '../components/EstadoExpedienteBadge';
import { useExpedientes } from '../api/hooks';
import type { Filtros } from '../api/types';
export function ExpedientesPage() {
  const search = useSearch({ from: '/_app/comercio-exterior/expedientes/' });
  const navigate = useNavigate();
  const currentSucursalId = useAuthStore((s) => s.currentSucursalId) ?? undefined;
  const canLeerTodas = useHasPermission(P.ComercioExteriorDocumentosLeerTodasSucursales);
  const sucursalId = canLeerTodas
    ? search.sucursalId === 'todas'
      ? undefined
      : (search.sucursalId ?? currentSucursalId)
    : currentSucursalId;
  const nuevoExpediente = useNuevoExpediente();
  const canCrear = useHasPermission(P.ComercioExteriorExpedientesCrear);
  const query = useExpedientes({ ...search, sucursalId });
  const pagina = query.data;
  function filtrar(patch: Filtros) {
    void navigate({
      to: '/comercio-exterior/expedientes',
      search: { ...search, offset: 0, ...patch },
    });
  }
  return (
    <main className="flex h-full min-h-0 flex-col gap-5 text-sm">
      <PageHeader
        titulo="Expedientes"
        descripcion="Importaciones y exportaciones: documentos, hitos y avisos de atraso."
        acciones={
          canCrear && (
            <Button onClick={nuevoExpediente.abrir}>
              <Plus className="size-3.5" />
              Nuevo expediente
            </Button>
          )
        }
      />
      <ListadoModulo
        tabs={
          <TabsEstado
            aria-label="Estado del expediente"
            opciones={[
              { valor: 1, label: 'Abiertos' },
              { valor: 2, label: 'Cancelados' },
            ]}
            valor={search.estado}
            conteos={undefined}
            onChange={(estado) => filtrar({ estado })}
          />
        }
        filtros={
          <BarraFiltros
            busqueda={
              <Input
                aria-label="Buscar por folio o referencia"
                placeholder="Folio o referencia"
                className="h-ctl-md w-64"
                value={search.q ?? ''}
                onChange={(e) => filtrar({ q: e.target.value || undefined })}
              />
            }
            filtros={
              <>
                {' '}
                <label className="flex items-center gap-2 text-xs text-ink-secondary">
                  Tipo
                  <select
                    aria-label="Filtrar tipo"
                    value={search.tipo ?? ''}
                    className="h-ctl-md rounded-md border border-line-control bg-surface-card px-2"
                    onChange={(e) =>
                      filtrar({ tipo: e.target.value ? Number(e.target.value) : undefined })
                    }
                  >
                    <option value="">Todos</option>
                    <option value={1}>Importación</option>
                    <option value={2}>Exportación</option>
                  </select>
                </label>
                <label className="flex items-center gap-2 text-xs text-ink-secondary">
                  Atraso
                  <select
                    aria-label="Filtrar atraso"
                    value={search.atraso === undefined ? '' : String(search.atraso)}
                    className="h-ctl-md rounded-md border border-line-control bg-surface-card px-2"
                    onChange={(e) =>
                      filtrar({
                        atraso: e.target.value === '' ? undefined : e.target.value === 'true',
                      })
                    }
                  >
                    <option value="">Todos</option>
                    <option value="true">Con atraso</option>
                    <option value="false">Sin atraso</option>
                  </select>
                </label>
                {canLeerTodas && (
                  <div className="flex items-center gap-2 text-xs text-ink-secondary">
                    <span>Sucursal</span>
                    <SucursalSelector
                      value={sucursalId}
                      placeholder="Todas"
                      onChange={(id) => filtrar({ sucursalId: id ?? 'todas' })}
                    />
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => filtrar({ sucursalId: 'todas' })}
                    >
                      Todas
                    </Button>
                  </div>
                )}
              </>
            }
            total={`${pagina?.total ?? 0} expedientes`}
          />
        }
        pie={
          <PaginacionTabla
            total={pagina?.total ?? 0}
            offset={search.offset ?? 0}
            limit={10}
            tamanos={[]}
            onChange={(offset) => filtrar({ offset })}
          />
        }
      >
        {query.isError ? (
          <ErrorState
            title="No se pudieron cargar los expedientes"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => {
              void query.refetch();
            }}
          />
        ) : query.isLoading ? (
          <TableSkeleton
            rows={10}
            columns={[{ width: 'w-48' }, { width: 'w-48' }, { width: 'w-32' }]}
          />
        ) : !pagina?.items.length ? (
          <EmptyState
            title="Sin expedientes"
            description="Los expedientes de las sucursales a las que tienes acceso aparecerán aquí."
          />
        ) : (
          <div className="flex h-full min-h-0 flex-col overflow-auto">
            <Table className="h-full w-full text-left text-sm">
              <TableHeader className="h-9 bg-surface-subtle text-2xs uppercase tracking-wide text-ink-muted">
                <TableRow>
                  {[
                    'Folio',
                    'Referencia',
                    'Tipo',
                    'Sucursal',
                    'Responsable',
                    'Atraso',
                    'Avisos',
                    'Estado',
                  ].map((c) => (
                    <TableHead key={c} className="px-3 font-semibold">
                      {c}
                    </TableHead>
                  ))}
                </TableRow>
              </TableHeader>
              <TableBody>
                {pagina.items.map((e) => (
                  <TableRow key={e.id} className="h-12 border-b border-line-row">
                    <TableCell className="px-3">
                      <Link
                        to="/comercio-exterior/expedientes/$id"
                        params={{ id: e.id }}
                        className="font-mono text-xs font-medium text-brand"
                      >
                        {e.folio}
                      </Link>
                    </TableCell>
                    <TableCell className="max-w-64 truncate px-3">{e.referencia}</TableCell>
                    <TableCell className="px-3">
                      {e.tipo === 1 ? 'Importación' : 'Exportación'}
                    </TableCell>
                    <TableCell className="px-3">{e.sucursalNombre}</TableCell>
                    <TableCell className="px-3">{e.responsableNombre}</TableCell>
                    <TableCell className="px-3">
                      <span
                        className={`inline-flex items-center gap-1.5 text-xs ${e.hitosAtrasados > 0 ? 'font-medium text-danger-fg' : 'text-ink-secondary'}`}
                      >
                        <span
                          aria-hidden="true"
                          className={`size-2 shrink-0 rounded-full ${e.hitosAtrasados > 0 ? 'bg-danger-solid' : 'bg-success-solid'}`}
                        />
                        {e.hitosAtrasados > 0
                          ? `${e.hitosAtrasados} ${e.hitosAtrasados === 1 ? 'hito vencido' : 'hitos vencidos'}`
                          : 'Sin atraso'}
                      </span>
                    </TableCell>
                    <TableCell className="px-3 tabular-nums">{e.avisos}</TableCell>
                    <TableCell className="px-3">
                      <EstadoExpedienteBadge estado={e.estado} />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </ListadoModulo>
    </main>
  );
}
