import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import {
  EstadoOrdenCompra,
  type EstadoOrdenCompra as EstadoOrdenCompraType,
} from '@/features/compras/ordenes/api/types';
import type { CrearOrdenCompraVaciaValues } from '@/features/compras/ordenes/schemas/crear-oc-vacia';

/**
 * Forma del comando que el wire HTTP recibe (mirror de
 * <c>CrearOrdenCompraVaciaCommand</c> backend). Extiende los valores
 * que el usuario edita en el form
 * (<see cref="CrearOrdenCompraVaciaValues"/>) con dos campos
 * <b>derivados</b> que NO viven en la UI:
 *
 * <list>
 *   <item><c>sucursalCodigo</c>: clave del sucursal seleccionado
 *   (lookup desde <c>useSucursales</c>). El backend lo necesita para
 *   construir el folio (<c>OC-{sucursalCodigo}{folioAnio}-{secuencia}</c>)
 *   y valida formato <c>^[A-Z]{2,4}$</c>.</item>
 *   <item><c>folioAnio</c>: año actual del calendario civil. El
 *   backend lo valida entre 2000 y 2100.</item>
 * </list>
 *
 * <para>Mantener el shape del wire separado del shape del form sigue
 * la convención establecida por <c>CrearRequisicionCommand</c> (RQ).</para>
 */
export interface CrearOrdenCompraVaciaCommand
  extends CrearOrdenCompraVaciaValues {
  sucursalCodigo: string;
  folioAnio: number;
}

/**
 * Respuesta de <c>POST /api/v1/compras/ordenes</c> (mirror de
 * <c>CrearOrdenCompraVaciaResponse</c> backend).
 */
export interface CrearOrdenCompraVaciaResponse {
  id: string;
  folio: string;
  folioAnio: number;
  estado: EstadoOrdenCompraType;
  version: number;
}

export interface CrearOrdenCompraVaciaMutationArgs {
  /** Comando completo (form values + campos derivados). */
  command: CrearOrdenCompraVaciaCommand;
  /**
   * UUID v4 generado con <c>useFormIdempotencyKey()</c>. Estable
   * durante la vida del componente — ADR-0020.
   */
  idempotencyKey: string;
}

/**
 * <c>useCrearOrdenCompraVacia()</c> — mutation hook para
 * <c>POST /api/v1/compras/ordenes</c>. Cubre dos modos del Sheet
 * "Nueva OC" (FOC3): modo "vacía sin RQ" y modo "sin RQ previa"
 * (FOC11). El modo "1:1 desde RQ" usa
 * <c>useCrearOrdenCompraDesdeRequisicion</c>.
 *
 * <para>Comportamiento:</para>
 * <list>
 *   <item><b>Idempotency-Key obligatorio</b> (header ADR-0020): el
 *   backend lo enforce con <c>RequireIdempotencyKeyAttribute</c>.</item>
 *   <item>Tras éxito: invalida <c>ordenesKeys.all</c> para que la
 *   bandeja refresque al volver. NO redirige solo; el caller (Sheet
 *   page) hace <c>navigate(...)</c> al detalle con el <c>id</c>
 *   devuelto.</item>
 *   <item>Errores se propagan como <c>ApiError</c> tipado:
 *   <c>422</c> con campo errors → <c>applyServerErrors</c> en el
 *   form;
 *   <c>403 CREAR_SIN_RQ_DENEGADO</c> → toast inline (esto se debería
 *   prevenir con el gate de UI del toggle, pero defensa en profundidad);
 *   <c>409 IDEMPOTENCY_IN_PROGRESS</c> ya lo reintenta el cliente
 *   HTTP automáticamente.</item>
 * </list>
 */
export function useCrearOrdenCompraVacia() {
  const queryClient = useQueryClient();
  // Asegura que el import de EstadoOrdenCompra no se sacuda como dead code:
  // los consumidores acceden al enum via la respuesta tipada.
  void EstadoOrdenCompra;

  return useMutation<
    CrearOrdenCompraVaciaResponse,
    Error,
    CrearOrdenCompraVaciaMutationArgs
  >({
    mutationFn: async ({ command, idempotencyKey }) => {
      const { data } = await apiRequest<CrearOrdenCompraVaciaResponse>(
        '/api/v1/compras/ordenes',
        {
          method: 'POST',
          body: command,
          idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ordenesKeys.all,
      });
    },
  });
}
