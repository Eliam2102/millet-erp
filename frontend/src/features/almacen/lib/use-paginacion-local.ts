import { useState } from 'react';

/** Pagina solo los registros ya cargados; nunca modifica el alcance de la consulta. */
export function usePaginacionLocal<T>(items: readonly T[], filtros: string) {
  const [pagina, setPagina] = useState({ filtros, offset: 0 });
  const offset =
    pagina.filtros === filtros
      ? Math.min(pagina.offset, Math.max(0, Math.ceil(items.length / 10) - 1) * 10)
      : 0;
  return {
    visibles: items.slice(offset, offset + 10),
    offset,
    onChange: (siguiente: number) => setPagina({ filtros, offset: siguiente }),
  };
}
