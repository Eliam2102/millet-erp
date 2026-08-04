import { ArrowLeft, ArrowRight, ArrowDown } from 'lucide-react';
import { NodoDocumento } from '@/components/erp/trazabilidad/NodoDocumento';
import {
  type NodoArbolDocumento,
  type TipoDocumentoTrazabilidad,
  tipoDocumentoLabel,
} from '@/components/erp/trazabilidad/types';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;ArbolDocumentos/&gt;</c> — vista bidireccional de la cadena
 * de trazabilidad cross-módulo (UF7-PR2, FOC6).
 *
 * <para><b>Layout</b>:</para>
 * <list>
 *   <item>Desktop (lg+): horizontal — ascendentes ← actual → descendentes,
 *   con flechas conectoras.</item>
 *   <item>Mobile (&lt; lg): vertical — ascendentes apilados arriba,
 *   actual al centro destacado, descendentes apilados abajo, con
 *   flechas verticales.</item>
 * </list>
 *
 * <para>Layout simple con CSS grid + flex (sin SVG/d3 por simplicidad).
 * Si la cadena tiene &gt; 5 nodos por lado, scroll horizontal/vertical
 * mantiene la vista usable. Para cadenas extremadamente largas (raras
 * en producción), considerar paginación o visualización compacta.</para>
 *
 * <para><b>API parametrizada</b> (cross-módulo): el componente NO sabe
 * qué módulo origina el árbol — recibe el nodo raíz ya estructurado
 * desde el caller. CxP/Recepción/Tesorería implementarán sus wrappers
 * con su propio hook que apunta al endpoint común
 * <c>/api/v1/compras/trazabilidad/arbol-documentos</c>.</para>
 */
export interface ArbolDocumentosProps {
  /** Nodo raíz del árbol (típicamente la entidad del wrapper actual). */
  raiz: NodoArbolDocumento;
  /** Tipo del documento "actual" para destacar visualmente. */
  tipoActual: TipoDocumentoTrazabilidad;
  /** Id del documento "actual" (para highlight). */
  idActual: string;
  className?: string;
}

export function ArbolDocumentos({
  raiz,
  tipoActual,
  idActual,
  className,
}: ArbolDocumentosProps) {
  const ascendentes = raiz.ascendentes ?? [];
  const descendentes = raiz.descendentes ?? [];

  function esActual(nodo: NodoArbolDocumento): boolean {
    return nodo.tipoDocumento === tipoActual && nodo.id === idActual;
  }

  // Layout horizontal en lg+, vertical en sm.
  // Estructura: 3 columnas (asc | raiz | desc) en horizontal,
  // 3 filas en vertical.
  return (
    <div
      className={cn('w-full', className)}
      data-component="arbol-documentos"
      role="tree"
      aria-label="Árbol de trazabilidad de documentos"
    >
      <div className="flex flex-col items-stretch gap-3 lg:flex-row lg:items-start lg:justify-center lg:overflow-x-auto">
        {/* Ascendentes */}
        {ascendentes.length > 0 && (
          <Columna
            titulo="Origen"
            nodos={ascendentes}
            esActual={esActual}
            position="izquierda"
          />
        )}

        {/* Conector */}
        {ascendentes.length > 0 && <Flecha direccion="der" />}

        {/* Nodo raíz (actual) */}
        <div className="flex flex-col items-center">
          <span className="mb-2 hidden text-xs font-medium uppercase tracking-wide text-primary lg:block">
            Actual
          </span>
          <NodoDocumento nodo={raiz} esActual />
        </div>

        {/* Conector */}
        {descendentes.length > 0 && <Flecha direccion="der" />}

        {/* Descendentes */}
        {descendentes.length > 0 && (
          <Columna
            titulo="Derivados"
            nodos={descendentes}
            esActual={esActual}
            position="derecha"
          />
        )}
      </div>

      {/* Estado vacío */}
      {ascendentes.length === 0 && descendentes.length === 0 && (
        <p className="mt-3 text-center text-sm text-muted-foreground">
          Esta {tipoDocumentoLabel(tipoActual).toLowerCase()} no tiene
          documentos relacionados aún.
        </p>
      )}
    </div>
  );
}

function Columna({
  titulo,
  nodos,
  esActual,
  position,
}: {
  titulo: string;
  nodos: NodoArbolDocumento[];
  esActual: (n: NodoArbolDocumento) => boolean;
  position: 'izquierda' | 'derecha';
}) {
  return (
    <div className="flex flex-col items-center gap-2">
      <span
        className={cn(
          'text-xs font-medium uppercase tracking-wide text-muted-foreground',
          position === 'izquierda' ? 'self-end lg:self-center' : 'self-start lg:self-center',
        )}
      >
        {titulo} ({nodos.length})
      </span>
      <div className="flex flex-col gap-2">
        {nodos.map((n) => (
          <NodoDocumento key={`${n.tipoDocumento}-${n.id}`} nodo={n} esActual={esActual(n)} />
        ))}
      </div>
    </div>
  );
}

function Flecha({ direccion }: { direccion: 'der' | 'abajo' }) {
  // En desktop: muestra flecha horizontal (←/→). En mobile: vertical.
  return (
    <div
      className="flex shrink-0 items-center justify-center text-muted-foreground"
      aria-hidden="true"
    >
      {/* Mobile: vertical */}
      <ArrowDown className="h-5 w-5 lg:hidden" />
      {/* Desktop: horizontal */}
      {direccion === 'der' ? (
        <ArrowRight className="hidden h-5 w-5 self-center lg:inline" />
      ) : (
        <ArrowLeft className="hidden h-5 w-5 self-center lg:inline" />
      )}
    </div>
  );
}
