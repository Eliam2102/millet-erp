import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { facturacionKeys } from './keys';
import type { DatosCliente } from '../lib/datos-cliente';

export function useEditarDatosCliente() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, version, datos }: { id: string; version: number; datos: DatosCliente }) => {
      const { data } = await apiRequest<{ version: number }>(`/api/v1/facturacion/facturas/${id}/datos-cliente`, {
        method: 'PUT', ifMatch: String(version), body: datos,
      });
      return data;
    },
    onSuccess: (_data, { id }) => client.invalidateQueries({ queryKey: facturacionKeys.facturaById(id) }),
  });
}
