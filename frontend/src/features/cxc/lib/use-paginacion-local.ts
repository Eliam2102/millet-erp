import { useState } from 'react';
/** Pagina únicamente el lote ya recibido; no cambia las consultas del módulo. */
export function usePaginacionLocal<T>(items: readonly T[], clave: string) {
  const [pagina, setPagina] = useState({ clave, offset: 0 });
  const offset =
    pagina.clave === clave
      ? Math.min(pagina.offset, Math.max(0, Math.ceil(items.length / 10) - 1) * 10)
      : 0;
  return {
    visibles: items.slice(offset, offset + 10),
    offset,
    cambiar: (valor: number) => setPagina({ clave, offset: valor }),
  };
}
