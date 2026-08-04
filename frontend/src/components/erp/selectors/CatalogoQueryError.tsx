import { mensajeErrorCatalogo } from './catalogo-error';

/**
 * Estado de error para la lista de un selector/picker. Sin esto, un
 * fallo de la query (típicamente 403 por permiso faltante) se veía como
 * "sin resultados" y nadie sospechaba del permiso.
 */
export function CatalogoQueryError({ error }: { error: unknown }) {
  return (
    <div className="px-4 py-6 text-sm text-muted-foreground" role="alert">
      {mensajeErrorCatalogo(error)}
    </div>
  );
}
