import { ESTADOS_CATALOGO } from '../lib/estados-ui';
import { useState } from 'react';
import { ChevronRight, MoreHorizontal } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { cn } from '@/lib/utils';
import { useHijosJerarquiaCentrosCosto } from '@/features/centros-costo/api/useJerarquiaCentrosCosto';
import {
  EstatusCatalogo,
  type NodoCeCo,
  type NodoTipoJerarquia,
} from '@/features/centros-costo/api/types';
import { etiquetaNivel, etiquetaTipoNodo } from '@/features/centros-costo/lib/etiquetas';

/**
 * Árbol de CONFIGURACIÓN del catálogo (05 §4.1, §7) — 3 niveles con grupos
 * como CHIP. Copia estructural de ArbolSaldos (lazy por nodo,
 * skeleton/error/empty por nivel, auto-expand de cadena única) con el
 * lenguaje visual de tabla del ERP (molde BandejaRequisiciones):
 * encabezados uppercase/gris, columnas alineadas, dim1 con fondo, zebra,
 * clave en font-mono. La alineación se logra con un GRID de columnas
 * compartido entre el encabezado y cada renglón — la sangría vive solo
 * dentro de la primera celda, así Grupo/Contenido/Acciones caen rectos a
 * cualquier profundidad (05 §7.4: tabla-en-árbol, no maestro-detalle).
 *
 * Roles ARIA de árbol (tree/treeitem/group) — es una jerarquía, no una
 * tabla de datos; el encabezado visual va aria-hidden (cada treeitem lleva
 * su texto accesible). Etiquetas SIEMPRE del helper único (07 §0). El
 * color semántico de estado (tri-estado) es de FE-PR3; aquí los badges son
 * chip de grupo + "Inactivo" slate.
 */

/** Plantilla de columnas: Clave·Nombre | Grupo | Contenido | Acciones. */
const COLS =
  'grid grid-cols-[minmax(0,1fr)_minmax(5rem,11rem)_minmax(6rem,13rem)_3rem] items-center gap-3';

export type AccionNodo = 'editar' | 'crear-hijo' | 'desactivar' | 'reactivar';

interface ArbolCentrosCostoProps {
  incluirInactivos?: boolean;
  /** Ids de nodos que deben abrirse (expansión de rama post-búsqueda). */
  expandidosForzados?: ReadonlySet<string>;
  /** Habilita el menú de acciones por renglón (catalogo.administrar). */
  puedeAdministrar?: boolean;
  onAccion?: (accion: AccionNodo, nodo: NodoCeCo) => void;
}

export function ArbolCentrosCosto({
  incluirInactivos,
  expandidosForzados,
  puedeAdministrar,
  onAccion,
}: ArbolCentrosCostoProps) {
  return (
    <div className="overflow-x-auto rounded-lg bg-surface-card shadow-card-flat">
      <div className="min-w-160">
        {/* Encabezado de columnas (uppercase/gris) — decorativo: los
            treeitem cargan su propio texto accesible. */}
        <div
          aria-hidden="true"
          className={cn(
            COLS,
            'border-b border-line-divider bg-surface-subtle px-3 py-2 text-2xs font-semibold uppercase tracking-wide text-ink-muted',
          )}
        >
          <span>Clave · Nombre</span>
          <span>Grupo</span>
          <span>Contenido</span>
          <span className="text-right">Acciones</span>
        </div>

        <ul role="tree" className="text-sm">
          <ListaHijos
            nodoTipo="raiz"
            nivel={0}
            incluirInactivos={incluirInactivos}
            expandidosForzados={expandidosForzados}
            puedeAdministrar={puedeAdministrar}
            onAccion={onAccion}
          />
        </ul>
      </div>
    </div>
  );
}

interface ListaHijosProps extends ArbolCentrosCostoProps {
  nodoTipo: NodoTipoJerarquia;
  nodoId?: string;
  nivel: number;
}

/** Los hijos de un nodo: una llamada lazy; skeleton/error/empty por nivel. */
function ListaHijos({
  nodoTipo,
  nodoId,
  nivel,
  incluirInactivos,
  expandidosForzados,
  puedeAdministrar,
  onAccion,
}: ListaHijosProps) {
  const query = useHijosJerarquiaCentrosCosto({
    nodoTipo,
    nodoId,
    incluirInactivos,
  });

  const sangria = { paddingLeft: nivel * 20 + 12 };

  if (query.isLoading) {
    return (
      <li role="none" aria-hidden="true">
        <div className="space-y-1 py-1.5" style={sangria} data-testid="nodo-skeleton">
          <div className="h-5 w-2/3 animate-pulse rounded bg-surface-muted" />
          <div className="h-5 w-1/2 animate-pulse rounded bg-surface-muted" />
        </div>
      </li>
    );
  }

  if (query.isError) {
    return (
      <li role="none">
        <div className="flex items-center gap-2 py-1.5 text-danger-fg" style={sangria}>
          <span>No se pudo cargar este nivel.</span>
          <Button variant="ghost" size="sm" onClick={() => query.refetch()}>
            Reintentar
          </Button>
        </div>
      </li>
    );
  }

  const hijos = query.data ?? [];
  if (hijos.length === 0) {
    return (
      <li role="none">
        <p
          className="py-2 text-ink-muted"
          style={sangria}
          data-testid={nivel === 0 ? 'arbol-vacio' : undefined}
        >
          {nivel === 0
            ? 'El catálogo está vacío. Contacta al administrador para cargar los centros de costo.'
            : 'Sin elementos en este nivel.'}
        </p>
      </li>
    );
  }

  // Cadena única: un solo hijo no-hoja se abre solo (misma llamada lazy).
  const autoExpandir = hijos.length === 1 && !hijos[0].esHoja;

  return (
    <>
      {hijos.map((nodo, i) => (
        <FilaNodo
          key={`${nodo.tipo}-${nodo.id}`}
          nodo={nodo}
          nivel={nivel}
          impar={i % 2 === 1}
          autoExpandir={autoExpandir}
          incluirInactivos={incluirInactivos}
          expandidosForzados={expandidosForzados}
          puedeAdministrar={puedeAdministrar}
          onAccion={onAccion}
        />
      ))}
    </>
  );
}

interface FilaNodoProps extends ArbolCentrosCostoProps {
  nodo: NodoCeCo;
  nivel: number;
  /** Posición impar dentro de su grupo de hermanos (zebra). */
  impar: boolean;
  autoExpandir: boolean;
}

function FilaNodo({
  nodo,
  nivel,
  impar,
  autoExpandir,
  incluirInactivos,
  expandidosForzados,
  puedeAdministrar,
  onAccion,
}: FilaNodoProps) {
  const forzado = expandidosForzados?.has(nodo.id) ?? false;
  const [open, setOpen] = useState(autoExpandir || forzado);
  const expandible = !nodo.esHoja;
  const inactivo = nodo.estatus === EstatusCatalogo.Inactivo;
  const esRaiz = nodo.tipo === 'dim1';
  const etiqueta = etiquetaTipoNodo(nodo.tipo, 'configuracion');

  // La búsqueda fuerza la apertura de la rama del resultado — patrón de
  // estado derivado DURANTE render (no un effect: la regla react-hooks
  // del repo prohíbe setState-in-effect y React lo documenta como
  // "adjusting state when props change").
  const [prevForzado, setPrevForzado] = useState(forzado);
  if (forzado !== prevForzado) {
    setPrevForzado(forzado);
    if (forzado) setOpen(true);
  }

  const etiquetaHijo =
    nodo.tipo === 'dim1'
      ? etiquetaNivel('dim2', 'configuracion')
      : nodo.tipo === 'dim2'
        ? etiquetaNivel('dim3', 'configuracion')
        : null;

  return (
    <li role="treeitem" aria-expanded={expandible ? open : undefined}>
      <div
        className={cn(
          COLS,
          'min-h-12 border-b border-line-row px-3 py-1.5 hover:bg-surface-muted/60',
          // Raíz (dim1) con fondo y peso; el resto zebra por hermanos.
          esRaiz ? 'bg-surface-muted/40 font-medium' : impar && 'bg-surface-muted/20',
          inactivo && 'text-ink-muted',
        )}
      >
        {/* Celda 1 — Clave · Nombre (con chevron y sangría interna). */}
        <div className="flex min-w-0 items-center gap-2" style={{ paddingLeft: nivel * 20 }}>
          {expandible ? (
            <button
              type="button"
              className="flex h-5 w-5 shrink-0 items-center justify-center rounded hover:bg-surface-muted"
              aria-label={`${open ? 'Colapsar' : 'Expandir'} ${nodo.clave}`}
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
          <span className="shrink-0 font-mono text-xs" title={etiqueta}>
            {nodo.clave}
          </span>
          <span className="truncate text-ink-muted">{nodo.nombre}</span>
          {nodo.estatus !== EstatusCatalogo.Activo && (
            <Badge variant={ESTADOS_CATALOGO[nodo.estatus].tono} className="shrink-0">
              {ESTADOS_CATALOGO[nodo.estatus].texto}
            </Badge>
          )}
        </div>

        {/* Celda 2 — Grupo (chip). */}
        <div className="min-w-0">
          {nodo.grupo && (
            <Badge variant="neutral" className="max-w-full truncate text-2xs">
              {nodo.grupo}
            </Badge>
          )}
        </div>

        {/* Celda 3 — Contenido (conteos de vivos). */}
        <div className="truncate text-xs text-ink-muted">
          {!nodo.esHoja && resumenConteos(nodo)}
        </div>

        {/* Celda 4 — Acciones. */}
        <div className="flex justify-end">
          {puedeAdministrar && onAccion && (
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button
                  variant="ghost"
                  size="sm"
                  className="h-6 w-6 shrink-0 p-0"
                  aria-label={`Acciones de ${nodo.clave}`}
                >
                  <MoreHorizontal className="h-4 w-4" aria-hidden="true" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                {etiquetaHijo && !inactivo && (
                  <DropdownMenuItem onSelect={() => onAccion('crear-hijo', nodo)}>
                    Nueva {etiquetaHijo} aquí
                  </DropdownMenuItem>
                )}
                <DropdownMenuItem onSelect={() => onAccion('editar', nodo)}>
                  Editar
                </DropdownMenuItem>
                {inactivo ? (
                  <DropdownMenuItem onSelect={() => onAccion('reactivar', nodo)}>
                    Reactivar
                  </DropdownMenuItem>
                ) : (
                  <DropdownMenuItem
                    className="text-danger-fg focus:text-danger-fg"
                    onSelect={() => onAccion('desactivar', nodo)}
                  >
                    Desactivar
                  </DropdownMenuItem>
                )}
              </DropdownMenuContent>
            </DropdownMenu>
          )}
        </div>
      </div>

      {expandible && open && (
        <ul role="group">
          <ListaHijos
            // El tipo del hijo ES el nivel a expandir (dim3 nunca llega
            // aquí: es hoja).
            nodoTipo={nodo.tipo as NodoTipoJerarquia}
            nodoId={nodo.id}
            nivel={nivel + 1}
            incluirInactivos={incluirInactivos}
            expandidosForzados={expandidosForzados}
            puedeAdministrar={puedeAdministrar}
            onAccion={onAccion}
          />
        </ul>
      )}
    </li>
  );
}

/**
 * Conteos de descendientes VIVOS (los calcula el backend con el predicado
 * de la cascada ADR-0049). Vocabulario vía el helper único.
 */
function resumenConteos(nodo: NodoCeCo): string {
  const dim3 = `${nodo.dim3Vivas} × ${etiquetaTipoNodo('dim3', 'configuracion')}`;
  if (nodo.tipo === 'dim1') {
    return `${nodo.dim2Vivas} × ${etiquetaTipoNodo('dim2', 'configuracion')} · ${dim3}`;
  }
  return dim3;
}
