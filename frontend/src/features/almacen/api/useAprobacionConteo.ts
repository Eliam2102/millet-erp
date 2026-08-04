import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { almacenKeys } from '@/features/almacen/api/keys';
import type {
  AgregarRecuentoCommand,
  AgregarRecuentoResponse,
  AplicarConteoResponse,
  AprobarLineaIndividualmenteCommand,
  EvaluarVariacionesResponse,
  LineaConteoComparacionDto,
  RechazarConteoCommand,
} from '@/features/almacen/api/types';

/**
 * Hooks del flujo de aprobación + aplicación de conteos (FE-F5-PR2).
 *
 * <para><b>Permisos diferenciados</b>:</para>
 * <list>
 *   <item>El endpoint <c>/comparacion</c> requiere
 *     <c>almacen.inventarios.aprobar-nivel1</c> — el contador no
 *     puede consultar el teórico (A6 protegido a nivel ruta + permiso).</item>
 *   <item>Recuentos los puede agregar quien tenga
 *     <c>almacen.inventarios.capturar</c> (vuelve al rol contador
 *     para una segunda pasada).</item>
 *   <item>Aprobar línea individualmente / aprobar / rechazar / aplicar
 *     exigen <c>aprobar-nivel1+</c>.</item>
 * </list>
 */

/**
 * <b>CON cantidad_teorica + variaciones</b>: endpoint del aprobador.
 * El contador no puede acceder (permiso distinto, captura sin sesgo).
 */
export function useLineasComparacion(conteoId: string | undefined) {
  return useQuery({
    queryKey: conteoId
      ? almacenKeys.lineasComparacion(conteoId)
      : ['almacen', 'conteo', 'comparacion', 'noop'],
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<readonly LineaConteoComparacionDto[]>(
        `/api/v1/almacen/conteos/${conteoId}/comparacion`,
        { signal },
      );
      return data;
    },
    enabled: conteoId != null,
  });
}

export function useAgregarRecuento() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: AgregarRecuentoCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<AgregarRecuentoResponse>(
        `/api/v1/almacen/conteos/${args.command.conteoId}/lineas/${args.command.lineaId}/recuento`,
        {
          method: 'POST',
          body: { cantidadRecontada: args.command.cantidadRecontada },
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: (_, args) => {
      queryClient.invalidateQueries({
        queryKey: almacenKeys.lineasComparacion(args.command.conteoId),
      });
      queryClient.invalidateQueries({
        queryKey: almacenKeys.lineasParaCapturar(args.command.conteoId),
      });
      queryClient.invalidateQueries({
        queryKey: almacenKeys.conteoById(args.command.conteoId),
      });
    },
  });
}

export function useEvaluarVariaciones() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { conteoId: string }) => {
      const { data } = await apiRequest<EvaluarVariacionesResponse>(
        `/api/v1/almacen/conteos/${args.conteoId}/evaluar-variaciones`,
        { method: 'POST', body: {} },
      );
      return data;
    },
    onSuccess: (_, args) => {
      queryClient.invalidateQueries({
        queryKey: almacenKeys.lineasComparacion(args.conteoId),
      });
      queryClient.invalidateQueries({
        queryKey: almacenKeys.conteoById(args.conteoId),
      });
    },
  });
}

export function useAprobarLineaIndividualmente() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { command: AprobarLineaIndividualmenteCommand }) => {
      const c = args.command;
      await apiRequest<void>(
        `/api/v1/almacen/conteos/${c.conteoId}/lineas/${c.lineaId}/aprobar-individualmente`,
        {
          method: 'POST',
          body: { justificacion: c.justificacion },
        },
      );
    },
    onSuccess: (_, args) => {
      queryClient.invalidateQueries({
        queryKey: almacenKeys.lineasComparacion(args.command.conteoId),
      });
      queryClient.invalidateQueries({
        queryKey: almacenKeys.conteoById(args.command.conteoId),
      });
    },
  });
}

export function useAprobarConteo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { conteoId: string }) => {
      await apiRequest<void>(
        `/api/v1/almacen/conteos/${args.conteoId}/aprobar`,
        { method: 'POST', body: {} },
      );
    },
    onSuccess: (_, args) => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.conteos() });
      queryClient.invalidateQueries({
        queryKey: almacenKeys.conteoById(args.conteoId),
      });
    },
  });
}

export function useRechazarConteo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { command: RechazarConteoCommand }) => {
      await apiRequest<void>(
        `/api/v1/almacen/conteos/${args.command.conteoId}/rechazar`,
        {
          method: 'POST',
          body: { motivo: args.command.motivo },
        },
      );
    },
    onSuccess: (_, args) => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.conteos() });
      queryClient.invalidateQueries({
        queryKey: almacenKeys.conteoById(args.command.conteoId),
      });
    },
  });
}

export function useAplicarConteo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      conteoId: string;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<AplicarConteoResponse>(
        `/api/v1/almacen/conteos/${args.conteoId}/aplicar`,
        {
          method: 'POST',
          body: {},
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: (_, args) => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.all });
      queryClient.invalidateQueries({
        queryKey: almacenKeys.conteoById(args.conteoId),
      });
    },
  });
}
