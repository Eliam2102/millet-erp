import { z } from 'zod';
import { EstadoMovimiento } from '@/features/almacen/api/types';

const DATE_ONLY_RE = /^\d{4}-\d{2}-\d{2}$/;

/**
 * Schema de los <c>search params</c> de la bandeja de Recepciones
 * (FE-F2-PR1). TanStack Router lo usa en <c>validateSearch</c> para
 * filtrar valores inválidos y aplicar defaults.
 *
 * <para>Persistir filtros en la URL permite que el breadcrumb del
 * detalle (cuando exista) regrese a la bandeja con los mismos filtros
 * (mismo patrón que doc 05 §13.9 de Compras).</para>
 */
export const RecepcionesSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoMovimiento.Borrador),
      z.literal(EstadoMovimiento.Validado),
      z.literal(EstadoMovimiento.Registrado),
      z.literal(EstadoMovimiento.Cancelado),
    ])
    .optional(),
  subAlmacenId: z.string().min(1).optional(),
  ordenCompraId: z.string().min(1).optional(),
  desde: z.string().regex(DATE_ONLY_RE).optional(),
  hasta: z.string().regex(DATE_ONLY_RE).optional(),
  q: z.string().min(1).max(200).optional(),
});

export type RecepcionesSearch = z.infer<typeof RecepcionesSearchSchema>;

export const DEFAULT_RECEPCIONES_SEARCH: RecepcionesSearch = {};
