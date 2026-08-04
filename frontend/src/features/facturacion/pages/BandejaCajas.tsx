import { useMemo, useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Plus, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { UsuarioSelector } from '@/components/erp/selectors/UsuarioSelector';
import { CanalVentaSelector } from '@/features/facturacion/components/selectors/CanalVentaSelector';
import {
  useListarCajas,
  useReemplazarUsuarioAlcances,
  useUsuarioAlcances,
} from '@/features/facturacion/api/useCajas';
import { useSucursales, useUsuarios } from '@/features/catalogos/api/hooks';
import { useCanalesVenta } from '@/features/facturacion/api/useCatalogosFacturacion';
import { useNuevaCaja } from '@/features/facturacion/components/nueva-caja-context';
import { ChipEstatusCaja } from '@/features/facturacion/components/ListaCajasCompacta';
import { resumenAlcance } from '@/features/facturacion/lib/cajas-format';
import type { UsuarioAlcanceInput } from '@/features/facturacion/api/types';
import type { CajasSearch } from '@/features/facturacion/lib/cajas-search-schema';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

const FROM = '/_app/facturacion/cajas/' as const;

/**
 * <c>Bandeja de cajas</c> (CAJAS-PR5, P1) con dos pestañas: cajas del
 * módulo y concesiones de alcance por usuario (`[Decisión 12-6]` — la
 * misma UI administra ambas). El CRUD vive en Facturación, no en /admin
 * (excepción declarada al ADR-0034, 12-cajas.md §8).
 */
export function BandejaCajas() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const tab = search.tab ?? 'cajas';

  function actualizarSearch(parcial: Partial<CajasSearch>) {
    navigate({ to: '/facturacion/cajas', search: { ...search, ...parcial } });
  }

  return (
    <div className="space-y-4 px-4 py-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Cajas</h1>
          <p className="text-sm text-muted-foreground">
            Alcance de datos por sucursal × canal y cajeros con acceso (12-cajas.md).
          </p>
        </div>
      </div>

      <div className="flex gap-1 border-b" role="tablist" aria-label="Secciones de cajas">
        <BotonTab
          activo={tab === 'cajas'}
          onClick={() => actualizarSearch({ tab: undefined })}
        >
          Cajas
        </BotonTab>
        <BotonTab
          activo={tab === 'alcances'}
          onClick={() => actualizarSearch({ tab: 'alcances' })}
        >
          Alcances por usuario
        </BotonTab>
      </div>

      {tab === 'cajas' ? (
        <TabCajas search={search} onSearch={actualizarSearch} />
      ) : (
        <TabUsuarioAlcances />
      )}
    </div>
  );
}

function BotonTab(props: { activo: boolean; onClick: () => void; children: string }) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={props.activo}
      onClick={props.onClick}
      className={cn(
        'border-b-2 px-3 py-2 text-sm font-medium transition-colors',
        props.activo
          ? 'border-primary text-foreground'
          : 'border-transparent text-muted-foreground hover:text-foreground',
      )}
    >
      {props.children}
    </button>
  );
}

// ─── Pestaña 1: cajas ────────────────────────────────────────────────

function TabCajas(props: {
  search: CajasSearch;
  onSearch: (parcial: Partial<CajasSearch>) => void;
}) {
  const nuevaCaja = useNuevaCaja();
  const query = useListarCajas(props.search.soloActivas ?? false);

  const q = (props.search.q ?? '').trim().toLowerCase();
  const items = (query.data ?? []).filter(
    (c) =>
      q.length === 0 ||
      c.nombre.toLowerCase().includes(q) ||
      (c.descripcion ?? '').toLowerCase().includes(q),
  );

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <label className="flex items-center gap-2 text-sm">
          <Checkbox
            checked={props.search.soloActivas ?? false}
            onCheckedChange={(v) =>
              props.onSearch({ soloActivas: v === true ? true : undefined })
            }
          />
          Solo activas
        </label>
        <Button onClick={() => nuevaCaja.abrir()}>
          <Plus className="mr-2 h-4 w-4" />
          Nueva caja
        </Button>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las cajas"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={5}
          columns={[{ width: 'w-40' }, { width: 'w-64' }, { width: 'w-32' }, { width: 'w-16' }]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          title="Sin cajas"
          description='Crea la primera con "Nueva caja"; nace activa y sin alcance (todas las sucursales y canales).'
        />
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">Nombre</th>
                <th className="px-3 py-2 text-left">Alcance</th>
                <th className="px-3 py-2 text-left">Estatus</th>
                <th className="px-3 py-2 text-right">Acción</th>
              </tr>
            </thead>
            <tbody>
              {items.map((c) => (
                <tr key={c.id} className="border-t hover:bg-muted/30">
                  <td className="px-3 py-2">
                    <div className="font-medium">{c.nombre}</div>
                    {c.descripcion && (
                      <div className="text-xs text-muted-foreground">{c.descripcion}</div>
                    )}
                  </td>
                  <td className="px-3 py-2 text-xs text-muted-foreground">
                    {resumenAlcance(c)}
                  </td>
                  <td className="px-3 py-2">
                    <ChipEstatusCaja estatus={c.estatus} />
                  </td>
                  <td className="px-3 py-2 text-right">
                    <Link
                      to="/facturacion/cajas/$id"
                      params={{ id: c.id }}
                      className="text-sm font-medium text-primary hover:underline"
                    >
                      Ver
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

// ─── Pestaña 2: concesiones por usuario ([Decisión 12-6]) ────────────

function TabUsuarioAlcances() {
  const [usuarioId, setUsuarioId] = useState<string | null>(null);
  return (
    <div className="space-y-4">
      <p className="text-sm text-muted-foreground">
        Concesiones para perfiles administrativos <b>sin caja</b>: cada fila otorga una
        combinación sucursal/canal (vacío = comodín). Con el permiso{' '}
        <code className="text-xs">facturacion.caja.leer-todas</code> el alcance es total y
        esta tabla no aplica.
      </p>
      <div className="w-80">
        <UsuarioSelector value={usuarioId} onChange={setUsuarioId} placeholder="Usuario…" />
      </div>
      {usuarioId != null && <EditorUsuarioAlcances key={usuarioId} usuarioId={usuarioId} />}
    </div>
  );
}

function EditorUsuarioAlcances(props: { usuarioId: string }) {
  const query = useUsuarioAlcances(props.usuarioId);
  const reemplazar = useReemplazarUsuarioAlcances();
  const sucursales = useSucursales();
  const canales = useCanalesVenta();
  const usuarios = useUsuarios();

  const [pendientes, setPendientes] = useState<UsuarioAlcanceInput[] | null>(null);
  const [nuevaSucursal, setNuevaSucursal] = useState<string | null>(null);
  const [nuevoCanal, setNuevoCanal] = useState<number | null>(null);

  const originales = useMemo<UsuarioAlcanceInput[]>(
    () =>
      (query.data ?? []).map((a) => ({
        sucursalId: a.sucursalId,
        canalVentaId: a.canalVentaId,
      })),
    [query.data],
  );
  const alcances = pendientes ?? originales;
  const dirty = pendientes != null;

  const nombreSucursal = (id: string | null) =>
    id == null
      ? 'Todas las sucursales'
      : (sucursales.data?.items.find((s) => s.id === id)?.nombre ?? id);
  const nombreCanal = (id: number | null) =>
    id == null
      ? 'Todos los canales'
      : (canales.data?.find((c) => c.id === id)?.nombre ?? `Canal ${id}`);
  const nombreUsuario =
    usuarios.data?.items.find((u) => u.id === props.usuarioId)?.nombre ?? props.usuarioId;

  if (query.isLoading) {
    return <TableSkeleton rows={3} columns={[{ width: 'w-full' }]} />;
  }
  if (query.isError) {
    return (
      <ErrorState
        title="No se pudieron cargar los alcances"
        problem={esApiError(query.error) ? query.error.problem : undefined}
        onRetry={() => query.refetch()}
      />
    );
  }

  function agregar() {
    if (nuevaSucursal == null && nuevoCanal == null) {
      toast.error('Una concesión sin sucursal ni canal equivale a alcance total; usa el permiso leer-todas.');
      return;
    }
    const nueva: UsuarioAlcanceInput = { sucursalId: nuevaSucursal, canalVentaId: nuevoCanal };
    const duplicada = alcances.some(
      (a) => a.sucursalId === nueva.sucursalId && a.canalVentaId === nueva.canalVentaId,
    );
    if (!duplicada) setPendientes([...alcances, nueva]);
    setNuevaSucursal(null);
    setNuevoCanal(null);
  }

  return (
    <div
      className={cn(
        'space-y-3 rounded-md border bg-card p-4',
        dirty && 'border-dashed border-primary/40 bg-primary/5',
      )}
    >
      <div className="flex items-center justify-between gap-2">
        <h3 className="font-medium">Concesiones de {nombreUsuario}</h3>
        {dirty && (
          <div className="flex gap-2">
            <Button
              size="sm"
              disabled={reemplazar.isPending}
              onClick={() =>
                reemplazar.mutate(
                  { usuarioId: props.usuarioId, alcances },
                  {
                    onSuccess: () => {
                      setPendientes(null);
                      toast.success('Alcances actualizados.');
                    },
                    onError: (error) =>
                      toast.error(
                        esApiError(error)
                          ? error.problem.title
                          : 'No se pudieron actualizar los alcances.',
                      ),
                  },
                )
              }
            >
              Guardar
            </Button>
            <Button size="sm" variant="ghost" onClick={() => setPendientes(null)}>
              Cancelar
            </Button>
          </div>
        )}
      </div>

      {alcances.length === 0 ? (
        <p className="text-sm italic text-muted-foreground">
          Sin concesiones: el usuario solo ve documentos por sus cajas.
        </p>
      ) : (
        <ul className="divide-y rounded-md border">
          {alcances.map((a, i) => (
            <li key={`${a.sucursalId ?? '*'}-${a.canalVentaId ?? '*'}`} className="flex items-center justify-between gap-2 px-3 py-2 text-sm">
              <span>
                {nombreSucursal(a.sucursalId)} · {nombreCanal(a.canalVentaId)}
              </span>
              <button
                type="button"
                onClick={() => setPendientes(alcances.filter((_, j) => j !== i))}
                className="text-muted-foreground hover:text-destructive"
                aria-label="Quitar concesión"
              >
                <X className="h-4 w-4" />
              </button>
            </li>
          ))}
        </ul>
      )}

      {/* Alta inline (border dashed primary, §6.3). */}
      <div className="flex flex-wrap items-end gap-2 rounded-md border border-dashed border-primary/40 bg-primary/5 p-3">
        <div className="w-64 space-y-1">
          <label className="text-xs text-muted-foreground">Sucursal (vacío = todas)</label>
          <SucursalSelector value={nuevaSucursal} onChange={setNuevaSucursal} />
        </div>
        <div className="w-64 space-y-1">
          <label className="text-xs text-muted-foreground">Canal (vacío = todos)</label>
          <CanalVentaSelector value={nuevoCanal} onChange={setNuevoCanal} />
        </div>
        <Button size="sm" variant="outline" onClick={agregar}>
          <Plus className="mr-1 h-3.5 w-3.5" />
          Agregar
        </Button>
      </div>
    </div>
  );
}
