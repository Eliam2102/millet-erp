import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { comprasKeys } from '@/features/compras/api/keys';
import type { EstadoRequisicion } from '@/features/compras/api/types';
import type { CrearRequisicionValues } from '@/features/compras/schemas/crear-requisicion';

/**
 * Forma del comando que el wire HTTP recibe (mirror de
 * <c>CrearRequisicionCommand</c> backend). Extiende los valores que
 * el usuario edita en el form (<see cref="CrearRequisicionValues"/>)
 * con dos campos <b>derivados</b> que NO viven en la UI:
 *
 * <list>
 *   <item><c>sucursalCodigo</c>: clave del sucursal seleccionado
 *   (lookup desde <c>useSucursales</c>). El backend lo necesita para
 *   construir el folio (<c>{codigo}-{anio}-{secuencia}</c>) y valida
 *   formato <c>^[A-Z]{2,4}$</c>.</item>
 *   <item><c>folioAnio</c>: año actual del calendario civil. El
 *   backend lo valida entre 2000 y 2100.</item>
 * </list>
 *
 * <para>Separar el shape del form del shape del wire mantiene el form
 * limpio (el usuario no ve estos campos derivados) y permite
 * testar el comando completo independientemente de la UI.</para>
 */
export interface CrearRequisicionCommand extends CrearRequisicionValues {
  sucursalCodigo: string;
  folioAnio: number;
}

/**
 * Respuesta de <c>POST /api/v1/compras/requisiciones</c> (mirror de
 * <c>CrearRequisicionResponse</c> backend).
 */
export interface CrearRequisicionResponse {
  id: string;
  folio: string;
  folioAnio: number;
  estado: EstadoRequisicion;
  version: number;
}

export interface CrearRequisicionMutationArgs {
  /** Comando completo (form values + campos derivados). */
  command: CrearRequisicionCommand;
  /**
   * UUID v4 generado con <c>useFormIdempotencyKey()</c>. Estable
   * durante la vida del componente — ADR-0020.
   */
  idempotencyKey: string;
}

/**
 * <c>useCrearRequisicion()</c> — mutation hook para
 * <c>POST /api/v1/compras/requisiciones</c>. Doc 05 §7.6.
 *
 * <para>Comportamiento:</para>
 * <list>
 *   <item><b>Idempotency-Key obligatorio</b> (header ADR-0020): el
 *   backend lo enforce con <c>RequireIdempotencyKeyAttribute</c>.</item>
 *   <item>Tras éxito: invalida la lista (<c>comprasKeys.requisiciones</c>)
 *   para que la bandeja refresque al volver. NO redirige solitario;
 *   el caller (P4 page) hace <c>navigate(...)</c> al detalle con el
 *   <c>id</c> devuelto.</item>
 *   <item>Errores se propagan como <c>ApiError</c> tipado:
 *   <c>422</c> con campo errors → <c>applyServerErrors</c>; <c>403
 *   SELECCIONAR_REQUISITANTE_DENEGADO</c> → toast inline en el
 *   selector; <c>409 IDEMPOTENCY_IN_PROGRESS</c> ya lo reintenta el
 *   cliente HTTP automáticamente.</item>
 * </list>
 */
export function useCrearRequisicion() {
  const queryClient = useQueryClient();

  return useMutation<CrearRequisicionResponse, Error, CrearRequisicionMutationArgs>({
    mutationFn: async ({ command, idempotencyKey }) => {
      const { data } = await apiRequest<CrearRequisicionResponse>(
        '/api/v1/compras/requisiciones',
        {
          method: 'POST',
          body: command,
          idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      // Invalida cualquier query de listado de requisiciones (sin
      // importar filtros) para que la bandeja refresque cuando el
      // usuario vuelva. La cache del detalle nuevo se popula al
      // navegar a P3 (que dispara useRequisicion).
      queryClient.invalidateQueries({
        queryKey: comprasKeys.requisiciones(),
      });
    },
  });
}
