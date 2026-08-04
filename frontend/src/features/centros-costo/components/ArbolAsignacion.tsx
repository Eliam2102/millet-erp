import { useState } from 'react';
import { ChevronRight } from 'lucide-react';
import { Checkbox } from '@/components/ui/checkbox';
import { cn } from '@/lib/utils';
import {
  NivelAlcance,
  TriEstado,
  type ArbolAsignacionResponse,
  type Dim1Asignacion,
  type Dim2Asignacion,
  type Dim3Asignacion,
  type GrupoDim2Asignacion,
  type GrupoDim3Asignacion,
  type MarcarAlcanceRequest,
} from '@/features/centros-costo/api/types';

/**
 * Árbol de ASIGNACIÓN de 5 niveles (FE-PR3) — Dim1 → GrupoDim2 → Dim2 →
 * GrupoDim3 → Dim3, con checkbox tri-estado por renglón. FULL-TREE: el
 * `arbol` ya trae los 5 niveles con el tri-estado calculado por el
 * backend; expandir es show/hide client-side (sin fetch). Hereda el
 * esqueleto visual tabla-en-árbol del ajuste (05 §7): grid de columnas
 * compartido header↔renglón, encabezados uppercase, dim1 con fondo,
 * sangría en la 1ª celda.
 *
 * <para>Convención del clic (05 §4.2, molde MatrizPermisos): el checkbox
 * del padre es COMPUTADO del `estado`; Radix normaliza indeterminate→true
 * al clic, así que <c>onCheckedChange(true)</c> = completar (marcar toda
 * la rama), <c>false</c> = limpiar → <c>asignar = checked === true</c>. La
 * UI NO re-deriva tri-estado: cada clic es un POST y el árbol se repinta
 * del refetch. Arranca colapsado a Dim1.</para>
 */

/** Nodo uniforme para render (aplana los 5 niveles del DTO). */
interface NodoRender {
  key: string;
  etiqueta: string;
  estado: TriEstado;
  dim3Vivas: number;
  dim3Asignadas: number;
  marca: Omit<MarcarAlcanceRequest, 'asignar'>;
  hijos: NodoRender[];
  /** Solo hojas Dim3: no expande, sin conteos. */
  esHoja: boolean;
}

const COLS = 'grid grid-cols-[minmax(0,1fr)_auto] items-center gap-3';

interface ArbolAsignacionProps {
  arbol: ArbolAsignacionResponse;
  onMarcar: (req: MarcarAlcanceRequest) => void;
  /** Deshabilita todos los checkboxes (alcance total o marcado en curso). */
  disabled?: boolean;
}

export function ArbolAsignacion({ arbol, onMarcar, disabled }: ArbolAsignacionProps) {
  const nodos = arbol.dim1s.map(dim1ANodo);

  return (
    <div className="overflow-x-auto rounded-md border">
      <div className="min-w-[520px]">
        <div
          aria-hidden="true"
          className={cn(
            COLS,
            'border-b bg-muted/50 px-3 py-2 text-xs font-medium uppercase tracking-wide text-muted-foreground',
          )}
        >
          <span>Alcance · Nombre</span>
          <span className="text-right">Asignadas</span>
        </div>

        <ul role="tree" className="text-sm">
          {nodos.map((n) => (
            <FilaNodo
              key={n.key}
              nodo={n}
              nivel={0}
              onMarcar={onMarcar}
              disabled={disabled}
            />
          ))}
        </ul>
      </div>
    </div>
  );
}

function FilaNodo({
  nodo,
  nivel,
  onMarcar,
  disabled,
}: {
  nodo: NodoRender;
  nivel: number;
  onMarcar: (req: MarcarAlcanceRequest) => void;
  disabled?: boolean;
}) {
  // Colapsado a Dim1 (nivel 0 abierto no; el usuario expande).
  const [open, setOpen] = useState(false);
  const expandible = !nodo.esHoja && nodo.hijos.length > 0;
  const esRaiz = nivel === 0;

  return (
    <li role="treeitem" aria-expanded={expandible ? open : undefined}>
      <div
        className={cn(
          COLS,
          'border-b px-3 py-1.5 hover:bg-muted/60',
          esRaiz && 'bg-muted/40 font-medium',
        )}
      >
        <div
          className="flex min-w-0 items-center gap-2"
          style={{ paddingLeft: nivel * 20 }}
        >
          <Checkbox
            checked={estadoAChecked(nodo.estado)}
            disabled={disabled}
            aria-label={`Alcance de ${nodo.etiqueta}`}
            onCheckedChange={(checked) =>
              onMarcar({ ...nodo.marca, asignar: checked === true })
            }
          />
          {expandible ? (
            <button
              type="button"
              className="flex h-5 w-5 shrink-0 items-center justify-center rounded hover:bg-muted"
              aria-label={`${open ? 'Colapsar' : 'Expandir'} ${nodo.etiqueta}`}
              onClick={() => setOpen((v) => !v)}
            >
              <ChevronRight
                className={cn('h-4 w-4 transition-transform', open && 'rotate-90')}
                aria-hidden="true"
              />
            </button>
          ) : (
            <span className="h-5 w-5 shrink-0" aria-hidden="true" />
          )}
          <span className="truncate">{nodo.etiqueta}</span>
        </div>

        <div className="flex justify-end text-xs">
          {!nodo.esHoja && <BadgeAsignadas nodo={nodo} />}
        </div>
      </div>

      {expandible && open && (
        <ul role="group">
          {nodo.hijos.map((h) => (
            <FilaNodo
              key={h.key}
              nodo={h}
              nivel={nivel + 1}
              onMarcar={onMarcar}
              disabled={disabled}
            />
          ))}
        </ul>
      )}
    </li>
  );
}

/** Todo→checked, Parcial→indeterminate, Ninguno→unchecked. */
function estadoAChecked(estado: TriEstado): boolean | 'indeterminate' {
  if (estado === TriEstado.Todo) return true;
  if (estado === TriEstado.Parcial) return 'indeterminate';
  return false;
}

/**
 * Columna ASIGNADAS con color semántico del tri-estado (05 §7.3): píldora
 * emerald (Todas) / amber (Parcial); Ninguna en texto gris SIN píldora
 * (la mayoría de los renglones cae ahí — el color señala señal, no ocupa
 * espacio). Píldora INLINE con las clases de la paleta de `EstadoBadge`,
 * NO el componente (acoplado a RQ/OC) ni tocando `components/ui/*` —
 * mismo patrón que la píldora "Inactivo" del §7.1. El `estado` ya viene
 * calculado en el DTO; cero re-derivación en cliente.
 */
function BadgeAsignadas({
  nodo,
}: {
  nodo: { estado: TriEstado; dim3Asignadas: number; dim3Vivas: number };
}) {
  if (nodo.estado === TriEstado.Todo) {
    return (
      <span className="rounded-full bg-emerald-100 px-2 py-0.5 font-medium text-emerald-900 ring-1 ring-inset ring-emerald-200">
        Todas · {nodo.dim3Asignadas}
      </span>
    );
  }
  if (nodo.estado === TriEstado.Parcial) {
    return (
      <span className="rounded-full bg-amber-100 px-2 py-0.5 font-medium text-amber-900 ring-1 ring-inset ring-amber-200">
        Parcial · {nodo.dim3Asignadas} de {nodo.dim3Vivas}
      </span>
    );
  }
  // Ninguna: texto gris, sin píldora.
  return (
    <span className="text-muted-foreground">
      Ninguna · {nodo.dim3Asignadas} de {nodo.dim3Vivas}
    </span>
  );
}

// ─── Aplanado del DTO anidado a NodoRender (los 5 niveles) ─────────────────

function dim1ANodo(d: Dim1Asignacion): NodoRender {
  return {
    key: `dim1-${d.id}`,
    etiqueta: `${d.clave} · ${d.nombre}`,
    estado: d.estado,
    dim3Vivas: d.dim3Vivas,
    dim3Asignadas: d.dim3Asignadas,
    marca: { nivel: NivelAlcance.Dim1, nodoId: d.id, grupoId: null },
    hijos: d.grupos.map((g) => grupoDim2ANodo(g, d.id)),
    esHoja: false,
  };
}

function grupoDim2ANodo(g: GrupoDim2Asignacion, dim1Id: string): NodoRender {
  return {
    key: `gd2-${g.id}`,
    etiqueta: g.nombre,
    estado: g.estado,
    dim3Vivas: g.dim3Vivas,
    dim3Asignadas: g.dim3Asignadas,
    // Grupo acotado al padre: nodoId = la Dim1 padre, grupoId = el grupo.
    marca: { nivel: NivelAlcance.GrupoDim2BajoDim1, nodoId: dim1Id, grupoId: g.id },
    hijos: g.dim2s.map(dim2ANodo),
    esHoja: false,
  };
}

function dim2ANodo(d: Dim2Asignacion): NodoRender {
  return {
    key: `dim2-${d.id}`,
    etiqueta: `${d.clave} · ${d.nombre}`,
    estado: d.estado,
    dim3Vivas: d.dim3Vivas,
    dim3Asignadas: d.dim3Asignadas,
    marca: { nivel: NivelAlcance.Dim2, nodoId: d.id, grupoId: null },
    hijos: d.grupos.map((g) => grupoDim3ANodo(g, d.id)),
    esHoja: false,
  };
}

function grupoDim3ANodo(g: GrupoDim3Asignacion, dim2Id: string): NodoRender {
  return {
    key: `gd3-${g.id}`,
    etiqueta: g.nombre,
    estado: g.estado,
    dim3Vivas: g.dim3Vivas,
    dim3Asignadas: g.dim3Asignadas,
    marca: { nivel: NivelAlcance.GrupoDim3BajoDim2, nodoId: dim2Id, grupoId: g.id },
    hijos: g.dim3s.map(dim3ANodo),
    esHoja: false,
  };
}

function dim3ANodo(d: Dim3Asignacion): NodoRender {
  return {
    key: `dim3-${d.id}`,
    etiqueta: `${d.clave} · ${d.nombre}`,
    // La hoja lleva `asignada` (bool), no `estado`: se deriva para el checkbox.
    estado: d.asignada ? TriEstado.Todo : TriEstado.Ninguno,
    dim3Vivas: 0,
    dim3Asignadas: 0,
    marca: { nivel: NivelAlcance.Dim3, nodoId: d.id, grupoId: null },
    hijos: [],
    esHoja: true,
  };
}
