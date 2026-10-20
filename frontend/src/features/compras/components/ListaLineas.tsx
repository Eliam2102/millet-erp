import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { DateTimeDisplay } from '@/components/erp';
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
        title="Sin partidas registradas."
        description="Esta requisición fue creada sin partidas; el editor de UF2-PR3 permitirá agregarlas en estado Borrador."
      />
    );
  }

  return (
    <div className="overflow-x-auto rounded-md border bg-surface-card">
      <Table className="w-full min-w-full text-sm">
        <TableHeader className="bg-surface-muted/50 text-xs uppercase tracking-wide text-ink-muted">
          <TableRow>
            <TableHead className="px-3 py-2 text-left font-medium">#</TableHead>
            <TableHead className="px-3 py-2 text-left font-medium">Artículo</TableHead>
            <TableHead className="px-3 py-2 text-left font-medium hidden @4xl:table-cell">
              Centro de costo
            </TableHead>
            <TableHead className="px-3 py-2 text-right font-medium">Cant.</TableHead>
            <TableHead className="px-3 py-2 text-left font-medium">UM</TableHead>
            <TableHead className="px-3 py-2 text-left font-medium hidden @4xl:table-cell">
              Fecha req.
            </TableHead>
            <TableHead className="px-3 py-2 text-left font-medium">Cubrimiento</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody className="[&_tr]:h-12">
          {lineas
            .slice()
            .sort((a, b) => a.posicion - b.posicion)
            .map((l, i) => {
              // Preferir la etiqueta enriquecida del DTO (ADR-0042 addendum);
              // fallback al resolver client-side (legacy) y por último al id.
              const etiquetaDto = [l.articuloClave, l.articuloNombre].filter(Boolean).join(' · ');
              const articulo = etiquetaDto || resolverArticulo?.(l.articuloId) || l.articuloId;
              return (
                <TableRow key={l.id} className={i % 2 === 1 ? 'bg-surface-muted/20' : undefined}>
                  <TableCell className="px-3 py-2 text-ink-muted">{l.posicion}</TableCell>
                  <TableCell className="px-3 py-2">
                    <div className="font-medium">{articulo}</div>
                    {l.notas && <p className="mt-1 text-xs text-ink-muted">{l.notas}</p>}
                  </TableCell>
                  <TableCell className="hidden px-3 py-2 @4xl:table-cell">
                    {/* Centro de costo resuelto por el read-port (ADR-0050): "clave —
                        nombre", o "No catalogado" si el id no resuelve; "—" si la
                        línea no lleva CC. El display NO pasa por el selector. */}
                    {l.centroCostoId ? (
                      formatCcMaquinaLabel({
                        clave: l.centroCostoClave,
                        nombre: l.centroCostoNombre,
                      })
                    ) : (
                      <span className="text-ink-muted">—</span>
                    )}
                  </TableCell>
                  <TableCell className="px-3 py-2 text-right tabular-nums">{l.cantidad}</TableCell>
                  <TableCell className="px-3 py-2">{l.unidadMedida}</TableCell>
                  <TableCell className="hidden px-3 py-2 @4xl:table-cell">
                    <DateTimeDisplay value={l.fechaRequerida} />
                  </TableCell>
                  <TableCell className="px-3 py-2">
                    <CubrimientoBar
                      cantidad={l.cantidad}
                      cantDeAlmacen={l.cantDeAlmacen}
                      cantDeCompra={l.cantDeCompra}
                      cantRecibida={l.cantRecibida}
                      cantPendiente={l.cantPendiente}
                    />
                    <ResumenParcialLinea linea={l} />
                  </TableCell>
                </TableRow>
              );
            })}
        </TableBody>
      </Table>
    </div>
  );
}

/**
 * Vista consolidada del parcial de una línea: lo que surte almacén, lo ya
 * entregado, lo comprometido en OC, lo recibido y lo que aún falta comprar.
 * Se oculta si el backend no envió el saldo (respuesta sin los campos nuevos).
 */
function ResumenParcialLinea({ linea }: { linea: LineaResponse }) {
  if (linea.cantSaldoPorComprar === undefined) return null;
  const texto = [
    `Almacén surte ${linea.cantDeAlmacen}`,
    `Entregado ${linea.cantEntregadoDeAlmacen ?? 0}`,
    `En OC ${linea.cantComprometidaEnOc ?? 0}`,
    `Recibido ${linea.cantRecibida}`,
    `Falta por comprar ${linea.cantSaldoPorComprar}`,
  ].join(' · ');
  return (
    <p className="mt-1 text-xs text-ink-muted" data-resumen-parcial="">
      {texto}
    </p>
  );
}
