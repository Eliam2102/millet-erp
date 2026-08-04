import { useMemo, useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Plus, Trash2 } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  DateTimeDisplay,
  DepartamentoSelector,
  SortableHeader,
  compareItemsBy,
  type SortState,
  EmptyState,
  ErrorState,
  TableSkeleton,
  UsuarioSelector,
} from '@/components/erp';
import {
  useAprobadoresHistorico,
  useAprobadoresVigentes,
  useRevocarAprobador,
} from '@/features/compras/api/useAprobadores';
import {
  useDepartamentos,
  useUsuarios,
  mapById,
} from '@/features/catalogos/api';
import {
  ROL_APROBADOR_LABEL,
  RolAprobador,
  type AprobadorHistoricoResponse,
  type AprobadorVigenteResponse,
} from '@/features/compras/api/types';
import { DesignarAprobadorDialog } from '@/features/compras/components/DesignarAprobadorDialog';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import {
  DEFAULT_ADMIN_APROBADORES_SEARCH,
  type AdminAprobadoresSearch,
} from '@/features/compras/lib/admin-aprobadores-search-schema';
import { cn } from '@/lib/utils';

const FROM = '/_app/compras/admin/aprobadores' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>P9 — Admin de aprobadores</c> (doc 05 §11.4). Tabs Vigentes /
 * Histórico, designar (modal), revocar (confirm). Gateado a nivel
 * ruta por <c>compras.aprobadores.administrar</c>.
 *
 * <para>El histórico exige al menos un filtro (depto, rol o usuario);
 * el form lo valida antes de pegarle al backend (que también lo
 * defiende con 422 <c>FILTRO_OBLIGATORIO</c>).</para>
 */
export function AdminAprobadores() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();

  const departamentosQuery = useDepartamentos();
  const usuariosQuery = useUsuarios();

  const deptosMap = useMemo(
    () => mapById(departamentosQuery.data?.items),
    [departamentosQuery.data],
  );
  const usuariosMap = useMemo(
    () => mapById(usuariosQuery.data?.items),
    [usuariosQuery.data],
  );

  const [modalDesignarAbierto, setModalDesignarAbierto] = useState(false);
  const [aRevocar, setARevocar] =
    useState<AprobadorVigenteResponse | null>(null);

  function actualizarSearch(parcial: Partial<AdminAprobadoresSearch>) {
    navigate({
      to: '/compras/admin/aprobadores',
      search: { ...search, ...parcial },
    });
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">
          Aprobadores
        </h1>
        <Button onClick={() => setModalDesignarAbierto(true)}>
          <Plus className="mr-2 h-4 w-4" />
          Designar aprobador
        </Button>
      </div>

      {/* Tabs */}
      <div
        role="tablist"
        aria-label="Vigentes o histórico"
        className="flex gap-1 border-b"
      >
        <TabButton
          activo={search.tab === 'vigentes'}
          onClick={() => actualizarSearch({ tab: 'vigentes' })}
        >
          Vigentes
        </TabButton>
        <TabButton
          activo={search.tab === 'historico'}
          onClick={() => actualizarSearch({ tab: 'historico' })}
        >
          Histórico
        </TabButton>
      </div>

      {/* Filtros (compartidos por ambas tabs) */}
      <FiltrosToolbar search={search} onChange={actualizarSearch} />

      {search.tab === 'vigentes' ? (
        <PanelVigentes
          search={search}
          deptosMap={deptosMap}
          usuariosMap={usuariosMap}
          onRevocar={(a) => setARevocar(a)}
        />
      ) : (
        <PanelHistorico
          search={search}
          deptosMap={deptosMap}
          usuariosMap={usuariosMap}
        />
      )}

      <DesignarAprobadorDialog
        open={modalDesignarAbierto}
        onOpenChange={setModalDesignarAbierto}
      />

      <RevocarConfirm
        aprobador={aRevocar}
        deptosMap={deptosMap}
        usuariosMap={usuariosMap}
        onClose={() => setARevocar(null)}
      />
    </div>
  );
}

// ─── Tabs ────────────────────────────────────────────────────────

function TabButton({
  activo,
  onClick,
  children,
}: {
  activo: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={activo}
      onClick={onClick}
      className={cn(
        '-mb-px border-b-2 px-4 py-2 text-sm font-medium transition-colors',
        activo
          ? 'border-primary text-foreground'
          : 'border-transparent text-muted-foreground hover:text-foreground',
      )}
    >
      {children}
    </button>
  );
}

// ─── Filtros ──────────────────────────────────────────────────────

interface FiltrosToolbarProps {
  search: AdminAprobadoresSearch;
  onChange: (parcial: Partial<AdminAprobadoresSearch>) => void;
}

function FiltrosToolbar({ search, onChange }: FiltrosToolbarProps) {
  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Departamento</label>
        <DepartamentoSelector
          value={search.departamentoId ?? null}
          onChange={(id) =>
            onChange({ departamentoId: id ?? undefined })
          }
        />
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Rol</label>
        <Select
          value={search.rol != null ? String(search.rol) : SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({
              rol: v === SENTINEL_ALL ? undefined : Number(v),
            })
          }
        >
          <SelectTrigger className="w-56">
            <SelectValue placeholder="Todos los roles" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos los roles</SelectItem>
            {(
              [
                RolAprobador.JefeDpto,
                RolAprobador.JefeAlmacen,
                RolAprobador.AutorizadorN2,
              ] as const
            ).map((r) => (
              <SelectItem key={r} value={String(r)}>
                {ROL_APROBADOR_LABEL[r]}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Usuario</label>
        <UsuarioSelector
          value={search.usuarioId ?? null}
          onChange={(id) => onChange({ usuarioId: id ?? undefined })}
        />
      </div>
      {(search.departamentoId || search.rol != null || search.usuarioId) && (
        <Button
          variant="ghost"
          onClick={() =>
            onChange({
              departamentoId: undefined,
              rol: undefined,
              usuarioId: undefined,
            })
          }
        >
          Limpiar filtros
        </Button>
      )}
    </div>
  );
}

// ─── Panel Vigentes ───────────────────────────────────────────────

interface PanelVigentesProps {
  search: AdminAprobadoresSearch;
  deptosMap: Map<string, { id: string; nombre: string }>;
  usuariosMap: Map<string, { id: string; nombre: string }>;
  onRevocar: (a: AprobadorVigenteResponse) => void;
}

function PanelVigentes({
  search,
  deptosMap,
  usuariosMap,
  onRevocar,
}: PanelVigentesProps) {
  const query = useAprobadoresVigentes({
    departamentoId: search.departamentoId,
    rol: search.rol,
    usuarioId: search.usuarioId,
  });

  if (query.isError) {
    return (
      <ErrorState
        title="No se pudieron cargar los aprobadores"
        problem={esApiError(query.error) ? query.error.problem : undefined}
        onRetry={() => query.refetch()}
      />
    );
  }

  if (query.isLoading) {
    return (
      <TableSkeleton
        rows={5}
        columns={[
          { width: 'w-48' },
          { width: 'w-40' },
          { width: 'w-48' },
          { width: 'w-32' },
          { width: 'w-24' },
        ]}
      />
    );
  }

  const items = query.data ?? [];

  if (items.length === 0) {
    return (
      <EmptyState
        title="Sin aprobadores vigentes"
        description="No hay designaciones que coincidan con los filtros. Usa “Designar aprobador” para abrir una vigencia."
      />
    );
  }

  return (
    <PanelVigentesTabla
      items={items}
      deptosMap={deptosMap}
      usuariosMap={usuariosMap}
      onRevocar={onRevocar}
    />
  );
}

interface PanelVigentesTablaProps {
  items: readonly AprobadorVigenteResponse[];
  deptosMap: Map<string, { id: string; nombre: string }>;
  usuariosMap: Map<string, { id: string; nombre: string }>;
  onRevocar: (a: AprobadorVigenteResponse) => void;
}

type VigenteSortKey = 'depto' | 'rol' | 'usuario' | 'vigenteDesde';

function PanelVigentesTabla({
  items,
  deptosMap,
  usuariosMap,
  onRevocar,
}: PanelVigentesTablaProps) {
  const [sort, setSort] = useState<SortState<VigenteSortKey> | null>(null);

  // Enriquecemos con labels resueltos para sortear por NOMBRE de
  // depto/usuario en lugar del id (UUID — orden lexicográfico
  // sin sentido para el usuario).
  const itemsEnriquecidos = useMemo(
    () =>
      items.map((a) => ({
        ...a,
        depto: deptosMap.get(a.departamentoId)?.nombre ?? a.departamentoId,
        usuario: usuariosMap.get(a.usuarioId)?.nombre ?? a.usuarioId,
      })),
    [items, deptosMap, usuariosMap],
  );

  const itemsOrdenados = useMemo(() => {
    if (sort == null) return itemsEnriquecidos;
    return [...itemsEnriquecidos].sort((a, b) =>
      compareItemsBy(a, b, sort.key, sort.dir),
    );
  }, [itemsEnriquecidos, sort]);

  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-sm">
        <thead className="bg-muted/50">
          <tr>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="depto"
                label="Departamento"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="rol"
                label="Rol"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="usuario"
                label="Usuario"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="vigenteDesde"
                label="Vigente desde"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-right font-medium">Acciones</th>
          </tr>
        </thead>
        <tbody>
          {itemsOrdenados.map((a) => (
            <tr key={a.id} className="border-t">
              <td className="px-3 py-2">{a.depto}</td>
              <td className="px-3 py-2">{ROL_APROBADOR_LABEL[a.rol]}</td>
              <td className="px-3 py-2">{a.usuario}</td>
              <td className="px-3 py-2">
                <DateTimeDisplay value={a.vigenteDesde} />
              </td>
              <td className="px-3 py-2 text-right">
                <Button
                  type="button"
                  size="sm"
                  variant="ghost"
                  onClick={() =>
                    onRevocar({
                      id: a.id,
                      departamentoId: a.departamentoId,
                      rol: a.rol,
                      usuarioId: a.usuarioId,
                      vigenteDesde: a.vigenteDesde,
                      designadoPor: a.designadoPor,
                      motivo: a.motivo,
                    })
                  }
                  className="text-rose-700 hover:bg-rose-50 hover:text-rose-800"
                  aria-label={`Revocar aprobador ${a.id}`}
                >
                  <Trash2 className="h-4 w-4" />
                </Button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ─── Panel Histórico ─────────────────────────────────────────────

interface PanelHistoricoProps {
  search: AdminAprobadoresSearch;
  deptosMap: Map<string, { id: string; nombre: string }>;
  usuariosMap: Map<string, { id: string; nombre: string }>;
}

function PanelHistorico({
  search,
  deptosMap,
  usuariosMap,
}: PanelHistoricoProps) {
  const tieneAlgunFiltro =
    search.departamentoId != null ||
    search.rol != null ||
    search.usuarioId != null;

  const query = useAprobadoresHistorico({
    departamentoId: search.departamentoId,
    rol: search.rol,
    usuarioId: search.usuarioId,
  });

  if (!tieneAlgunFiltro) {
    return (
      <EmptyState
        title="Selecciona al menos un filtro"
        description="El histórico requiere filtrar por departamento, rol o usuario para no devolver toda la matriz histórica."
      />
    );
  }

  if (query.isError) {
    return (
      <ErrorState
        title="No se pudo cargar el histórico"
        problem={esApiError(query.error) ? query.error.problem : undefined}
        onRetry={() => query.refetch()}
      />
    );
  }

  if (query.isLoading) {
    return (
      <TableSkeleton
        rows={5}
        columns={[
          { width: 'w-48' },
          { width: 'w-40' },
          { width: 'w-48' },
          { width: 'w-32' },
          { width: 'w-32' },
        ]}
      />
    );
  }

  const items = query.data ?? [];

  if (items.length === 0) {
    return (
      <EmptyState
        title="Sin registros históricos"
        description="No hay designaciones (vigentes o cerradas) que coincidan con los filtros."
      />
    );
  }

  return (
    <PanelHistoricoTabla
      items={items}
      deptosMap={deptosMap}
      usuariosMap={usuariosMap}
    />
  );
}

interface PanelHistoricoTablaProps {
  items: readonly AprobadorHistoricoResponse[];
  deptosMap: Map<string, { id: string; nombre: string }>;
  usuariosMap: Map<string, { id: string; nombre: string }>;
}

type HistoricoSortKey =
  | 'depto'
  | 'rol'
  | 'usuario'
  | 'vigenteDesde'
  | 'vigenteHasta';

function PanelHistoricoTabla({
  items,
  deptosMap,
  usuariosMap,
}: PanelHistoricoTablaProps) {
  const [sort, setSort] = useState<SortState<HistoricoSortKey> | null>(null);

  const itemsEnriquecidos = useMemo(
    () =>
      items.map((a) => ({
        ...a,
        depto: deptosMap.get(a.departamentoId)?.nombre ?? a.departamentoId,
        usuario: usuariosMap.get(a.usuarioId)?.nombre ?? a.usuarioId,
      })),
    [items, deptosMap, usuariosMap],
  );

  const itemsOrdenados = useMemo(() => {
    if (sort == null) return itemsEnriquecidos;
    return [...itemsEnriquecidos].sort((a, b) =>
      compareItemsBy(a, b, sort.key, sort.dir),
    );
  }, [itemsEnriquecidos, sort]);

  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-sm">
        <thead className="bg-muted/50">
          <tr>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="depto"
                label="Departamento"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="rol"
                label="Rol"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="usuario"
                label="Usuario"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="vigenteDesde"
                label="Vigente desde"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="vigenteHasta"
                label="Cerró"
                current={sort}
                onSortChange={setSort}
              />
            </th>
          </tr>
        </thead>
        <tbody>
          {itemsOrdenados.map((a) => {
            const vigente = a.vigenteHasta == null;
            return (
              <tr
                key={a.id}
                className={cn('border-t', vigente && 'bg-emerald-50/50')}
              >
                <td className="px-3 py-2">{a.depto}</td>
                <td className="px-3 py-2">{ROL_APROBADOR_LABEL[a.rol]}</td>
                <td className="px-3 py-2">{a.usuario}</td>
                <td className="px-3 py-2">
                  <DateTimeDisplay value={a.vigenteDesde} />
                </td>
                <td className="px-3 py-2">
                  {vigente ? (
                    <span className="rounded bg-emerald-100 px-1.5 py-0.5 text-xs text-emerald-800">
                      Vigente
                    </span>
                  ) : (
                    <DateTimeDisplay value={a.vigenteHasta} />
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

// ─── Confirm de revocar ──────────────────────────────────────────

function RevocarConfirm({
  aprobador,
  deptosMap,
  usuariosMap,
  onClose,
}: {
  aprobador: AprobadorVigenteResponse | null;
  deptosMap: Map<string, { id: string; nombre: string }>;
  usuariosMap: Map<string, { id: string; nombre: string }>;
  onClose: () => void;
}) {
  const idempotencyKey = useFormIdempotencyKey();
  const revocar = useRevocarAprobador();

  function ejecutar() {
    if (aprobador == null) return;
    revocar.mutate(
      { aprobadorId: aprobador.id, idempotencyKey },
      {
        onSuccess: () => {
          toast.success('Aprobador revocado');
          onClose();
        },
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: error.traceId
                ? `Código: ${error.traceId}`
                : undefined,
            });
          } else {
            toast.error('Error inesperado al revocar.');
          }
        },
      },
    );
  }

  const dept =
    aprobador != null
      ? (deptosMap.get(aprobador.departamentoId)?.nombre ?? aprobador.departamentoId)
      : '';
  const user =
    aprobador != null
      ? (usuariosMap.get(aprobador.usuarioId)?.nombre ?? aprobador.usuarioId)
      : '';

  return (
    <AlertDialog
      open={aprobador != null}
      onOpenChange={(open) => {
        if (!open && !revocar.isPending) onClose();
      }}
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>¿Revocar aprobador?</AlertDialogTitle>
          <AlertDialogDescription>
            Cierra la vigencia actual de <b>{user}</b> como{' '}
            <b>{aprobador != null ? ROL_APROBADOR_LABEL[aprobador.rol] : ''}</b>{' '}
            en <b>{dept}</b>. El histórico queda intacto; el row solo deja de
            estar vigente.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={revocar.isPending}>
            Cancelar
          </AlertDialogCancel>
          <AlertDialogAction disabled={revocar.isPending} onClick={ejecutar}>
            {revocar.isPending ? 'Revocando…' : 'Revocar'}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

// Exporta para acceso desde tests si hace falta.
export { DEFAULT_ADMIN_APROBADORES_SEARCH };
