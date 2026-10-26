import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { useMemo, useState } from 'react';
import { ConMotivo } from '@/features/compras/components/ConMotivo';
import { Check, Pencil, Plus, Trash2, X } from 'lucide-react';
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
import { EmptyState, DateTimeDisplay } from '@/components/erp';
import { CubrimientoBar } from '@/components/erp/display/CubrimientoBar';
import { LineaInlineForm } from '@/features/compras/components/LineaInlineForm';
import type { LineaResponse, RequisicionResponse } from '@/features/compras/api/types';
import { useActualizarNotasLinea, useEliminarLinea } from '@/features/compras/api/useLineas';
import {
  accionAgregarLinea,
  accionEditarLinea,
  accionEditarNotasLinea,
  accionEliminarLinea,
} from '@/features/compras/lib/acciones-disponibles';
import { useAuthStore } from '@/lib/auth/auth-store';
import { esApiError, codigoSoporte } from '@/lib/api';
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
  const articulosMap = useMemo(() => mapById(articulosQuery.data?.items), [articulosQuery.data]);

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
          toast.success(`Partida ${linea.posicion} eliminada`);
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
              description: codigoSoporte(error),
            });
          } else {
            toast.error('Error inesperado al eliminar la partida.');
          }
        },
      },
    );
  }

  if (rq.lineas.length === 0) {
    if (agregarAbierto) {
      return <LineaInlineForm requisicionId={rq.id} clasificacion={rq.clasificacion} onCancel={() => setAgregarAbierto(false)} />;
    }
    return (
      <EmptyState
        title={
          canAgregar.visible && canAgregar.habilitada
            ? 'Esta requisición no tiene partidas. Agrega la primera.'
            : 'Sin partidas registradas.'
        }
        description={
          canAgregar.visible && canAgregar.habilitada
            ? 'Una RQ debe tener al menos una partida para transmitirse.'
            : undefined
        }
        action={
          canAgregar.visible && canAgregar.habilitada ? (
            <Button onClick={abrirAgregar}>
              <Plus className="mr-2 h-4 w-4" />
              Agregar partida
            </Button>
          ) : undefined
        }
      />
    );
  }

  const lineas = rq.lineas.slice().sort((a, b) => a.posicion - b.posicion);
  const lineaEnEdicion = lineas.find((l) => l.id === lineaEnEdicionId);

  return (
    <>
      <div className="space-y-3">
        {canAgregar.visible && !agregarAbierto && (
          <div className="flex justify-end">
            <ConMotivo motivo={canAgregar.motivoDeshabilitada}>
              <Button size="sm" onClick={abrirAgregar} disabled={!canAgregar.habilitada}>
                <Plus className="mr-2 h-4 w-4" />
                Agregar partida
              </Button>
            </ConMotivo>
          </div>
        )}

        <div className="overflow-x-auto rounded-md border bg-surface-card">
          <Table className="w-full min-w-full text-sm">
            <TableHeader className="bg-surface-muted/50 text-xs uppercase tracking-wide text-ink-muted">
              <TableRow>
                <TableHead className="px-3 py-2 text-left font-medium">#</TableHead>
                <TableHead className="px-3 py-2 text-left font-medium">Artículo</TableHead>
                <TableHead className="px-3 py-2 text-right font-medium">Cant.</TableHead>
                <TableHead className="px-3 py-2 text-left font-medium">UM</TableHead>
                <TableHead className="px-3 py-2 text-left font-medium hidden @4xl:table-cell">
                  Fecha req.
                </TableHead>
                <TableHead className="px-3 py-2 text-left font-medium">Cubrimiento</TableHead>
                <TableHead className="px-3 py-2 text-left font-medium hidden @4xl:table-cell">
                  Notas
                </TableHead>
                {(canEditarEstructural.visible || canEliminar.visible) && (
                  <TableHead className="px-3 py-2 text-right font-medium">Acciones</TableHead>
                )}
              </TableRow>
            </TableHeader>
            <TableBody className="[&_tr]:h-12">
              {lineas.map((linea, i) => {
                // La fila en edición se oculta: su form se monta fuera de la
                // tabla para no heredar su ancho (UX-F3-01).
                if (linea.id === lineaEnEdicionId) return null;
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
                    bgClass={i % 2 === 1 ? 'bg-surface-muted/20' : undefined}
                    canEditarEstructural={canEditarEstructural.habilitada}
                    canEliminar={canEliminar.habilitada}
                    canEditarNotas={canEditarNotas.habilitada}
                    onEditar={() => abrirEditar(linea)}
                    onEliminar={() => setLineaAEliminar(linea)}
                  />
                );
              })}
            </TableBody>
          </Table>
        </div>

        {lineaEnEdicion && (
          <LineaInlineForm
            requisicionId={rq.id}
            clasificacion={rq.clasificacion}
            linea={lineaEnEdicion}
            onCancel={cerrarEdicion}
            onSaved={cerrarEdicion}
          />
        )}

        {/* Inline form expandible al final — agregar líneas sin modal
            (design/frontend-polish). El form se mantiene abierto entre
            submits para agregar varias en sucesión. */}
        {agregarAbierto && (
          <LineaInlineForm requisicionId={rq.id} clasificacion={rq.clasificacion} onCancel={() => setAgregarAbierto(false)} />
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
            <AlertDialogTitle>¿Eliminar partida {lineaAEliminar?.posicion}?</AlertDialogTitle>
            <AlertDialogDescription>
              Esta acción no se puede deshacer. La partida se borra de la RQ en estado Borrador.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={eliminar.isPending}>Cancelar</AlertDialogCancel>
            <AlertDialogAction
              onClick={confirmarEliminar}
              disabled={eliminar.isPending}
              className="bg-danger-bg text-danger-fg hover:bg-danger-bg/90"
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
  const [notasError, setNotasError] = useState<string | null>(null);
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
          setNotasError(null);
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
          // Conserva lo tecleado y deja el campo abierto: el usuario corrige o reintenta.
          setNotasError(
            esApiError(error) ? error.problem.title : 'No se pudieron guardar las notas.',
          );
        },
      },
    );
  }

  function cancelarNotas() {
    setNotasError(null);
    setNotasInput(linea.notas ?? '');
    setNotasEditando(false);
  }

  const showAcciones = canEditarEstructural || canEliminar;

  return (
    <TableRow className={bgClass}>
      <TableCell className="px-3 py-2 text-ink-muted">{linea.posicion}</TableCell>
      <TableCell className="px-3 py-2">
        {articuloClave ? (
          <>
            <div className="font-mono text-xs font-medium">{articuloClave}</div>
            {articuloNombre && (
              <p className="mt-0.5 text-xs text-ink/80 line-clamp-1">{articuloNombre}</p>
            )}
          </>
        ) : (
          <div className="font-mono text-xs text-ink-muted" title={linea.articuloId}>
            {linea.articuloId.slice(0, 8)}…
          </div>
        )}
      </TableCell>
      <TableCell className="px-3 py-2 text-right tabular-nums">{linea.cantidad}</TableCell>
      <TableCell className="px-3 py-2">{linea.unidadMedida}</TableCell>
      <TableCell className="hidden px-3 py-2 @4xl:table-cell">
        <DateTimeDisplay value={linea.fechaRequerida} />
      </TableCell>
      <TableCell className="px-3 py-2">
        <CubrimientoBar
          cantidad={linea.cantidad}
          cantDeAlmacen={linea.cantDeAlmacen}
          cantDeCompra={linea.cantDeCompra}
          cantEnOc={linea.cantComprometidaEnOc}
          cantRecibida={linea.cantRecibida}
          cantPendiente={linea.cantPendiente}
          cantSinCompra={linea.cantSinCompra}
        />
      </TableCell>
      <TableCell className="hidden px-3 py-2 @4xl:table-cell">
        {notasEditando ? (
          <div className="flex flex-wrap items-center gap-1">
            <Input
              type="text"
              aria-label={`Notas de la partida ${linea.posicion}`}
              aria-invalid={notasError ? true : undefined}
              aria-describedby={notasError ? `notas-error-${linea.id}` : undefined}
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
              aria-label={`Guardar notas de la partida ${linea.posicion}`}
              className="h-7 px-2"
            >
              <Check className="h-3.5 w-3.5" strokeWidth={1.6} aria-hidden="true" />
            </Button>
            <Button
              type="button"
              size="sm"
              variant="ghost"
              onClick={cancelarNotas}
              disabled={actualizarNotas.isPending}
              aria-label={`Cancelar edición de notas de la partida ${linea.posicion}`}
              className="h-7 px-2"
            >
              <X className="h-3.5 w-3.5" strokeWidth={1.6} aria-hidden="true" />
            </Button>
            {notasError && (
              <p
                id={`notas-error-${linea.id}`}
                role="alert"
                className="w-full text-xs text-danger-fg"
              >
                {notasError}
              </p>
            )}
          </div>
        ) : (
          <button
            type="button"
            onClick={canEditarNotas ? () => setNotasEditando(true) : undefined}
            disabled={!canEditarNotas}
            className={
              canEditarNotas
                ? 'block w-full max-w-xs text-left text-xs hover:underline'
                : 'block w-full max-w-xs text-left text-xs text-ink-muted'
            }
            title={canEditarNotas ? 'Click para editar' : undefined}
          >
            {linea.notas ?? (canEditarNotas ? '— click para añadir —' : '—')}
          </button>
        )}
      </TableCell>
      {showAcciones && (
        <TableCell className="px-3 py-2 text-right">
          <div className="inline-flex gap-1">
            {canEditarEstructural && (
              <Button
                type="button"
                size="sm"
                variant="ghost"
                onClick={onEditar}
                aria-label={`Editar partida ${linea.posicion}`}
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
                aria-label={`Eliminar partida ${linea.posicion}`}
                className="h-7 px-2 text-danger-fg hover:bg-danger-bg hover:text-danger-fg"
              >
                <Trash2 className="h-3.5 w-3.5" />
              </Button>
            )}
          </div>
        </TableCell>
      )}
    </TableRow>
  );
}
