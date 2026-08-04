import { useMemo, type ReactNode } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Inbox } from 'lucide-react';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
} from '@/components/erp';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Button } from '@/components/ui/button';
import { usePendientesAutorizacion } from '@/features/compras/api/usePendientesAutorizacion';
import { useDepartamentos, mapById } from '@/features/catalogos/api';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ListaPendientesCompacta } from '@/features/compras/components/ListaPendientesCompacta';
import type { PendientesSearch } from '@/features/compras/lib/pendientes-search-schema';
import { cn } from '@/lib/utils';

/**
 * <c>P2 Bandeja de pendientes — master-detail layout</c>
 * (design/frontend-polish).
 *
 * <para>Análogo a <c>&lt;RequisicionesLayout/&gt;</c> pero usando
 * <c>usePendientesAutorizacion</c> (que filtra server-side
 * <c>estado=EnAutorizacion</c>) y <c>PendientesSearch</c> como shape
 * de search params. Lista compacta sticky 320px + panel detalle
 * (renderiza el mismo <c>&lt;DetalleRequisicion/&gt;</c> via prop
 * <c>detalle</c>). El detalle infiere su CTA "Cerrar" del pathname
 * (<c>/pendientes/$id</c> vs <c>/requisiciones/$id</c>) y vuelve a
 * la bandeja correspondiente.</para>
 *
 * <para>El filtro de estado del P1 acá no aplica (el endpoint lo fija);
 * solo se expone el filtro de departamento (gateado por
 * <c>ver-todos-departamentos</c>) y la búsqueda por folio (input del
 * topbar). Mismo drill-down mobile que P1.</para>
 */
export interface PendientesLayoutProps {
  idActivo: string | null;
  detalle?: ReactNode;
}

const SENTINEL_ALL = '__all__';

export function PendientesLayout({
  idActivo,
  detalle,
}: PendientesLayoutProps) {
  const search = useSearch({ strict: false }) as PendientesSearch;
  const navigate = useNavigate();
  const verTodosDepartamentos = useHasPermission(
    PermisosCanonicos.ComprasRequisicionesVerTodosDepartamentos,
  );

  const query = usePendientesAutorizacion({
    departamentoId: search.departamentoId,
    offset: search.offset,
    limit: search.limit,
  });
  const departamentosQuery = useDepartamentos();

  const deptosMap = useMemo(
    () => mapById(departamentosQuery.data?.items),
    [departamentosQuery.data],
  );

  const itemsFiltrados = useMemo(() => {
    const items = query.data?.items ?? [];
    if (!search.q) return items;
    const needle = search.q.toLowerCase();
    return items.filter((r) => r.folio.toLowerCase().includes(needle));
  }, [query.data, search.q]);

  function setSearch(next: PendientesSearch) {
    if (idActivo != null) {
      navigate({
        to: '/compras/pendientes/$id',
        params: { id: idActivo },
        search: next,
        replace: false,
      });
    } else {
      navigate({
        to: '/compras/pendientes',
        search: next,
        replace: false,
      });
    }
  }

  function handleDeptoChange(value: string) {
    setSearch({
      ...search,
      departamentoId: value === SENTINEL_ALL ? undefined : value,
      offset: 0,
    });
  }

  function handleLimpiar() {
    setSearch({ offset: 0, limit: search.limit });
  }

  const hayFiltros =
    search.departamentoId != null ||
    (search.q != null && search.q.length > 0);

  return (
    <div className="flex flex-col gap-4 md:h-[calc(100vh-3.5rem-3rem)] md:flex-row">
      <aside
        className={cn(
          'flex flex-col gap-3 md:w-80 md:shrink-0 md:overflow-hidden',
          idActivo != null ? 'hidden md:flex' : 'flex',
        )}
        aria-label="Lista de pendientes"
        data-print="hidden"
      >
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h1 className="text-xl font-semibold tracking-tight">
            Pendientes
          </h1>
        </div>

        <div
          className="flex flex-wrap items-center gap-2"
          role="search"
          aria-label="Filtros de bandeja de pendientes"
        >
          {verTodosDepartamentos && (
            <Select
              value={search.departamentoId ?? SENTINEL_ALL}
              onValueChange={handleDeptoChange}
            >
              <SelectTrigger
                aria-label="Filtrar por departamento"
                className="h-9 w-auto min-w-40 gap-1 font-medium"
              >
                <SelectValue placeholder="Todos los departamentos" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={SENTINEL_ALL}>
                  Todos los departamentos
                </SelectItem>
                {Array.from(deptosMap.values()).map((d) => (
                  <SelectItem key={d.id} value={d.id}>
                    {d.clave} · {d.nombre}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}

          {hayFiltros && (
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={handleLimpiar}
            >
              Limpiar
            </Button>
          )}
        </div>

        <div className="flex-1 overflow-y-auto rounded-md border bg-card">
          <RenderListaContent
            query={query}
            items={itemsFiltrados}
            idActivo={idActivo}
            search={search}
          />
        </div>
      </aside>

      <section
        className={cn(
          'min-w-0 flex-1 md:overflow-y-auto',
          idActivo == null ? 'hidden md:block' : 'block',
        )}
        aria-label="Detalle de requisición pendiente"
      >
        {idActivo == null ? <PlaceholderSinSeleccion /> : detalle}
      </section>
    </div>
  );
}

interface RenderListaContentProps {
  query: ReturnType<typeof usePendientesAutorizacion>;
  items: readonly import('@/features/compras/api/types').RequisicionListItemResponse[];
  idActivo: string | null;
  search: PendientesSearch;
}

function RenderListaContent({
  query,
  items,
  idActivo,
  search,
}: RenderListaContentProps) {
  if (query.isLoading) {
    return (
      <div className="p-3">
        <TableSkeleton
          rows={8}
          columns={[{ width: 'w-full' }, { width: 'w-full' }]}
        />
      </div>
    );
  }

  if (query.isError) {
    const problem = esApiError(query.error) ? query.error.problem : undefined;
    return <ErrorState problem={problem} onRetry={() => query.refetch()} />;
  }

  const total = query.data?.total ?? 0;
  if (total === 0) {
    return (
      <EmptyState
        icon={<Inbox className="h-10 w-10" />}
        title="No hay requisiciones pendientes."
        description="Cuando un capturador transmita una RQ, aparecerá aquí."
      />
    );
  }

  if (items.length === 0 && search.q) {
    return (
      <EmptyState
        title={`Ningún folio coincide con "${search.q}".`}
        description="Cambia o limpia el filtro de búsqueda del topbar."
      />
    );
  }

  return (
    <ListaPendientesCompacta
      items={items}
      idActivo={idActivo}
      search={search}
    />
  );
}

function PlaceholderSinSeleccion() {
  return (
    <div className="flex h-full min-h-64 items-center justify-center rounded-md border border-dashed bg-muted/20 p-8 text-center">
      <div className="space-y-1 text-muted-foreground">
        <Inbox className="mx-auto h-8 w-8 opacity-50" aria-hidden="true" />
        <p className="text-sm">Selecciona una requisición pendiente</p>
        <p className="text-xs">para autorizar, rechazar o revisar.</p>
      </div>
    </div>
  );
}
