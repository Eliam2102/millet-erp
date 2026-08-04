import { z } from 'zod';
import { NivelReorden, ObjetivoReposicion } from '@/features/almacen/api/types';

/**
 * Schemas Zod del recurso "reabasto" (código = reorden, ADR-0047 PR5.A/5.F).
 * Reflejan el <c>CrearConfiguracionReordenValidator</c> del backend: montos
 * &gt;= 0 y máximo &gt;= mínimo. La unicidad de la llave (artículo/nivel/entidad),
 * la exclusión N1⊕N2 y la existencia de la asignación las decide el backend.
 */

/**
 * Regex laxo de UUID (8-4-4-4-12 hex) — los seeds del backend usan GUIDs
 * deterministas que no califican como v1-v8 estricto. Mismo patrón que
 * <c>almacen/schemas/almacen.ts</c>.
 */
const UUID_SHAPE_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Política común (min/máx/punto-reorden/bandera/objetivo). */
const politica = {
  minimo: z.number().min(0, 'No puede ser negativo'),
  maximo: z.number().min(0, 'No puede ser negativo'),
  puntoReorden: z.number().min(0, 'No puede ser negativo'),
  autoRequisicion: z.boolean(),
  objetivo: z.union([
    z.literal(ObjetivoReposicion.Minimo),
    z.literal(ObjetivoReposicion.Maximo),
    z.literal(ObjetivoReposicion.Reorden),
  ]),
};

const maximoGteMinimo = {
  check: (v: { minimo: number; maximo: number }) => v.maximo >= v.minimo,
  message: 'El máximo no puede ser menor que el mínimo.',
  path: ['maximo'] as const,
};

/**
 * Schema de <c>POST /api/v1/almacen/reorden</c>. Incluye la llave
 * artículo/nivel/entidad además de la política.
 */
export const CrearReordenSchema = z
  .object({
    articuloId: z.string().regex(UUID_SHAPE_RE, 'Artículo requerido'),
    nivel: z.union([
      z.literal(NivelReorden.Sucursal),
      z.literal(NivelReorden.Almacen),
    ]),
    entidadId: z.string().regex(UUID_SHAPE_RE, 'Entidad requerida'),
    ...politica,
  })
  .refine(maximoGteMinimo.check, {
    message: maximoGteMinimo.message,
    path: [...maximoGteMinimo.path],
  });

export type CrearReordenValues = z.infer<typeof CrearReordenSchema>;

/**
 * Schema de <c>PATCH /api/v1/almacen/reorden/{id}</c>. La llave es inmutable;
 * solo se edita la política (mirror de <c>EditarConfiguracionReordenCommand</c>).
 */
export const EditarReordenSchema = z
  .object(politica)
  .refine(maximoGteMinimo.check, {
    message: maximoGteMinimo.message,
    path: [...maximoGteMinimo.path],
  });

export type EditarReordenValues = z.infer<typeof EditarReordenSchema>;
