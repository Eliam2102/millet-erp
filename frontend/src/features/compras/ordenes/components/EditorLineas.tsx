import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { textoMedida } from '@/features/almacen/lib/medida-hoja';
import { useMemo, useState } from 'react';
import { ConMotivo } from '@/features/compras/components/ConMotivo';
import { Pencil, Plus, Trash2, Inbox } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
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
import { etiquetaDescuento } from '../lib/subtotal-linea';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { formatCantidad } from '@/lib/format-cantidad';
import { useDecimalesUnidad } from '@/components/erp/forms/useDecimalesUnidad';

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
  const [lineaEnEdicionId, setLineaEnEdicionId] = useState<string | null>(null);
  const [lineaAEliminar, setLineaAEliminar] = useState<LineaOrdenCompraResponse | null>(null);

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
  const articulosMap = useMemo(() => mapById(articulosQuery.data?.items), [articulosQuery.data]);

  const accAgregarManual = accionAgregarLineaManual(oc, permisos);
  const accEditar = accionEditarLineaEstructural(oc, permisos);
  const accEliminar = accionEliminarLinea(oc, permisos);
  // Fracción arancelaria: columna solo en OC de comercio exterior (PLATFORM-TODO(<F1-COM-06>): supuesto Vilo).
  const mostrarFraccion = oc.esImportacion;

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
      toast.success(`Partida ${linea.posicion} eliminada.`);
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
      toast.error('No se pudo eliminar la partida.', {
        description: err instanceof Error ? err.message : 'Error desconocido.',
      });
    }
  }

  const lineasOrdenadas = [...oc.lineas].sort((a, b) => a.posicion - b.posicion);
  const lineaEnEdicion = lineasOrdenadas.find((l) => l.id === lineaEnEdicionId);

  // Empty state: ninguna línea + inline form para agregar la primera, si
  // la matriz lo permite.
  if (lineasOrdenadas.length === 0) {
    if (agregarAbierto) {
      return <LineaInlineFormOc oc={oc} onCancel={() => setAgregarAbierto(false)} />;
    }
    return (
      <EmptyState
        icon={<Inbox className="h-10 w-10" />}
        title={
          accAgregarManual.visible
            ? 'Esta OC no tiene partidas todavía.'
            : 'Sin partidas registradas.'
        }
        description={
          accAgregarManual.visible && accAgregarManual.habilitada
            ? 'Una OC debe tener al menos una partida para autorizarse.'
            : 'Las partidas se agregan en estado Borrador o Rechazada.'
        }
        action={
          accAgregarManual.visible && accAgregarManual.habilitada ? (
            <Button onClick={abrirAgregar}>
              <Plus className="mr-2 h-4 w-4" />
              Agregar partida manual
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
          Partidas ({lineasOrdenadas.length})
        </h3>
        {!agregarAbierto && accAgregarManual.visible && (
          <div className="flex items-center gap-2">
            <ConMotivo motivo={accAgregarManual.motivoDeshabilitada}>
              <Button
                size="sm"
                variant="outline"
                onClick={abrirAgregar}
                disabled={!accAgregarManual.habilitada}
                data-action="agregar-linea-manual"
              >
                <Plus className="mr-1 h-3.5 w-3.5" />
                Agregar partida manual
              </Button>
            </ConMotivo>
          </div>
        )}
      </div>

      {/* Tabla de líneas */}
      <div className="overflow-x-auto rounded-md border bg-surface-card">
        <Table className="w-full min-w-full text-sm">
          <TableHeader className="bg-surface-muted/50 text-xs uppercase tracking-wide text-ink-muted">
            <TableRow>
              <TableHead className="px-3 py-2 text-right font-medium w-12">#</TableHead>
              <TableHead className="px-3 py-2 text-left font-medium">Artículo</TableHead>
              {mostrarFraccion && (
                <TableHead className="px-3 py-2 text-left font-medium">
                  Fracción arancelaria
                </TableHead>
              )}
              <TableHead className="hidden px-3 py-2 text-left font-medium @4xl:table-cell">
                CC-Máquina
              </TableHead>
              <TableHead className="px-3 py-2 text-right font-medium">Cantidad</TableHead>
              <TableHead className="px-3 py-2 text-left font-medium">UM</TableHead>
              <TableHead className="hidden px-3 py-2 text-right font-medium @4xl:table-cell">
                Recibido
              </TableHead>
              <TableHead className="hidden px-3 py-2 text-right font-medium @4xl:table-cell">
                Por surtir
              </TableHead>
              <TableHead className="px-3 py-2 text-right font-medium">Precio</TableHead>
              <TableHead className="px-3 py-2 text-right font-medium">Subtotal</TableHead>
              <TableHead className="hidden px-3 py-2 text-right font-medium @4xl:table-cell">
                IVA
              </TableHead>
              {(accEditar.visible || accEliminar.visible) && (
                <TableHead className="px-3 py-2 text-right font-medium">Acciones</TableHead>
              )}
            </TableRow>
          </TableHeader>
          <TableBody className="[&_tr]:h-12">
            {lineasOrdenadas.map((linea, i) => {
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
                  moneda={oc.moneda}
                  articuloClave={linea.articuloClave ?? articulo?.clave ?? null}
                  articuloNombre={linea.articuloNombre ?? articulo?.nombre ?? null}
                  bgClass={i % 2 === 1 ? 'bg-surface-muted/20' : undefined}
                  mostrarFraccion={mostrarFraccion}
                  canEditar={accEditar.habilitada}
                  canEliminar={accEliminar.habilitada}
                  showAcciones={accEditar.visible || accEliminar.visible}
                  onEditar={() => abrirEditar(linea)}
                  onEliminar={() => setLineaAEliminar(linea)}
                />
              );
            })}
          </TableBody>
          <tfoot>
            <TableRow className="border-t bg-surface-muted/30 font-medium">
              <TableCell colSpan={3} className="hidden @4xl:table-cell" />
              <TableCell colSpan={mostrarFraccion ? 6 : 5} className="px-3 py-2 text-right">
                Totales
              </TableCell>
              <TableCell className="px-3 py-2 text-right tabular-nums">
                <MoneyDisplay
                  amount={lineasOrdenadas.reduce((acc, l) => acc + l.subtotalLinea, 0)}
                  currency={oc.moneda}
                  strong
                />
              </TableCell>
              <TableCell className="hidden px-3 py-2 text-right tabular-nums @4xl:table-cell">
                <MoneyDisplay
                  amount={lineasOrdenadas.reduce((acc, l) => acc + l.ivaImporte, 0)}
                  currency={oc.moneda}
                />
              </TableCell>
              {(accEditar.visible || accEliminar.visible) && <TableCell />}
            </TableRow>
          </tfoot>
        </Table>
      </div>

      {lineaEnEdicion && (
        <LineaInlineFormOc
          oc={oc}
          linea={lineaEnEdicion}
          onCancel={cerrarEdicion}
          onSaved={cerrarEdicion}
        />
      )}

      {/* Inline form expandible al final — agregar líneas sin modal. El
          form se mantiene abierto entre submits para agregar varias en
          sucesión (mismo UX que RQ). */}
      {agregarAbierto && <LineaInlineFormOc oc={oc} onCancel={() => setAgregarAbierto(false)} />}

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
              Vas a eliminar la partida {lineaAEliminar?.posicion} ({lineaAEliminar?.cantidad}{' '}
              {lineaAEliminar?.unidadMedida}). Esta acción no se puede deshacer. Solo aplica en
              estado Borrador o Rechazada.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={eliminar.isPending}>Cancelar</AlertDialogCancel>
            <AlertDialogAction
              onClick={confirmarEliminar}
              disabled={eliminar.isPending}
              className="bg-danger-bg text-danger-fg hover:bg-danger-bg/90"
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
  /** Moneda de la OC, para formatear importes. */
  moneda: string;
  /** Clave del artículo resuelta contra el catálogo. <c>null</c> si
   * el artículo no está en el catálogo cargado (huérfano o limit). */
  articuloClave: string | null;
  /** Nombre del artículo resuelto contra el catálogo. <c>null</c> si
   * el artículo no está en el catálogo cargado. */
  articuloNombre: string | null;
  bgClass?: string;
  mostrarFraccion: boolean;
  canEditar: boolean;
  canEliminar: boolean;
  showAcciones: boolean;
  onEditar: () => void;
  onEliminar: () => void;
}

function FilaLinea({
  linea,
  moneda,
  articuloClave,
  articuloNombre,
  bgClass,
  mostrarFraccion,
  canEditar,
  canEliminar,
  showAcciones,
  onEditar,
  onEliminar,
}: FilaLineaProps) {
  const decimales = useDecimalesUnidad().porCodigo(linea.unidadMedida);
  return (
    <TableRow className={cn(bgClass)} data-linea={linea.id}>
      <TableCell className="px-3 py-2 text-right tabular-nums text-ink-muted">
        {linea.posicion}
      </TableCell>
      <TableCell className="px-3 py-2">
        <div className="flex flex-wrap items-center gap-1.5">
          {articuloClave ? (
            <span className="font-mono text-xs font-medium">{articuloClave}</span>
          ) : (
            <span className="font-mono text-xs text-ink-muted" title={linea.articuloId}>
              {linea.articuloId.slice(0, 8)}…
            </span>
          )}
          {linea.requisicionId != null && (
            <LineaDesdeRqBadge requisicionId={linea.requisicionId} folio={linea.requisicionFolio} />
          )}
        </div>
        {articuloNombre && (
          <p className="mt-0.5 text-xs text-ink/80 line-clamp-1">{articuloNombre}</p>
        )}
        {textoMedida(linea, linea.cantidad) && (
          <p className="mt-0.5 text-xs text-ink-muted" data-testid="oc-linea-medida">
            {textoMedida(linea, linea.cantidad)}
          </p>
        )}
        {linea.loteProveedor && (
          <p className="mt-0.5 text-xs text-ink-muted" data-testid="oc-linea-lote">
            Lote {linea.loteProveedor}
          </p>
        )}
        {linea.descripcionExtendida && (
          <p className="mt-1 text-xs text-ink-muted line-clamp-1">{linea.descripcionExtendida}</p>
        )}
      </TableCell>
      {mostrarFraccion && (
        <TableCell className="px-3 py-2 font-mono text-xs" data-testid="oc-linea-fraccion">
          {linea.fraccionArancelaria ?? <span className="text-ink-muted">—</span>}
        </TableCell>
      )}
      <TableCell className="hidden px-3 py-2 @4xl:table-cell">
        {/* CC-Máquina resuelto por el read-port (ADR-0050): "clave — nombre",
            "No catalogado" si el id no resuelve, "—" si la línea no lleva CC. */}
        {linea.centroCostoId ? (
          formatCcMaquinaLabel({
            clave: linea.centroCostoClave,
            nombre: linea.centroCostoNombre,
          })
        ) : (
          <span className="text-ink-muted">—</span>
        )}
      </TableCell>
      <TableCell className="px-3 py-2 text-right tabular-nums">
        {formatCantidad(linea.cantidad, decimales)}
      </TableCell>
      <TableCell className="px-3 py-2">{linea.unidadMedida}</TableCell>
      <TableCell
        className="hidden px-3 py-2 text-right tabular-nums @4xl:table-cell"
        data-testid="oc-linea-recibido"
      >
        {formatCantidad(linea.cantidadRecibida, decimales)}
      </TableCell>
      {/* Por surtir = lo que el proveedor aún no ha entregado (nunca negativo). */}
      <TableCell
        className="hidden px-3 py-2 text-right tabular-nums @4xl:table-cell"
        data-testid="oc-linea-por-surtir"
      >
        {formatCantidad(Math.max(linea.cantidad - linea.cantidadRecibida, 0), decimales)}
      </TableCell>
      <TableCell className="px-3 py-2 text-right tabular-nums">
        {linea.precioUnitario <= 0 ? (
          <span className="inline-flex flex-col items-end gap-1" data-testid="oc-linea-sin-precio">
            <span className="font-medium text-danger-fg">
              <MoneyDisplay amount={linea.precioUnitario} currency={moneda} />
            </span>
            <Badge variant="warning">Sin precio</Badge>
          </span>
        ) : (
          <MoneyDisplay amount={linea.precioUnitario} currency={moneda} />
        )}
      </TableCell>
      <TableCell className="px-3 py-2 text-right tabular-nums font-medium">
        <MoneyDisplay amount={linea.subtotalLinea} currency={moneda} />
        <p className="text-xs font-normal text-ink-muted">
          {etiquetaDescuento(linea.descuentoTipo, linea.descuentoValor)}
        </p>
      </TableCell>
      <TableCell className="hidden px-3 py-2 text-right tabular-nums text-ink-muted @4xl:table-cell">
        <MoneyDisplay amount={linea.ivaImporte} currency={moneda} />
      </TableCell>
      {showAcciones && (
        <TableCell className="px-3 py-2 text-right">
          <div className="inline-flex gap-1">
            {canEditar && (
              <Button
                type="button"
                size="sm"
                variant="ghost"
                onClick={onEditar}
                aria-label={`Editar partida ${linea.posicion}`}
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
                aria-label={`Eliminar partida ${linea.posicion}`}
                className="h-7 px-2 text-danger-fg hover:bg-danger-bg hover:text-danger-fg"
                data-action="eliminar-linea"
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
