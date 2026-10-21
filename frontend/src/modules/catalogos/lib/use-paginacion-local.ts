import { useState } from 'react';
/** Pagina solamente los registros ya recibidos; no modifica consultas remotas. */
export function usePaginacionLocal<T>(items: readonly T[], clave: string = '') {
  const [pagina, setPagina] = useState({ clave, offset: 0 });
  const offset =
    pagina.clave === clave
      ? Math.min(pagina.offset, Math.max(0, Math.ceil(items.length / 10) - 1) * 10)
      : 0;
  return {
    offset,
    visibles: items.slice(offset, offset + 10),
    onChange: (offset: number) => setPagina({ clave, offset }),
  };
}
