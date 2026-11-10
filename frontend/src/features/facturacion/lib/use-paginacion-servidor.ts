import { useState } from 'react';

/**
 * Offset de una lista paginada contra el servidor: vuelve a 0 cuando cambia
 * cualquier filtro (<c>filtros</c> es su huella serializada).
 */
export function usePaginacionServidor(filtros: string) {
  const [pagina, setPagina] = useState({ filtros, offset: 0 });
  return {
    offset: pagina.filtros === filtros ? pagina.offset : 0,
    onChange: (siguiente: number) => setPagina({ filtros, offset: siguiente }),
  };
}
