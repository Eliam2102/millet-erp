import { useMemo, useState } from 'react';
import { Pencil, Plus, Trash2, Inbox } from 'lucide-react';
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
import { EmptyState } from '@/components/erp';
import {
  accionAgregarLineaManual,
  accionEditarLineaEstructural,
  accionEliminarLinea,
} from '@/features/compras/ordenes/lib/acciones-disponibles';
import {
  type LineaOrdenCompraResponse,
  type OrdenCompraDetalleResponse,
} from '@/features/compras/ordenes/api/types';
import { useEliminarLinea } from '@/features/compras/ordenes/api/useEliminarLinea';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { useQueryClient } from '@tanstack/react-query';
import { handleOcMutationError } from '@/features/compras/ordenes/lib/handle-conflict';
import { LineaInlineFormOc } from '@/features/compras/ordenes/components/LineaInlineFormOc';
import { LineaDesdeRqBadge } from '@/features/compras/ordenes/components/LineaDesdeRqBadge';
import { useArticulos, mapById } from '@/features/catalogos/api';
import { formatCcMaquinaLabel } from '@/features/centros-costo/lib/cc-maquina-label';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;EditorLineas/&gt;</c> — tabla de líneas con add/edit/delete
 * inline para Tab "Líneas" del detalle de OC. Mismo patrón que el
 * editor de RQ: agregar via inline form expandible al final, editar
 * reemplaza la fila por el form (border ámbar), eliminar via
 * AlertDialog.
 *
 * <para>Acciones gateadas por la matriz §6.1 vía
 * <c>acciones-disponibles.ts</c>:</para>
 * <list>
 *   <item><c>Agregar línea (manual)</c>: solo si la OC es
 *   <c>SinRequisicionPrevia=true</c> Y estado en
 *   <c>Borrador</c>/<c>Rechazada</c>.</item>
 *   <item><c>Editar línea</c>: estructural en
 *   <c>Borrador</c>/<c>Rechazada</c>; líneas heredadas de RQ tienen
 *   articulo+cantidad bloqueados.</item>
 *   <item><c>Eliminar línea</c>: en
 *   <c>Borrador</c>/<c>Rechazada</c> + confirm dialog.</item>
 * </list>
 *
 * <para><b>Agregar líneas desde RQ post-creación</b> está
 * intencionalmente fuera de scope: el flujo principal es consolidar
 * N RQs al crear la OC desde el Sheet "Nueva OC". Si emerge necesidad
 * en UAT, se evaluará en Fase 2.</para>
 */
export interface EditorLineasProps {
  oc: OrdenCompraDetalleResponse;
}

export function EditorLineas({ oc }: EditorLineasProps) {
  const permisos = useAuthStore((s) => s.permisos);

  // Tanto agregar como editar usan <LineaInlineFormOc> — sin modal
  // (frontend/docs/patrones-compras.md §6). Solo uno de los dos modos
  // puede estar activo a la vez (cancelan el otro al abrir).
  const [agregarAbierto, setAgregarAbierto] = useState(false);
  const [lineaEnEdicionId, setLineaEnEdicionId] = useState<string | null>(
    null,
  );
  const [lineaAEliminar, setLineaAEliminar] =
    useState<LineaOrdenCompraResponse | null>(null);

  const eliminar = useEliminarLinea();
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();

  // Catálogo de artículos para resolver articuloId → clave/descripción
  // en cada fila. Limit alto + includeInactivas para no perder
  // artículos que pudieron haberse marcado inactivos después de
  // usarse en esta OC.
  const articulosQuery = useArticulos({
    limit: 1000,
    includeInactivas: true,
  });
  const articulosMap = useMemo(
    () => mapById(articulosQuery.data?.items),
    [articulosQuery.data],
  );

  const accAgregarManual = accionAgregarLineaManual(oc, permisos);
  const accEditar = accionEditarLineaEstructural(oc, permisos);
  const accEliminar = accionEliminarLinea(oc, permisos);

  function abrirAgregar() {
    setLineaEnEdicionId(null);
    setAgregarAbierto(true);
  }

  function abrirEditar(linea: LineaOrdenCompraResponse) {
    setAgregarAbierto(false);
    setLineaEnEdicionId(linea.id);
  }

  function cerrarEdicion() {
    setLineaEnEdicionId(null);
  }

  async function confirmarEliminar() {
    if (lineaAEliminar == null) return;
    const linea = lineaAEliminar;
    try {
      await eliminar.mutateAsync({
        ordenCompraId: oc.id,
        lineaId: linea.id,
      });
      toast.success(`Línea ${linea.posicion} eliminada.`);
      setLineaAEliminar(null);
    } catch (err) {
      // 409 → ConflictDialog en modo simple. Cierra el confirm para
      // que el dialog se vea sin overlap.
      if (
        handleOcMutationError(err, {
          ordenCompraId: oc.id,
          conflictDialog,
          queryClient,
        })
      ) {
        setLineaAEliminar(null);
        return;
      }
      toast.error('No se pudo eliminar la línea.', {
        description: err instanceof Error ? err.message : 'Error desconocido.',
      });
    }
  }

  const lineasOrdenadas = [...oc.lineas].sort(
    (a, b) => a.posicion - b.posicion,
  );

  // Empty state: ninguna línea + inline form para agregar la primera, si
  // la matriz lo permite.
  if (lineasOrdenadas.length === 0) {
    if (agregarAbierto) {
      return (
        <LineaInlineFormOc
          oc={oc}
          onCancel={() => setAgregarAbierto(false)}
        />
      );
    }
    return (
      <EmptyState
        icon={<Inbox className="h-10 w-10" />}
        title={
          accAgregarManual.visible
            ? 'Esta OC no tiene líneas todavía.'
            : 'Sin líneas registradas.'
        }
        description={
          accAgregarManual.visible && accAgregarManual.habilitada
            ? 'Una OC debe tener al menos una línea para autorizarse.'
            : 'Las líneas se agregan en estado Borrador o Rechazada.'
        }
        action={
          accAgregarManual.visible && accAgregarManual.habilitada ? (
            <Button onClick={abrirAgregar}>
              <Plus className="mr-2 h-4 w-4" />
              Agregar línea manual
            </Button>
          ) : undefined
        }
      />
    );
  }

  return (
    <div className="space-y-3" data-component="editor-lineas">
      {/* Toolbar — botón "Agregar" arriba si no está abierto el form */}
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-sm font-semibold tracking-tight">
          Líneas ({lineasOrdenadas.length})
        </h3>
        {!agregarAbierto && accAgregarManual.visible && (
          <div className="flex items-center gap-2">
            <Button
              size="sm"
              variant="outline"
              onClick={abrirAgregar}
              disabled={!accAgregarManual.habilitada}
              title={accAgregarManual.motivoDeshabilitada}
              data-action="agregar-linea-manual"
            >
              <Plus className="mr-1 h-3.5 w-3.5" />
              Agregar línea manual
            </Button>
          </div>
        )}
      </div>

      {/* Tabla de líneas */}
      <div className="overflow-x-auto rounded-md border bg-card">
        <table className="w-full min-w-[940px] text-sm">
          <thead className="bg-muted/50 text-xs uppercase tracking-wide text-muted-foreground">
            <tr>
              <th className="px-3 py-2 text-right font-medium w-12">#</th>
              <th className="px-3 py-2 text-left font-medium">Artículo</th>
              <th className="px-3 py-2 text-left font-medium">CC-Máquina</th>
              <th className="px-3 py-2 text-right font-medium">Cantidad</th>
              <th className="px-3 py-2 text-left font-medium">UM</th>
              <th className="px-3 py-2 text-right font-medium">Precio</th>
              <th className="px-3 py-2 text-right font-medium">Subtotal</th>
              <th className="px-3 py-2 text-right font-medium">IVA</th>
              {(accEditar.visible || accEliminar.visible) && (
                <th className="px-3 py-2 text-right font-medium">Acciones</th>
              )}
            </tr>
          </thead>
          <tbody>
            {lineasOrdenadas.map((linea, i) => {
              if (linea.id === lineaEnEdicionId) {
                // La fila se reemplaza por el form inline en modo editar.
                const totalColumnas =
                  8 +
                  (accEditar.visible || accEliminar.visible ? 1 : 0);
                return (
                  <tr key={linea.id}>
                    <td colSpan={totalColumnas} className="p-2">
                      <LineaInlineFormOc
                        oc={oc}
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
                  articuloClave={linea.articuloClave ?? articulo?.clave ?? null}
                  articuloNombre={linea.articuloNombre ?? articulo?.nombre ?? null}
                  bgClass={i % 2 === 1 ? 'bg-muted/20' : undefined}
                  canEditar={accEditar.habilitada}
                  canEliminar={accEliminar.habilitada}
                  showAcciones={
                    accEditar.visible || accEliminar.visible
                  }
                  onEditar={() => abrirEditar(linea)}
                  onEliminar={() => setLineaAEliminar(linea)}
                />
              );
            })}
          </tbody>
          <tfoot>
            <tr className="border-t bg-muted/30 font-medium">
              <td colSpan={6} className="px-3 py-2 text-right">
                Totales
              </td>
              <td className="px-3 py-2 text-right tabular-nums">
                {lineasOrdenadas
                  .reduce((acc, l) => acc + l.subtotalLinea, 0)
                  .toFixed(2)}
              </td>
              <td className="px-3 py-2 text-right tabular-nums">
                {lineasOrdenadas
                  .reduce((acc, l) => acc + l.ivaImporte, 0)
                  .toFixed(2)}
              </td>
              {(accEditar.visible || accEliminar.visible) && <td />}
            </tr>
          </tfoot>
        </table>
      </div>

      {/* Inline form expandible al final — agregar líneas sin modal. El
          form se mantiene abierto entre submits para agregar varias en
          sucesión (mismo UX que RQ). */}
      {agregarAbierto && (
        <LineaInlineFormOc
          oc={oc}
          onCancel={() => setAgregarAbierto(false)}
        />
      )}

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
              Vas a eliminar la línea {lineaAEliminar?.posicion} (
              {lineaAEliminar?.cantidad} {lineaAEliminar?.unidadMedida}).
              Esta acción no se puede deshacer. Solo aplica en estado
              Borrador o Rechazada.
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
              data-action="confirmar-eliminar"
            >
              {eliminar.isPending ? 'Eliminando…' : 'Eliminar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

// ─── Fila de la tabla ─────────────────────────────────────────────

interface FilaLineaProps {
  linea: LineaOrdenCompraResponse;
  /** Clave del artículo resuelta contra el catálogo. <c>null</c> si
   * el artículo no está en el catálogo cargado (huérfano o limit). */
  articuloClave: string | null;
  /** Nombre del artículo resuelto contra el catálogo. <c>null</c> si
   * el artículo no está en el catálogo cargado. */
  articuloNombre: string | null;
  bgClass?: string;
  canEditar: boolean;
  canEliminar: boolean;
  showAcciones: boolean;
  onEditar: () => void;
  onEliminar: () => void;
}

function FilaLinea({
  linea,
  articuloClave,
  articuloNombre,
  bgClass,
  canEditar,
  canEliminar,
  showAcciones,
  onEditar,
  onEliminar,
}: FilaLineaProps) {
  return (
    <tr className={cn(bgClass)} data-linea={linea.id}>
      <td className="px-3 py-2 text-right tabular-nums text-muted-foreground">
        {linea.posicion}
      </td>
      <td className="px-3 py-2">
        <div className="flex flex-wrap items-center gap-1.5">
          {articuloClave ? (
            <span className="font-mono text-xs font-medium">
              {articuloClave}
            </span>
          ) : (
            <span
              className="font-mono text-xs text-muted-foreground"
              title={linea.articuloId}
            >
              {linea.articuloId.slice(0, 8)}…
            </span>
          )}
          {linea.requisicionId != null && (
            <LineaDesdeRqBadge
              requisicionId={linea.requisicionId}
              folio={linea.requisicionFolio}
            />
          )}
        </div>
        {articuloNombre && (
          <p className="mt-0.5 text-xs text-foreground/80 line-clamp-1">
            {articuloNombre}
          </p>
        )}
        {linea.descripcionExtendida && (
          <p className="mt-1 text-xs text-muted-foreground line-clamp-1">
            {linea.descripcionExtendida}
          </p>
        )}
      </td>
      <td className="px-3 py-2">
        {/* CC-Máquina resuelto por el read-port (ADR-0050): "clave — nombre",
            "No catalogado" si el id no resuelve, "—" si la línea no lleva CC. */}
        {linea.centroCostoId ? (
          formatCcMaquinaLabel({
            clave: linea.centroCostoClave,
            nombre: linea.centroCostoNombre,
          })
        ) : (
          <span className="text-muted-foreground">—</span>
        )}
      </td>
      <td className="px-3 py-2 text-right tabular-nums">
        {linea.cantidad.toFixed(2)}
      </td>
      <td className="px-3 py-2">{linea.unidadMedida}</td>
      <td className="px-3 py-2 text-right tabular-nums">
        {linea.precioUnitario.toFixed(2)}
      </td>
      <td className="px-3 py-2 text-right tabular-nums font-medium">
        {linea.subtotalLinea.toFixed(2)}
      </td>
      <td className="px-3 py-2 text-right tabular-nums text-muted-foreground">
        {linea.ivaImporte.toFixed(2)}
      </td>
      {showAcciones && (
        <td className="px-3 py-2 text-right">
          <div className="inline-flex gap-1">
            {canEditar && (
              <Button
                type="button"
                size="sm"
                variant="ghost"
                onClick={onEditar}
                aria-label={`Editar línea ${linea.posicion}`}
                className="h-7 px-2"
                data-action="editar-linea"
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
                data-action="eliminar-linea"
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
