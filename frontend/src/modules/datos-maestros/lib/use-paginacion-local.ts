import { useState } from 'react';
/** Pagina solamente los registros ya recibidos; no modifica consultas remotas. */
export function usePaginacionLocal<T>(
  items: readonly T[],
  clave: string = '',
  limiteInicial: number = 10,
) {
  const [pagina, setPagina] = useState({ clave, offset: 0 });
  const [limit, setLimit] = useState(limiteInicial);
  const offset =
    pagina.clave === clave
      ? Math.min(pagina.offset, Math.max(0, Math.ceil(items.length / limit) - 1) * limit)
      : 0;
  return {
    offset,
    limit,
    visibles: items.slice(offset, offset + limit),
    /** Con `nuevoLimit` (selector de tamaño) cambia el tamaño; PaginacionTabla ya manda offset 0. */
    onChange: (offset: number, nuevoLimit?: number) => {
      if (nuevoLimit != null) setLimit(nuevoLimit);
      setPagina({ clave, offset });
    },
  };
}
