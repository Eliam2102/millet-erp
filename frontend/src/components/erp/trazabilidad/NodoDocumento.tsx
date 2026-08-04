import { Link } from '@tanstack/react-router';
import {
  ClipboardList,
  ShoppingCart,
  PackageCheck,
  Receipt,
  ReceiptText,
  CreditCard,
  ExternalLink,
  FileText,
  FileMinus,
  HandCoins,
  Truck,
} from 'lucide-react';
import {
  TipoDocumentoTrazabilidad,
  tipoDocumentoLabel,
  tipoDocumentoDetalleRoute,
  type NodoArbolDocumento,
} from '@/components/erp/trazabilidad/types';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;NodoDocumento/&gt;</c> — chip individual que renderea un nodo
 * del árbol de trazabilidad (UF7-PR2). Muestra:
 *
 * <list>
 *   <item>Ícono según tipo (clipboard / cart / package / receipt /
 *   credit-card).</item>
 *   <item>Folio + estado (chip pequeño).</item>
 *   <item>Fecha localizada.</item>
 *   <item>Link al detalle (si <c>tipoDocumentoDetalleRoute</c>
 *   resuelve no-null) — abre en la misma tab.</item>
 *   <item>Highlight visual para el nodo "actual" (el que el caller
 *   pasa como <c>esActual</c>).</item>
 * </list>
 *
 * <para>A11y: el link tiene texto descriptivo + el chip de tipo está
 * marcado como rol "img" con label.</para>
 */
export interface NodoDocumentoProps {
  nodo: NodoArbolDocumento;
  /** Si <c>true</c>, el nodo se renderea con border primary destacado. */
  esActual?: boolean;
}

const ICONO_POR_TIPO: Record<
  TipoDocumentoTrazabilidad,
  React.ComponentType<{ className?: string }>
> = {
  [TipoDocumentoTrazabilidad.Requisicion]: ClipboardList,
  [TipoDocumentoTrazabilidad.OrdenCompra]: ShoppingCart,
  [TipoDocumentoTrazabilidad.Recepcion]: PackageCheck,
  [TipoDocumentoTrazabilidad.FacturaProveedor]: Receipt,
  [TipoDocumentoTrazabilidad.PagoProveedor]: CreditCard,
  // Facturación (doc 13 §6.3).
  [TipoDocumentoTrazabilidad.PedidoFacturable]: FileText,
  [TipoDocumentoTrazabilidad.FacturaVenta]: ReceiptText,
  [TipoDocumentoTrazabilidad.FacturaAnticipo]: HandCoins,
  [TipoDocumentoTrazabilidad.NotaCredito]: FileMinus,
  [TipoDocumentoTrazabilidad.ReciboPago]: CreditCard,
  [TipoDocumentoTrazabilidad.CartaPorte]: Truck,
};

export function NodoDocumento({ nodo, esActual = false }: NodoDocumentoProps) {
  const Icon = ICONO_POR_TIPO[nodo.tipoDocumento] ?? ClipboardList;
  const tipoLabel = tipoDocumentoLabel(nodo.tipoDocumento);
  const route = tipoDocumentoDetalleRoute(nodo.tipoDocumento, nodo.id);

  const contenido = (
    <>
      <div className="flex items-center gap-2">
        <Icon className="h-4 w-4 shrink-0" aria-hidden="true" />
        <span className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
          {tipoLabel}
        </span>
        {route != null && (
          <ExternalLink className="ml-auto h-3 w-3 opacity-50" aria-hidden="true" />
        )}
      </div>
      <div className="mt-1 truncate font-mono text-sm font-semibold">
        {nodo.folio}
      </div>
      <div className="mt-0.5 flex flex-wrap items-baseline gap-1.5 text-xs">
        <span className="rounded bg-muted px-1.5 py-0.5 text-muted-foreground">
          {nodo.estado}
        </span>
        {nodo.fecha ? (
          <span className="text-muted-foreground">
            {new Date(nodo.fecha).toLocaleDateString()}
          </span>
        ) : null}
      </div>
    </>
  );

  const className = cn(
    'block min-w-[180px] max-w-[220px] rounded-md border bg-card p-2 text-left transition',
    esActual
      ? 'border-primary ring-2 ring-primary/30'
      : 'hover:border-foreground/30 hover:bg-muted/40',
  );

  if (route == null) {
    return (
      <div
        className={className}
        data-component="nodo-documento"
        data-tipo={tipoLabel}
        data-actual={esActual || undefined}
        aria-label={`${tipoLabel} ${nodo.folio} (sin detalle disponible)`}
      >
        {contenido}
      </div>
    );
  }

  return (
    <Link
      to={route.to}
      params={route.params}
      className={className}
      data-component="nodo-documento"
      data-tipo={tipoLabel}
      data-actual={esActual || undefined}
      aria-label={`Ver detalle de ${tipoLabel} ${nodo.folio}`}
    >
      {contenido}
    </Link>
  );
}
