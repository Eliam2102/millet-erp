/**
 * Etiquetas legibles para las tablas de conteo (captura P9 y aprobación
 * P10). Regla UX: nunca GUID en pantalla — se muestra la clave (y
 * descripción) que el backend enriquece vía read-ports (ADR-0042); si una
 * clave no resolvió, el fallback es el id truncado con el id completo en
 * el tooltip, mismo patrón que EditorLineas de Compras.
 */

export function EtiquetaArticulo({
  articuloId,
  clave,
  descripcion,
}: {
  articuloId: string;
  clave: string | null;
  descripcion: string | null;
}) {
  if (!clave) {
    return (
      <span className="font-mono text-xs text-muted-foreground" title={articuloId}>
        {articuloId.slice(0, 8)}…
      </span>
    );
  }
  return (
    <span title={descripcion ?? undefined}>
      <span className="font-mono text-xs">{clave}</span>
      {descripcion && (
        <span className="ml-1 text-xs text-muted-foreground">
          {descripcion.length > 40 ? `${descripcion.slice(0, 40)}…` : descripcion}
        </span>
      )}
    </span>
  );
}

export function EtiquetaSubAlmacen({
  subAlmacenId,
  clave,
}: {
  subAlmacenId: string;
  clave: string | null;
}) {
  if (!clave) {
    return (
      <span className="font-mono text-xs text-muted-foreground" title={subAlmacenId}>
        {subAlmacenId.slice(0, 8)}…
      </span>
    );
  }
  return <span className="font-mono text-xs">{clave}</span>;
}
