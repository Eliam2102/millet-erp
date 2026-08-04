import { Link } from '@tanstack/react-router';
import { GitFork, ExternalLink } from 'lucide-react';
import { useOcsHermanasDuplicadas } from '@/features/compras/ordenes/api/useOcsHermanasDuplicadas';
import type { OrdenCompraDetalleResponse } from '@/features/compras/ordenes/api/types';

/**
 * <c>&lt;AsideListOcsHermanas/&gt;</c> — bloque informativo del
 * detalle de OC con la cadena de duplicaciones (UF5-PR2, FOC7).
 *
 * <list>
 *   <item>Si la OC actual <b>tiene</b> <c>ocOrigenId</c>: muestra link
 *   "Origen: OC-..." apuntando al detalle del original. Si comparte
 *   origen con otras hermanas, las lista.</item>
 *   <item>Si la OC actual <b>es</b> origen de duplicaciones: lista todas
 *   las hermanas con folio + estado.</item>
 *   <item>Si no hay relación: el componente devuelve <c>null</c>
 *   (no aparece nada en el detalle).</item>
 * </list>
 *
 * <para><b>Nota</b>: el endpoint <c>/duplicadas</c> filtra por
 * <c>oc_origen_id = id</c>. Para "ver hermanas que comparten origen
 * con esta OC duplicada", consultamos con <c>oc.ocOrigenId</c> y
 * excluimos el id actual del listado.</para>
 */
export interface AsideListOcsHermanasProps {
  oc: OrdenCompraDetalleResponse;
}

export function AsideListOcsHermanas({ oc }: AsideListOcsHermanasProps) {
  // Caso A: la OC es duplicada → ocOrigenId apunta al original.
  // Buscamos hermanas usando ocOrigenId (excluimos this).
  const ocOrigenIdDeBusqueda = oc.ocOrigenId ?? oc.id;
  const query = useOcsHermanasDuplicadas(ocOrigenIdDeBusqueda);

  const hermanas = (query.data?.hermanas ?? []).filter((h) => h.id !== oc.id);
  const esDuplicada = oc.ocOrigenId != null;
  const tieneHermanas = hermanas.length > 0;

  if (!esDuplicada && !tieneHermanas) {
    return null;
  }

  return (
    <aside
      className="rounded-md border bg-muted/30 p-3 text-sm"
      data-component="aside-list-ocs-hermanas"
    >
      <h3 className="mb-2 flex items-center gap-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">
        <GitFork className="h-3.5 w-3.5" />
        Cadena de duplicación
      </h3>

      {esDuplicada && oc.ocOrigenId && (
        <p className="mb-2">
          <span className="text-muted-foreground">Esta OC se duplicó de:</span>{' '}
          <Link
            to="/compras/ordenes/$id"
            params={{ id: oc.ocOrigenId }}
            className="inline-flex items-center gap-1 font-mono text-xs hover:underline"
            data-link="oc-origen"
          >
            OC origen
            <ExternalLink className="h-3 w-3" />
          </Link>
        </p>
      )}

      {tieneHermanas && (
        <div>
          <p className="mb-1 text-xs text-muted-foreground">
            {esDuplicada
              ? 'Otras OCs duplicadas del mismo origen:'
              : 'OCs duplicadas a partir de esta:'}
          </p>
          <ul className="space-y-1">
            {hermanas.map((h) => (
              <li key={h.id}>
                <Link
                  to="/compras/ordenes/$id"
                  params={{ id: h.id }}
                  className="inline-flex items-center gap-2 font-mono text-xs hover:underline"
                  data-link={`oc-hermana-${h.id}`}
                >
                  <span>{h.folio}</span>
                  <span className="text-[10px] uppercase tracking-wide text-muted-foreground">
                    {labelEstado(h.estado)}
                  </span>
                  <ExternalLink className="h-3 w-3" />
                </Link>
              </li>
            ))}
          </ul>
        </div>
      )}
    </aside>
  );
}

// Labels mínimas — coherente con EstadoBadge sin importar todo el módulo
// de domain-glossary (que ya las tiene). Para el aside list compacto
// basta con strings cortos.
function labelEstado(estado: number): string {
  const map: Record<number, string> = {
    0: 'Borrador',
    1: 'En autorización (N1)',
    2: 'En autorización (N2)',
    3: 'Autorizada',
    4: 'Rechazada',
    5: 'Cerrada',
    6: 'Cancelada',
  };
  return map[estado] ?? '?';
}
