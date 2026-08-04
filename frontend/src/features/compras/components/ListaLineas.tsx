import { MoneyDisplay, DateTimeDisplay } from '@/components/erp';
import { CubrimientoBar } from '@/components/erp/display/CubrimientoBar';
import { EmptyState } from '@/components/erp/feedback/EmptyState';
import { formatCcMaquinaLabel } from '@/features/centros-costo/lib/cc-maquina-label';
import type { LineaResponse } from '@/features/compras/api/types';

/**
 * <c>&lt;ListaLineas/&gt;</c> — tabla de líneas dentro de la
 * pantalla de detalle (P3) read-only. UF2-PR3 trae el editor inline
 * para estados Borrador/Autorizada (matriz §6.1).
 *
 * <para>La columna de cubrimiento usa <c>&lt;CubrimientoBar/&gt;</c>
 * (UF5-PR1) — barra segmentada con 4 patrones distinguibles sin
 * depender de color, números visibles al lado y tooltip extendido en
 * desktop.</para>
 */
export interface ListaLineasProps {
  lineas: LineaResponse[];
  /** Mapa id → nombre para resolver artículos. Si no se provee, muestra el id. */
  resolverArticulo?: (articuloId: string) => string;
}

export function ListaLineas({ lineas, resolverArticulo }: ListaLineasProps) {
  if (lineas.length === 0) {
    return (
      <EmptyState
        title="Sin líneas registradas."
        description="Esta requisición fue creada sin líneas; el editor de UF2-PR3 permitirá agregarlas en estado Borrador."
      />
    );
  }

  return (
    <div className="overflow-x-auto rounded-md border bg-card">
      <table className="w-full min-w-[980px] text-sm">
        <thead className="bg-muted/50 text-xs uppercase tracking-wide text-muted-foreground">
          <tr>
            <th className="px-3 py-2 text-left font-medium">#</th>
            <th className="px-3 py-2 text-left font-medium">Artículo</th>
            <th className="px-3 py-2 text-left font-medium">CC-Máquina</th>
            <th className="px-3 py-2 text-right font-medium">Cant.</th>
            <th className="px-3 py-2 text-left font-medium">UM</th>
            <th className="px-3 py-2 text-right font-medium">Precio est.</th>
            <th className="px-3 py-2 text-left font-medium">Fecha req.</th>
            <th className="px-3 py-2 text-left font-medium">Cubrimiento</th>
          </tr>
        </thead>
        <tbody>
          {lineas
            .slice()
            .sort((a, b) => a.posicion - b.posicion)
            .map((l, i) => {
              // Preferir la etiqueta enriquecida del DTO (ADR-0042 addendum);
              // fallback al resolver client-side (legacy) y por último al id.
              const etiquetaDto = [l.articuloClave, l.articuloNombre]
                .filter(Boolean)
                .join(' · ');
              const articulo =
                etiquetaDto || resolverArticulo?.(l.articuloId) || l.articuloId;
              return (
                <tr
                  key={l.id}
                  className={i % 2 === 1 ? 'bg-muted/20' : undefined}
                >
                  <td className="px-3 py-2 text-muted-foreground">
                    {l.posicion}
                  </td>
                  <td className="px-3 py-2">
                    <div className="font-medium">{articulo}</div>
                    {l.notas && (
                      <p className="mt-1 text-xs text-muted-foreground">
                        {l.notas}
                      </p>
                    )}
                  </td>
                  <td className="px-3 py-2">
                    {/* CC-Máquina resuelto por el read-port (ADR-0050): "clave —
                        nombre", o "No catalogado" si el id no resuelve; "—" si la
                        línea no lleva CC. El display NO pasa por el selector. */}
                    {l.centroCostoId ? (
                      formatCcMaquinaLabel({
                        clave: l.centroCostoClave,
                        nombre: l.centroCostoNombre,
                      })
                    ) : (
                      <span className="text-muted-foreground">—</span>
                    )}
                  </td>
                  <td className="px-3 py-2 text-right tabular-nums">
                    {l.cantidad}
                  </td>
                  <td className="px-3 py-2">{l.unidadMedida}</td>
                  <td className="px-3 py-2 text-right">
                    <MoneyDisplay
                      amount={l.precioEstimadoMonto}
                      currency={l.precioEstimadoMoneda}
                    />
                  </td>
                  <td className="px-3 py-2">
                    <DateTimeDisplay value={l.fechaRequerida} />
                  </td>
                  <td className="px-3 py-2">
                    <CubrimientoBar
                      cantidad={l.cantidad}
                      cantDeAlmacen={l.cantDeAlmacen}
                      cantDeCompra={l.cantDeCompra}
                      cantRecibida={l.cantRecibida}
                      cantPendiente={l.cantPendiente}
                    />
                  </td>
                </tr>
              );
            })}
        </tbody>
      </table>
    </div>
  );
}
