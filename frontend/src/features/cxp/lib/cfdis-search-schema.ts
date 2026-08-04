import { z } from 'zod';
import {
  EstadoCfdiRecibido,
  TipoCfdi,
} from '@/features/cxp/api/types';

/**
 * Schema de los <c>search params</c> de la bandeja de CFDIs
 * (FE-F1-PR1). TanStack Router lo usa en <c>validateSearch</c> para
 * filtrar valores inválidos y aplicar defaults.
 *
 * <para>Persistir filtros en la URL permite que el breadcrumb de
 * cualquier acción (Descartar, Marcar duplicado) regrese a la bandeja
 * con los mismos filtros aplicados.</para>
 *
 * <para>Filtro alterno <c>soloVencidos</c>: muestra solo CFDIs en
 * <c>PorProcesar</c> con <c>fechaRecepcion</c> &gt; 5 días (cálculo
 * client-side sobre la página actual; ver doc 07 §FE-F1-PR1).</para>
 */
export const CfdisSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoCfdiRecibido.PorProcesar),
      z.literal(EstadoCfdiRecibido.ConvertidoEnPasivo),
      z.literal(EstadoCfdiRecibido.Duplicado),
      z.literal(EstadoCfdiRecibido.Descartado),
    ])
    .optional(),
  tipo: z
    .union([
      z.literal(TipoCfdi.Ingreso),
      z.literal(TipoCfdi.Egreso),
      z.literal(TipoCfdi.Pago),
      z.literal(TipoCfdi.Traslado),
      z.literal(TipoCfdi.Nomina),
    ])
    .optional(),
  rfcEmisor: z
    .string()
    .trim()
    .min(3)
    .max(13)
    .regex(/^[A-ZÑ&]{3,4}\d{6}[A-Z\d]{3}$/i)
    .optional(),
  q: z.string().min(1).max(200).optional(),
  soloVencidos: z.boolean().optional(),
});

export type CfdisSearch = z.infer<typeof CfdisSearchSchema>;

export const DEFAULT_CFDIS_SEARCH: CfdisSearch = {};
