import { useMemo, useState } from 'react';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
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
  EmptyState,
  MoneyDisplay,
  DateTimeDisplay,
} from '@/components/erp';
import { CubrimientoBar } from '@/components/erp/display/CubrimientoBar';
import { LineaInlineForm } from '@/features/compras/components/LineaInlineForm';
import type {
  LineaResponse,
  RequisicionResponse,
} from '@/features/compras/api/types';
import {
  useActualizarNotasLinea,
  useEliminarLinea,
} from '@/features/compras/api/useLineas';
import {
  accionAgregarLinea,
  accionEditarLinea,
  accionEditarNotasLinea,
  accionEliminarLinea,
} from '@/features/compras/lib/acciones-disponibles';
import { useAuthStore } from '@/lib/auth/auth-store';
import { esApiError } from '@/lib/api';
import { esConflictoConcurrencia } from '@/lib/api/error';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { useQueryClient } from '@tanstack/react-query';
import { comprasKeys } from '@/features/compras/api/keys';
import { useArticulos, mapById } from '@/features/catalogos/api';

/**
 * <c>&lt;EditorLineas/&gt;</c> — tabla de líneas con acciones
 * inline gateadas por la matriz §6.1 (doc 05). Reemplaza
 * <c>&lt;ListaLineas/&gt;</c> en P3 cuando la RQ permite alguna
 * mutación de líneas; si todas las acciones están ocultas (estado
 * terminal o sin permisos), se comporta read-only.
 *
 * <para>Acciones soportadas:</para>
 * <list>
 *   <item><b>Agregar</b> botón superior — expande
 *   <c>&lt;LineaInlineForm/&gt;</c> al final de la tabla en modo
 *   agregar. Solo en <c>Borrador</c>.</item>
 *   <item><b>Editar (estructural)</b> Pencil por fila — reemplaza la
 *   fila por <c>&lt;LineaInlineForm/&gt;</c> en modo editar (border
 *   ámbar). Solo en <c>Borrador</c>.</item>
 *   <item><b>Eliminar</b> Trash por fila — confirm dialog y delete.
 *   Solo en <c>Borrador</c>.</item>
 *   <item><b>Editar notas</b> textarea inline por fila con optimistic
 *   update. Disponible en
 *   <c>Borrador</c>/<c>EnAutorizacion</c>/<c>Autorizada</c>/<c>EnSurtido</c>.
 *   En estados terminales: read-only.</item>
 * </list>
 */
export interface EditorLineasProps {
  rq: RequisicionResponse;
}

export function EditorLineas({ rq }: EditorLineasProps) {
  const permisos = useAuthStore((s) => s.permisos);

  const canAgregar = accionAgregarLinea(rq, permisos);
  const canEditarEstructural = accionEditarLinea(rq, permisos);
  const canEliminar = accionEliminarLinea(rq, permisos);
  const canEditarNotas = accionEditarNotasLinea(rq, permisos);

  // Tanto agregar como editar usan <LineaInlineForm> — sin modal
  // (design/frontend-polish). Estado:
  //   agregarAbierto = true  → form inline al final de la tabla.
  //   lineaEnEdicionId      → la fila correspondiente se reemplaza
  //                           por el form inline.
  // Solo uno de los dos modos puede estar activo a la vez (cancelan
  // el otro al abrir).
  const [agregarAbierto, setAgregarAbierto] = useState(false);
  const [lineaEnEdicionId, setLineaEnEdicionId] = useState<string | null>(null);
  const [lineaAEliminar, setLineaAEliminar] = useState<LineaResponse | null>(null);

  // Catálogo de artículos para resolver articuloId → clave/nombre
  // por fila. Limit alto + includeInactivas para no perder
  // artículos que pudieron haberse marcado inactivos después de
  // usarse en esta RQ.
  const articulosQuery = useArticulos({
    limit: 1000,
    includeInactivas: true,
  });
  const articulosMap = useMemo(
    () => mapById(articulosQuery.data?.items),
    [articulosQuery.data],
  );

  const eliminar = useEliminarLinea();
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();

  function abrirAgregar() {
    setLineaEnEdicionId(null);
    setAgregarAbierto(true);
  }

  function abrirEditar(linea: LineaResponse) {
    setAgregarAbierto(false);
    setLineaEnEdicionId(linea.id);
  }

  function cerrarEdicion() {
    setLineaEnEdicionId(null);
  }

  function confirmarEliminar() {
    if (lineaAEliminar == null) return;
    const linea = lineaAEliminar;
    eliminar.mutate(
      { requisicionId: rq.id, lineaId: linea.id },
      {
        onSuccess: () => {
          toast.success(`Línea ${linea.posicion} eliminada`);
          setLineaAEliminar(null);
        },
        onError: (error) => {
          if (esConflictoConcurrencia(error)) {
            setLineaAEliminar(null);
            conflictDialog.openSimple({
              traceId: error.traceId,
              onRefrescar: () =>
                queryClient.invalidateQueries({
                  queryKey: comprasKeys.requisicion(rq.id),
                }),
            });
            return;
          }
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: error.traceId
                ? `Código: ${error.traceId}`
                : undefined,
            });
          } else {
            toast.error('Error inesperado al eliminar la línea.');
          }
        },
      },
    );
  }

  if (rq.lineas.length === 0) {
    if (agregarAbierto) {
      return (
        <LineaInlineForm
          requisicionId={rq.id}
          onCancel={() => setAgregarAbierto(false)}
        />
      );
    }
    return (
      <EmptyState
        title={
          canAgregar.visible && canAgregar.habilitada
            ? 'Esta requisición no tiene líneas. Agrega la primera.'
            : 'Sin líneas registradas.'
        }
        description={
          canAgregar.visible && canAgregar.habilitada
            ? 'Una RQ debe tener al menos una línea para transmitirse.'
            : undefined
        }
        action={
          canAgregar.visible && canAgregar.habilitada ? (
            <Button onClick={abrirAgregar}>
              <Plus className="mr-2 h-4 w-4" />
              Agregar línea
            </Button>
          ) : undefined
        }
      />
    );
  }

  const lineas = rq.lineas
    .slice()
    .sort((a, b) => a.posicion - b.posicion);

  return (
    <>
      <div className="space-y-3">
        {canAgregar.visible && !agregarAbierto && (
          <div className="flex justify-end">
            <Button
              size="sm"
              onClick={abrirAgregar}
              disabled={!canAgregar.habilitada}
              title={canAgregar.motivoDeshabilitada}
            >
              <Plus className="mr-2 h-4 w-4" />
              Agregar línea
            </Button>
          </div>
        )}

        <div className="overflow-x-auto rounded-md border bg-card">
          <table className="w-full min-w-[960px] text-sm">
            <thead className="bg-muted/50 text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-3 py-2 text-left font-medium">#</th>
                <th className="px-3 py-2 text-left font-medium">Artículo</th>
                <th className="px-3 py-2 text-right font-medium">Cant.</th>
                <th className="px-3 py-2 text-left font-medium">UM</th>
                <th className="px-3 py-2 text-right font-medium">Precio est.</th>
                <th className="px-3 py-2 text-left font-medium">Fecha req.</th>
                <th className="px-3 py-2 text-left font-medium">Cubrimiento</th>
                <th className="px-3 py-2 text-left font-medium">Notas</th>
                {(canEditarEstructural.visible || canEliminar.visible) && (
                  <th className="px-3 py-2 text-right font-medium">Acciones</th>
                )}
              </tr>
            </thead>
            <tbody>
              {lineas.map((linea, i) => {
                if (linea.id === lineaEnEdicionId) {
                  // La fila se reemplaza por el form inline en modo
                  // editar. <td colSpan> ocupa todas las columnas de
                  // la tabla — la cabecera sigue arriba.
                  const totalColumnas =
                    8 +
                    (canEditarEstructural.visible || canEliminar.visible
                      ? 1
                      : 0);
                  return (
                    <tr key={linea.id}>
                      <td colSpan={totalColumnas} className="p-2">
                        <LineaInlineForm
                          requisicionId={rq.id}
                          linea={linea}
                          onCancel={cerrarEdicion}
                          onSaved={cerrarEdicion}
                        />
                      </td>
                    </tr>
                  );
                }
                // Preferir la etiqueta enriquecida del DTO (ADR-0042 addendum);
                // fallback al catálogo capado client-side y por último al id.
                const articulo = articulosMap.get(linea.articuloId);
                return (
                  <FilaLinea
                    key={linea.id}
                    linea={linea}
                    rqId={rq.id}
                    articuloClave={linea.articuloClave ?? articulo?.clave ?? null}
                    articuloNombre={linea.articuloNombre ?? articulo?.nombre ?? null}
                    bgClass={i % 2 === 1 ? 'bg-muted/20' : undefined}
                    canEditarEstructural={canEditarEstructural.habilitada}
                    canEliminar={canEliminar.habilitada}
                    canEditarNotas={canEditarNotas.habilitada}
                    onEditar={() => abrirEditar(linea)}
                    onEliminar={() => setLineaAEliminar(linea)}
                  />
                );
              })}
            </tbody>
          </table>
        </div>

        {/* Inline form expandible al final — agregar líneas sin modal
            (design/frontend-polish). El form se mantiene abierto entre
            submits para agregar varias en sucesión. */}
        {agregarAbierto && (
          <LineaInlineForm
            requisicionId={rq.id}
            onCancel={() => setAgregarAbierto(false)}
          />
        )}
      </div>

      <AlertDialog
        open={lineaAEliminar != null}
        onOpenChange={(open) => {
          if (!open) setLineaAEliminar(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              ¿Eliminar línea {lineaAEliminar?.posicion}?
            </AlertDialogTitle>
            <AlertDialogDescription>
              Esta acción no se puede deshacer. La línea se borra de la RQ
              en estado Borrador.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={eliminar.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={confirmarEliminar}
              disabled={eliminar.isPending}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              {eliminar.isPending ? 'Eliminando…' : 'Eliminar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}

// ─── Fila de la tabla ─────────────────────────────────────────────

interface FilaLineaProps {
  linea: LineaResponse;
  rqId: string;
  /** Clave resuelta contra el catálogo de artículos. <c>null</c> si
   * el artículo no está en el catálogo cargado. */
  articuloClave: string | null;
  /** Nombre resuelto contra el catálogo de artículos. <c>null</c> si
   * el artículo no está en el catálogo cargado. */
  articuloNombre: string | null;
  bgClass?: string;
  canEditarEstructural: boolean;
  canEliminar: boolean;
  canEditarNotas: boolean;
  onEditar: () => void;
  onEliminar: () => void;
}

function FilaLinea({
  linea,
  rqId,
  articuloClave,
  articuloNombre,
  bgClass,
  canEditarEstructural,
  canEliminar,
  canEditarNotas,
  onEditar,
  onEliminar,
}: FilaLineaProps) {
  const [notasEditando, setNotasEditando] = useState(false);
  const [notasInput, setNotasInput] = useState(linea.notas ?? '');
  const actualizarNotas = useActualizarNotasLinea();
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();

  function guardarNotas() {
    const next = notasInput.trim() === '' ? null : notasInput;
    if (next === linea.notas) {
      setNotasEditando(false);
      return;
    }
    actualizarNotas.mutate(
      { requisicionId: rqId, lineaId: linea.id, values: { notas: next } },
      {
        onSuccess: () => {
          setNotasEditando(false);
        },
        onError: (error) => {
          // Optimistic update se revierte automáticamente. Si el error
          // es 409 CONCURRENCY_CONFLICT, abrir el conflict dialog en
          // modo simple (las notas son un solo campo; preserve mode
          // sería overkill). Para otros errores: toast + revert.
          if (esConflictoConcurrencia(error)) {
            setNotasEditando(false);
            setNotasInput(linea.notas ?? '');
            conflictDialog.openSimple({
              traceId: error.traceId,
              onRefrescar: () =>
                queryClient.invalidateQueries({
                  queryKey: comprasKeys.requisicion(rqId),
                }),
            });
            return;
          }
          if (esApiError(error)) {
            toast.error(error.problem.title);
          } else {
            toast.error('Error al guardar notas.');
          }
          setNotasInput(linea.notas ?? '');
        },
      },
    );
  }

  function cancelarNotas() {
    setNotasInput(linea.notas ?? '');
    setNotasEditando(false);
  }

  const showAcciones = canEditarEstructural || canEliminar;

  return (
    <tr className={bgClass}>
      <td className="px-3 py-2 text-muted-foreground">{linea.posicion}</td>
      <td className="px-3 py-2">
        {articuloClave ? (
          <>
            <div className="font-mono text-xs font-medium">
              {articuloClave}
            </div>
            {articuloNombre && (
              <p className="mt-0.5 text-xs text-foreground/80 line-clamp-1">
                {articuloNombre}
              </p>
            )}
          </>
        ) : (
          <div
            className="font-mono text-xs text-muted-foreground"
            title={linea.articuloId}
          >
            {linea.articuloId.slice(0, 8)}…
          </div>
        )}
      </td>
      <td className="px-3 py-2 text-right tabular-nums">{linea.cantidad}</td>
      <td className="px-3 py-2">{linea.unidadMedida}</td>
      <td className="px-3 py-2 text-right">
        <MoneyDisplay
          amount={linea.precioEstimadoMonto}
          currency={linea.precioEstimadoMoneda}
        />
      </td>
      <td className="px-3 py-2">
        <DateTimeDisplay value={linea.fechaRequerida} />
      </td>
      <td className="px-3 py-2">
        <CubrimientoBar
          cantidad={linea.cantidad}
          cantDeAlmacen={linea.cantDeAlmacen}
          cantDeCompra={linea.cantDeCompra}
          cantRecibida={linea.cantRecibida}
          cantPendiente={linea.cantPendiente}
        />
      </td>
      <td className="px-3 py-2">
        {notasEditando ? (
          <div className="flex items-center gap-1">
            <Input
              type="text"
              value={notasInput}
              onChange={(e) => setNotasInput(e.target.value)}
              maxLength={500}
              autoFocus
              className="h-7 text-xs"
              onKeyDown={(e) => {
                if (e.key === 'Enter') guardarNotas();
                if (e.key === 'Escape') cancelarNotas();
              }}
            />
            <Button
              type="button"
              size="sm"
              variant="ghost"
              onClick={guardarNotas}
              disabled={actualizarNotas.isPending}
              className="h-7 px-2"
            >
              ✓
            </Button>
            <Button
              type="button"
              size="sm"
              variant="ghost"
              onClick={cancelarNotas}
              disabled={actualizarNotas.isPending}
              className="h-7 px-2"
            >
              ✕
            </Button>
          </div>
        ) : (
          <button
            type="button"
            onClick={canEditarNotas ? () => setNotasEditando(true) : undefined}
            disabled={!canEditarNotas}
            className={
              canEditarNotas
                ? 'block w-full max-w-xs text-left text-xs hover:underline'
                : 'block w-full max-w-xs text-left text-xs text-muted-foreground'
            }
            title={canEditarNotas ? 'Click para editar' : undefined}
          >
            {linea.notas ?? (canEditarNotas ? '— click para añadir —' : '—')}
          </button>
        )}
      </td>
      {showAcciones && (
        <td className="px-3 py-2 text-right">
          <div className="inline-flex gap-1">
            {canEditarEstructural && (
              <Button
                type="button"
                size="sm"
                variant="ghost"
                onClick={onEditar}
                aria-label={`Editar línea ${linea.posicion}`}
                className="h-7 px-2"
              >
                <Pencil className="h-3.5 w-3.5" />
              </Button>
            )}
            {canEliminar && (
              <Button
                type="button"
                size="sm"
                variant="ghost"
                onClick={onEliminar}
                aria-label={`Eliminar línea ${linea.posicion}`}
                className="h-7 px-2 text-rose-600 hover:bg-rose-50 hover:text-rose-700"
              >
                <Trash2 className="h-3.5 w-3.5" />
              </Button>
            )}
          </div>
        </td>
      )}
    </tr>
  );
}
