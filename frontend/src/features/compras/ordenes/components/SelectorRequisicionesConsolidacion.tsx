import { useMemo, useState } from 'react';
import { Inbox, Search } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
  DateTimeDisplay,
} from '@/components/erp';
import {
  useDepartamentos,
  useUsuarios,
  mapById,
} from '@/features/catalogos/api';
import { useRequisicionesDisponibles } from '@/features/compras/ordenes/api/useRequisicionesDisponibles';
import type { RequisicionDisponible } from '@/features/compras/ordenes/api/useRequisicionesDisponibles';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

/**
 * <c>P5 — Selector de RQs para consolidación N:1</c> (FOC4 cerrado,
 * doc 05 §10.5). Modal multi-select que el Sheet "Nueva OC" abre
 * cuando el comprador clickea "Agregar requisiciones (consolidación)".
 *
 * <para><b>Restricción de sucursal cerrada §10.5</b>: el selector
 * exige <c>sucursalId</c> y no muestra RQs de otras sucursales — la
 * UI no permite bypass. Si el comprador cambia la sucursal en el
 * Sheet después de seleccionar RQs, el caller (Sheet) DEBE limpiar
 * la selección para evitar consolidar mezcla.</para>
 *
 * <para><b>Filtros</b>: search por folio (substring case-insensitive)
 * client-side sobre las RQs ya cargadas. El backend solo acepta
 * <c>sucursalId</c> como server-side filter; departamento /
 * requisitante / fecha se podrían agregar al endpoint en un
 * follow-up cuando emerja la necesidad de paginar listas grandes.</para>
 *
 * <para><b>Submit</b>: <c>onConfirm(rqs)</c> con la lista completa
 * de RQs seleccionadas (no solo IDs — el caller las usa para mostrar
 * chips e iterar el agregar-líneas). El modal NO ejecuta mutaciones
 * — solo selección. La orquestación de "crear OC vacía + iterar
 * agregar líneas desde RQ" vive en el Sheet (UF2-PR1).</para>
 */
export interface SelectorRequisicionesConsolidacionProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Sucursal de destino seleccionada en el Sheet. El selector
   * filtra RQs de esa sucursal exclusivamente (invariante §10.5). */
  sucursalId: string | null;
  /** Pre-selección actual (chips ya en el Sheet). El modal arranca
   * con estos IDs marcados para permitir editar la lista sin perder
   * progreso. */
  rqsPreviamenteSeleccionadas: readonly RequisicionDisponible[];
  /** Submit: el caller recibe la lista completa de RQs marcadas. */
  onConfirm: (rqs: readonly RequisicionDisponible[]) => void;
}

export function SelectorRequisicionesConsolidacion({
  open,
  onOpenChange,
  sucursalId,
  rqsPreviamenteSeleccionadas,
  onConfirm,
}: SelectorRequisicionesConsolidacionProps) {
  // Selección local del modal — arranca igual a las pre-seleccionadas.
  // Se reinicia cuando el modal abre/cierra para evitar fantasmas.
  const [seleccionados, setSeleccionados] = useState<Set<string>>(
    () => new Set(rqsPreviamenteSeleccionadas.map((r) => r.id)),
  );
  const [busqueda, setBusqueda] = useState('');

  // Re-sync cuando el modal se reabre (props cambian).
  const [openTrack, setOpenTrack] = useState(open);
  if (open !== openTrack) {
    setOpenTrack(open);
    if (open) {
      setSeleccionados(new Set(rqsPreviamenteSeleccionadas.map((r) => r.id)));
      setBusqueda('');
    }
  }

  const query = useRequisicionesDisponibles(sucursalId);
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

  const itemsFiltrados = useMemo(() => {
    const items = query.data ?? [];
    if (busqueda.trim().length === 0) return items;
    const needle = busqueda.toLowerCase();
    return items.filter((r) => r.folio.toLowerCase().includes(needle));
  }, [query.data, busqueda]);

  function toggleRq(id: string) {
    setSeleccionados((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function toggleAll() {
    setSeleccionados((prev) => {
      const todosVisiblesYaMarcados = itemsFiltrados.every((r) =>
        prev.has(r.id),
      );
      if (todosVisiblesYaMarcados) {
        const next = new Set(prev);
        for (const r of itemsFiltrados) next.delete(r.id);
        return next;
      }
      const next = new Set(prev);
      for (const r of itemsFiltrados) next.add(r.id);
      return next;
    });
  }

  function handleConfirmar() {
    const items = query.data ?? [];
    const seleccionadasFull = items.filter((r) => seleccionados.has(r.id));
    onConfirm(seleccionadasFull);
    onOpenChange(false);
  }

  const todosVisiblesYaMarcados =
    itemsFiltrados.length > 0 &&
    itemsFiltrados.every((r) => seleccionados.has(r.id));

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        className="max-h-[90vh] max-w-3xl overflow-hidden p-0"
        data-component="selector-rqs-consolidacion"
      >
        <DialogHeader className="border-b px-6 py-4">
          <DialogTitle>Seleccionar RQs para consolidar</DialogTitle>
          <DialogDescription>
            Solo aparecen RQs <strong>autorizadas y no comprometidas</strong>{' '}
            de la sucursal de destino seleccionada. Restricción §10.5: una OC
            no puede consolidar RQs de sucursales distintas.
          </DialogDescription>
        </DialogHeader>

        {/* Filtros */}
        <div className="flex items-center gap-2 border-b px-6 py-3">
          <div className="relative flex-1">
            <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
            <Input
              type="search"
              placeholder="Buscar por folio…"
              className="pl-9"
              value={busqueda}
              onChange={(e) => setBusqueda(e.target.value)}
              aria-label="Buscar RQs por folio"
            />
          </div>
          <span className="text-xs text-muted-foreground">
            {seleccionados.size} de {query.data?.length ?? 0} seleccionadas
          </span>
        </div>

        {/* Tabla / estados */}
        <div className="max-h-96 overflow-auto" data-region="lista">
          <RenderLista
            query={query}
            items={itemsFiltrados}
            seleccionados={seleccionados}
            sucursalId={sucursalId}
            todosVisiblesYaMarcados={todosVisiblesYaMarcados}
            onToggleRq={toggleRq}
            onToggleAll={toggleAll}
            deptosMap={deptosMap}
            usuariosMap={usuariosMap}
            busqueda={busqueda}
          />
        </div>

        <DialogFooter className="border-t px-6 py-3">
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancelar
          </Button>
          <Button
            onClick={handleConfirmar}
            disabled={seleccionados.size === 0}
            data-action="confirmar-seleccion"
          >
            Agregar {seleccionados.size}{' '}
            {seleccionados.size === 1 ? 'RQ' : 'RQs'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ============================================================================
// Subcomponente render — separado para mantener el componente principal
// limpio. Maneja estados loading/error/empty/data y la tabla con expand.
// ============================================================================

interface RenderListaProps {
  query: ReturnType<typeof useRequisicionesDisponibles>;
  items: readonly RequisicionDisponible[];
  seleccionados: Set<string>;
  sucursalId: string | null;
  todosVisiblesYaMarcados: boolean;
  onToggleRq: (id: string) => void;
  onToggleAll: () => void;
  deptosMap: Map<string, { nombre: string }>;
  usuariosMap: Map<string, { nombre: string }>;
  busqueda: string;
}

function RenderLista({
  query,
  items,
  seleccionados,
  sucursalId,
  todosVisiblesYaMarcados,
  onToggleRq,
  onToggleAll,
  deptosMap,
  usuariosMap,
  busqueda,
}: RenderListaProps) {
  if (sucursalId == null || sucursalId.length === 0) {
    return (
      <EmptyState
        icon={<Inbox className="h-10 w-10" />}
        title="Selecciona la sucursal de destino antes de consolidar."
        description="La sucursal define qué RQs son elegibles. Cierra este modal, escoge sucursal en el Sheet y vuelve."
      />
    );
  }

  if (query.isLoading) {
    return (
      <div className="p-3">
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-6' },
            { width: 'w-28' },
            { width: 'w-24' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-12' },
          ]}
        />
      </div>
    );
  }

  if (query.isError) {
    const problem = esApiError(query.error) ? query.error.problem : undefined;
    return <ErrorState problem={problem} onRetry={() => query.refetch()} />;
  }

  if ((query.data?.length ?? 0) === 0) {
    return (
      <EmptyState
        icon={<Inbox className="h-10 w-10" />}
        title="No hay RQs autorizadas disponibles para esta sucursal."
        description="Las RQs deben estar Autorizadas y NO comprometidas en otra OC. Verifica el flujo de aprobación o consulta con el equipo de RQ."
      />
    );
  }

  if (items.length === 0 && busqueda.length > 0) {
    return (
      <EmptyState
        title={`Ningún folio coincide con "${busqueda}".`}
        description="Limpia o cambia el filtro de búsqueda."
      />
    );
  }

  return (
    <table className="w-full text-sm">
      <thead className="sticky top-0 bg-muted/80 backdrop-blur text-xs uppercase tracking-wide text-muted-foreground">
        <tr>
          <th className="px-3 py-2 text-left">
            <input
              type="checkbox"
              checked={todosVisiblesYaMarcados}
              onChange={onToggleAll}
              aria-label="Seleccionar todos los RQs visibles"
              className="h-4 w-4 rounded border-input"
              data-action="toggle-all"
            />
          </th>
          <th className="px-3 py-2 text-left font-medium">Folio</th>
          <th className="px-3 py-2 text-left font-medium">Fecha</th>
          <th className="px-3 py-2 text-left font-medium">Departamento</th>
          <th className="px-3 py-2 text-left font-medium">Requisitante</th>
          <th className="px-3 py-2 text-right font-medium">Líneas</th>
        </tr>
      </thead>
      <tbody>
        {items.map((rq, i) => {
          const checked = seleccionados.has(rq.id);
          const depto = deptosMap.get(rq.departamentoId);
          const requisitante = usuariosMap.get(rq.requisitanteId);
          return (
            <tr
              key={rq.id}
              className={cn(
                i % 2 === 1 ? 'bg-muted/20' : undefined,
                checked && 'bg-primary/5',
              )}
              data-rq={rq.id}
              data-checked={checked || undefined}
            >
              <td className="px-3 py-2">
                <input
                  type="checkbox"
                  checked={checked}
                  onChange={() => onToggleRq(rq.id)}
                  aria-label={`Seleccionar ${rq.folio}`}
                  className="h-4 w-4 rounded border-input"
                  data-action="toggle-rq"
                />
              </td>
              <td className="px-3 py-2 font-mono">{rq.folio}</td>
              <td className="px-3 py-2">
                <DateTimeDisplay value={rq.fechaSolicitud} />
              </td>
              <td className="px-3 py-2 truncate">
                {depto?.nombre ?? rq.departamentoId}
              </td>
              <td className="px-3 py-2 truncate">
                {requisitante?.nombre ?? rq.requisitanteId}
              </td>
              <td className="px-3 py-2 text-right tabular-nums">
                {rq.totalLineas}
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
