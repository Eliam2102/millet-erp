import { useState } from 'react';
import { useTamanoPagina } from '@/components/erp/data/useTamanoPagina';

/**
 * Offset de una lista paginada contra el servidor: vuelve a 0 cuando cambia
 * cualquier filtro (<c>filtros</c> es su huella serializada).
 */
export function usePaginacionServidor(filtros: string) {
  const { limit } = useTamanoPagina();
  const [pagina, setPagina] = useState({ filtros, offset: 0, limit });
  if (pagina.filtros !== filtros || pagina.limit !== limit) {
    setPagina({ filtros, offset: 0, limit });
  }
  return {
    offset: pagina.filtros === filtros && pagina.limit === limit ? pagina.offset : 0,
    onChange: (siguiente: number) => setPagina({ filtros, offset: siguiente, limit }),
  };
}
