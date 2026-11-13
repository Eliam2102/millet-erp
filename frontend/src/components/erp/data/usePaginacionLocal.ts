import { useId, useState } from 'react';
import { useTamanoPagina } from './useTamanoPagina';

/** Colección ya recibida; el total remoto y la carga incremental pertenecen al llamador. */
export function usePaginacionLocal<T>(items: readonly T[], filtros = '', inicial = 10) {
  const regionId = `tabla-${useId()}`;
  const tamano = useTamanoPagina('principal', 100, inicial, regionId, filtros);
  const { limit } = tamano;
  const [pagina, setPagina] = useState({ filtros, offset: 0, limit });
  if (pagina.filtros !== filtros || pagina.limit !== limit) {
    setPagina({ filtros, offset: 0, limit });
  }
  const offset =
    pagina.filtros === filtros && pagina.limit === limit
      ? Math.min(pagina.offset, Math.max(0, Math.ceil(items.length / limit) - 1) * limit)
      : 0;
  const onChange = (siguiente: number, nuevoLimit = limit) => {
    if (nuevoLimit !== limit) tamano.elegir(nuevoLimit);
    setPagina({
      filtros,
      offset: nuevoLimit === limit ? Math.max(0, siguiente) : 0,
      limit: nuevoLimit,
    });
  };
  return {
    visibles: items.slice(offset, offset + limit),
    offset,
    limit,
    onChange,
    cambiar: onChange,
    regionId,
  };
}
