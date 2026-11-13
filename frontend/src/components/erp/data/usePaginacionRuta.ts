import { useTamanoPagina } from './useTamanoPagina';

/** Un enlace con cantidad explícita prevalece; sin ella restaura la preferencia de la vista. */
export function usePaginacionRuta(search: { page?: number; pageSize?: number }) {
  const tamano = useTamanoPagina();
  const limit = search.pageSize ?? tamano.limit;
  return { offset: ((search.page ?? 1) - 1) * limit, limit };
}
