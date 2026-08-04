import { PackageSearch } from 'lucide-react';
import type { PreviewCubrimientoLinea } from '@/features/compras/api/types';

/**
 * <c>&lt;CubrimientoEstimadoPanel/&gt;</c> — panel informativo del cubrimiento
 * ESTIMADO por línea, mostrado en el detalle solo mientras la RQ está
 * <c>EnAutorizacion</c> (PR-C). Antes de autorizar, el <c>&lt;CubrimientoBar/&gt;</c>
 * real está en 0% (la bifurcación aún no corre), así que este panel le da al
 * autorizador una idea de si hay material para surtir.
 *
 * <para><b>Visual deliberadamente DISTINTO</b> del <c>CubrimientoBar</c> real
 * (borde punteado, fondo tenue, etiqueta explícita) para que NADIE confunda
 * una <b>estimación</b> con stock <b>reservado</b>: el preview no reserva nada
 * y el disponible es móvil hasta autorizar.</para>
 */
export interface CubrimientoEstimadoPanelProps {
  lineas: readonly PreviewCubrimientoLinea[];
  /** Mapa id → nombre para resolver artículos. Si no se provee, muestra el id. */
  resolverArticulo?: (articuloId: string) => string;
  isLoading?: boolean;
}

function fmt(n: number): string {
  return n % 1 === 0 ? String(n) : n.toFixed(2);
}

export function CubrimientoEstimadoPanel({
  lineas,
  resolverArticulo,
  isLoading = false,
}: CubrimientoEstimadoPanelProps) {
  return (
    <section
      data-cubrimiento-estimado=""
      aria-label="Disponibilidad estimada"
      className="rounded-md border border-dashed border-sky-300 bg-sky-50/40 p-3"
    >
      <header className="flex items-center gap-2 text-sm font-medium text-sky-900">
        <PackageSearch className="h-4 w-4" />
        Disponibilidad estimada
      </header>
      <p className="mt-0.5 text-xs text-muted-foreground">
        Estimación sujeta a disponibilidad hasta autorizar — no reserva stock.
      </p>

      {isLoading ? (
        <div className="mt-2 space-y-1.5" aria-hidden>
          <div className="h-4 w-full animate-pulse rounded bg-sky-100" />
          <div className="h-4 w-2/3 animate-pulse rounded bg-sky-100" />
        </div>
      ) : (
        <ul className="mt-2 space-y-1.5">
          {lineas.map((l) => {
            // Etiqueta enriquecida del DTO (ADR-0042) → fallback al resolver
            // (catálogo capado) → id. Mismo orden que <ListaLineas/>.
            const etiquetaDto = [l.articuloClave, l.articuloNombre]
              .filter(Boolean)
              .join(' · ');
            const articulo =
              etiquetaDto || resolverArticulo?.(l.articuloId) || l.articuloId;
            const total = Math.max(1, l.cantidad);
            const pctAlmacen = (l.estimadoDeAlmacen / total) * 100;
            const pctCompra = (l.estimadoDeCompra / total) * 100;
            return (
              <li
                key={l.lineaId}
                data-linea-estimada={l.lineaId}
                className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs"
              >
                <span className="min-w-0 flex-1 truncate text-foreground">
                  {articulo}
                </span>
                {/* Barra estimada — punteada, distinta del CubrimientoBar real. */}
                <span
                  role="img"
                  aria-label={`Estimado: ${fmt(l.estimadoDeAlmacen)} disponible, ${fmt(l.estimadoDeCompra)} a comprar, de ${fmt(l.cantidad)}`}
                  className="relative inline-flex h-2.5 w-28 overflow-hidden rounded border border-dashed border-sky-400 bg-white"
                >
                  {pctAlmacen > 0 && (
                    <span
                      className="h-full bg-emerald-400/70"
                      style={{ width: `${pctAlmacen}%` }}
                    />
                  )}
                  {pctCompra > 0 && (
                    <span
                      className="h-full bg-amber-300/70"
                      style={{
                        width: `${pctCompra}%`,
                        backgroundImage:
                          'repeating-linear-gradient(45deg, rgba(0,0,0,0.18) 0 3px, transparent 3px 6px)',
                      }}
                    />
                  )}
                </span>
                <span className="font-mono text-muted-foreground">
                  {fmt(l.estimadoDeAlmacen)} disp · {fmt(l.estimadoDeCompra)} a comprar
                </span>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
