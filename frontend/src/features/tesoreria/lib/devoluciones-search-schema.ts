import { z } from 'zod';
import { PaginaSearchShape } from '@/components/erp/data/paginacion';
import { EstadoDevolucionCliente, OrigenDevolucionCliente } from '@/features/tesoreria/api/types';

export const DevolucionesSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoDevolucionCliente.Solicitada),
      z.literal(EstadoDevolucionCliente.Autorizada),
      z.literal(EstadoDevolucionCliente.Rechazada),
      z.literal(EstadoDevolucionCliente.Pagada),
      z.literal(EstadoDevolucionCliente.Cancelada),
    ])
    .optional(),
  origen: z
    .union([z.literal(OrigenDevolucionCliente.DepositoErroneo), z.literal(OrigenDevolucionCliente.NotaCredito)])
    .optional(),
  ...PaginaSearchShape,
});

export type DevolucionesSearch = z.infer<typeof DevolucionesSearchSchema>;
