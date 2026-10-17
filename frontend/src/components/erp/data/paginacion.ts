import { z } from 'zod';

/** Fragmento de search params de paginación (1-based) compartido por las bandejas de Almacén. */
export const PaginaSearchShape = {
  page: z.number().int().min(1).optional(),
  pageSize: z.number().int().min(1).max(200).optional(),
};

export const PAGE_SIZE_DEFAULT = 25;

/** Convierte <c>page/pageSize</c> de la URL en offset/limit de la API. */
export function paginaAOffsetLimit(s: { page?: number; pageSize?: number }) {
  const limit = s.pageSize ?? PAGE_SIZE_DEFAULT;
  return { offset: ((s.page ?? 1) - 1) * limit, limit };
}

/** Inverso: offset/limit de <c>PaginacionTabla</c> → <c>page/pageSize</c> (omite los defaults). */
export function offsetLimitAPagina(offset: number, limit: number) {
  const page = Math.floor(offset / limit) + 1;
  return {
    page: page === 1 ? undefined : page,
    pageSize: limit === PAGE_SIZE_DEFAULT ? undefined : limit,
  };
}
