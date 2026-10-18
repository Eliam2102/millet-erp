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
    <main className="flex h-full min-h-0 flex-col gap-4 px-6 pt-5 text-sm">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-3xl font-semibold text-ink">Expedientes</h1>
          <p className="mt-1 text-ink-muted">
            Importaciones y exportaciones: documentos, hitos y avisos de atraso.
          </p>
        </div>
        {canCrear && (
          <Button onClick={nuevoExpediente.abrir}>
            <Plus className="size-3.5" />
            Nuevo expediente
          </Button>
        )}
      </header>
      <section className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-t-lg bg-surface-card shadow-card-flat">
        <div
          role="tablist"
          aria-label="Estado del expediente"
          className="flex gap-5 border-b border-line-divider px-4"
        >
          {(
            [
              { value: 1, label: 'Abiertos' },
              { value: 2, label: 'Cancelados' },
              { value: undefined, label: 'Todas' },
            ] as const
          ).map((t) => (
            <button
              key={t.label}
              role="tab"
              aria-selected={search.estado === t.value}
              onClick={() => filtrar({ estado: t.value })}
              className={`h-11 text-sm ${search.estado === t.value ? 'font-medium text-ink shadow-tab-active' : 'text-ink-muted'}`}
            >
              {t.label}
            </button>
          ))}
        </div>
        <div className="flex flex-wrap items-center gap-3 border-b border-line-divider px-4 py-2.5">
          <Input
            aria-label="Buscar por folio o referencia"
            placeholder="Folio o referencia"
            className="h-ctl-md w-64"
            value={search.q ?? ''}
            onChange={(e) => filtrar({ q: e.target.value || undefined })}
          />
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
                filtrar({ atraso: e.target.value === '' ? undefined : e.target.value === 'true' })
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
              <Button variant="ghost" size="sm" onClick={() => filtrar({ sucursalId: 'todas' })}>
                Todas
              </Button>
            </div>
          )}
          <span className="ml-auto text-xs text-ink-muted">{pagina?.total ?? 0} expedientes</span>
        </div>
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
          <div className="flex min-h-0 flex-1 flex-col overflow-auto">
            <table className="h-full w-full text-left text-sm">
              <thead className="h-9 bg-surface-subtle text-2xs uppercase tracking-wide text-ink-muted">
                <tr>
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
                    <th key={c} className="px-3 font-semibold">
                      {c}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {pagina.items.map((e) => (
                  <tr key={e.id} className="h-12 border-b border-line-row">
                    <td className="px-3">
                      <Link
                        to="/comercio-exterior/expedientes/$id"
                        params={{ id: e.id }}
                        className="font-mono text-xs font-medium text-brand"
                      >
                        {e.folio}
                      </Link>
                    </td>
                    <td className="max-w-64 truncate px-3">{e.referencia}</td>
                    <td className="px-3">{e.tipo === 1 ? 'Importación' : 'Exportación'}</td>
                    <td className="px-3">{e.sucursalNombre}</td>
                    <td className="px-3">{e.responsableNombre}</td>
                    <td className="px-3">
                      <span
                        className={`inline-flex items-center gap-1.5 text-xs ${e.hitosAtrasados > 0 ? 'font-medium text-danger-fg' : 'text-ink-secondary'}`}
                      >
                        <span
                          aria-hidden="true"
                          className={`size-[7px] shrink-0 rounded-full ${e.hitosAtrasados > 0 ? 'bg-danger-solid' : 'bg-success-solid'}`}
                        />
                        {e.hitosAtrasados > 0
                          ? `${e.hitosAtrasados} ${e.hitosAtrasados === 1 ? 'hito vencido' : 'hitos vencidos'}`
                          : 'Sin atraso'}
                      </span>
                    </td>
                    <td className="px-3 tabular-nums">{e.avisos}</td>
                    <td className="px-3">
                      <EstadoExpedienteBadge estado={e.estado} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <footer className="mt-auto flex h-11 shrink-0 items-center justify-between border-t border-line-divider px-4 text-xs text-ink-muted">
          <span>
            {pagina?.total
              ? `${(search.offset ?? 0) + 1}–${Math.min((search.offset ?? 0) + 10, pagina.total)} de ${pagina.total}`
              : '0 expedientes'}
          </span>
          <div className="flex gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={!search.offset}
              title={!search.offset ? 'Primera página.' : undefined}
              onClick={() => filtrar({ offset: Math.max(0, (search.offset ?? 0) - 10) })}
            >
              Anterior
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={!pagina || (search.offset ?? 0) + 10 >= pagina.total}
              title={
                !pagina || (search.offset ?? 0) + 10 >= pagina.total ? 'Última página.' : undefined
              }
              onClick={() => filtrar({ offset: (search.offset ?? 0) + 10 })}
            >
              Siguiente
            </Button>
          </div>
        </footer>
      </section>
    </main>
  );
}
