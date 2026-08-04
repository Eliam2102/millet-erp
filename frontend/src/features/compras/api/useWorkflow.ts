import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { comprasKeys } from '@/features/compras/api/keys';
import type { AutorizarValues } from '@/features/compras/schemas/autorizar';
import type { TerminarRequisicionValues } from '@/features/compras/schemas/terminar-requisicion';

/**
 * Hooks de mutación del workflow de autorización (doc 05 §6, §7.6).
 *
 * <para>Convención de invalidación: tras éxito, todos invalidan</para>
 * <list>
 *   <item><c>comprasKeys.requisicion(rqId)</c> para que P3 refleje
 *   el cambio de estado.</item>
 *   <item><c>comprasKeys.requisiciones()</c> (familia) para que la
 *   bandeja general (P1) refresque.</item>
 *   <item><c>comprasKeys.all</c> (familia entera) — incluye también
 *   <c>pendientes-autorizacion</c>. Más amplio pero garantiza que
 *   la P2 refresque sin importar los filtros activos.</item>
 * </list>
 *
 * <para>Idempotency-Key obligatorio en todos (ADR-0020): el backend
 * lo enforce con <c>RequireIdempotencyKeyAttribute</c>.</para>
 */

function invalidarFamiliaCompras(
  queryClient: ReturnType<typeof useQueryClient>,
) {
  queryClient.invalidateQueries({ queryKey: comprasKeys.all });
}

// ─── Transmitir (Borrador → EnAutorizacion) ─────────────────────────

export interface TransmitirArgs {
  requisicionId: string;
  idempotencyKey: string;
}

export function useTransmitirRequisicion() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, TransmitirArgs>({
    mutationFn: async ({ requisicionId, idempotencyKey }) => {
      await apiRequest<void>(
        `/api/v1/compras/requisiciones/${requisicionId}/transmitir`,
        { method: 'POST', idempotencyKey },
      );
    },
    onSuccess: () => invalidarFamiliaCompras(queryClient),
  });
}

// ─── Autorizar (Nivel1 / Nivel2) ────────────────────────────────────

export interface AutorizarArgs {
  requisicionId: string;
  values: AutorizarValues;
  idempotencyKey: string;
}

export function useAutorizarRequisicion() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, AutorizarArgs>({
    mutationFn: async ({ requisicionId, values, idempotencyKey }) => {
      await apiRequest<void>(
        `/api/v1/compras/requisiciones/${requisicionId}/autorizaciones`,
        { method: 'POST', body: values, idempotencyKey },
      );
    },
    onSuccess: () => invalidarFamiliaCompras(queryClient),
  });
}

// ─── Rechazar (EnAutorizacion → Rechazada) ──────────────────────────

export interface RechazarArgs {
  requisicionId: string;
  values: TerminarRequisicionValues;
  idempotencyKey: string;
}

export function useRechazarRequisicion() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, RechazarArgs>({
    mutationFn: async ({ requisicionId, values, idempotencyKey }) => {
      await apiRequest<void>(
        `/api/v1/compras/requisiciones/${requisicionId}/rechazar`,
        { method: 'POST', body: values, idempotencyKey },
      );
    },
    onSuccess: () => invalidarFamiliaCompras(queryClient),
  });
}

// ─── Eliminar (Borrador / EnAutorizacion → Eliminada) ───────────────

export interface EliminarArgs {
  requisicionId: string;
  values: TerminarRequisicionValues;
}

/**
 * <c>useEliminarRequisicion</c> — pre-autorización
 * (<c>Borrador</c>/<c>EnAutorizacion</c> → <c>Eliminada</c>).
 *
 * <para><b>SIN Idempotency-Key</b> — decisión backend documentada en
 * doc 05 §7.6: la operación es naturalmente idempotente (intentar
 * eliminar una RQ ya eliminada devuelve 422 estado-inválido, no se
 * acumulan side effects). Reintentar con la misma payload es seguro.</para>
 */
export function useEliminarRequisicion() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, EliminarArgs>({
    mutationFn: async ({ requisicionId, values }) => {
      await apiRequest<void>(
        `/api/v1/compras/requisiciones/${requisicionId}/eliminar`,
        { method: 'POST', body: values },
      );
    },
    onSuccess: () => invalidarFamiliaCompras(queryClient),
  });
}

// ─── Cancelar (Autorizada / EnSurtido → Cancelada) ──────────────────

export interface CancelarArgs {
  requisicionId: string;
  values: TerminarRequisicionValues;
  idempotencyKey: string;
}

/**
 * <c>useCancelarRequisicion</c> — post-autorización
 * (<c>Autorizada</c>/<c>EnSurtido</c> → <c>Cancelada</c>). Doc 05 §7.6.
 *
 * <para><b>Requiere Idempotency-Key</b> — la cancelación dispara
 * efectos downstream (liberación de reservas de stock, cancelación de
 * OCs borrador asociadas) que NO son idempotentes naturalmente. El
 * backend usa el Idempotency-Key para dedupe.</para>
 *
 * <para><b>Error específico <c>CANCELAR_FALLO</c> (422)</b>: si el
 * comando del lado A+W o de stock falla downstream, el backend devuelve
 * 422 con <c>code=CANCELAR_FALLO</c> + <c>traceId</c>. El call site
 * debe mostrar toast específico con CTA "Reintentar" (no automático,
 * el usuario decide tras revisar logs).</para>
 */
export function useCancelarRequisicion() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, CancelarArgs>({
    mutationFn: async ({ requisicionId, values, idempotencyKey }) => {
      await apiRequest<void>(
        `/api/v1/compras/requisiciones/${requisicionId}/cancelar`,
        { method: 'POST', body: values, idempotencyKey },
      );
    },
    onSuccess: () => invalidarFamiliaCompras(queryClient),
  });
}

// ─── Cerrar manual (Autorizada / EnSurtido → CerradaSinSurtir/Parcial) ─

export interface CerrarManualArgs {
  requisicionId: string;
  values: TerminarRequisicionValues;
  idempotencyKey: string;
}

/**
 * <c>useCerrarManualRequisicion</c> — cierre manual del jefe de almacén /
 * almacenista (ADR-0043). El estado terminal lo deriva el backend de lo
 * entregado (<c>CerradaSinSurtir</c> / <c>CerradaSurtidaParcial</c>).
 *
 * <para><b>Requiere Idempotency-Key</b>: libera reservas del tramo de stock
 * (efecto no idempotente). Error <c>CIERRE_MANUAL_FALLO</c> (422) si un port
 * falla downstream — el call site muestra toast con CTA "Reintentar".</para>
 */
export function useCerrarManualRequisicion() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, CerrarManualArgs>({
    mutationFn: async ({ requisicionId, values, idempotencyKey }) => {
      await apiRequest<void>(
        `/api/v1/compras/requisiciones/${requisicionId}/cerrar-manual`,
        { method: 'POST', body: values, idempotencyKey },
      );
    },
    onSuccess: () => invalidarFamiliaCompras(queryClient),
  });
}
