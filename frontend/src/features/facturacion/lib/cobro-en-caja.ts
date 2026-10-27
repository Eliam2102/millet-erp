import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { toast } from 'sonner';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import type {
  ComprobanteCobrableItem,
  SesionActualResponse,
} from '@/features/facturacion/api/types';
import {
  cobrablesQueryOptions,
  useComprobantesCobrables,
  useSesionActual,
} from '@/features/facturacion/api/useCajaSesiones';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';

/** Ruta de Mi caja con el cobro del comprobante ya abierto (`?cobrar=…&folio=…`). */
export function rutaCobroCaja(comprobanteId: string, folio: string): string {
  const search = new URLSearchParams({ cobrar: comprobanteId, folio });
  return `/facturacion/caja?${search.toString()}`;
}

/**
 * El usuario puede cobrar ahora: tiene permiso de operar caja, su sesión está
 * Abierta y no arrastra un día anterior pendiente de cierre. Sin permiso no se
 * consulta la sesión (el endpoint respondería 403).
 */
export function usePuedeOperarCaja(): boolean {
  const permiso = useHasPermission(PermisosCanonicos.FacturacionCajaOperar);
  const sesion = useSesionActual({ enabled: permiso });
  return permiso && sesionOperable(sesion.data);
}

function sesionOperable(data: SesionActualResponse | undefined): boolean {
  return data?.sesion?.estado === 'Abierta' && !data.diaAnteriorPendiente;
}

/** Cobrable = el backend lo lista entre los cobrables (PUE, MXN, timbrado, sin cobro) y con importe. */
function esCobrable(items: ComprobanteCobrableItem[] | undefined, comprobanteId: string) {
  const comprobante = items?.find((c) => c.comprobanteId === comprobanteId);
  return comprobante != null && comprobante.total > 0;
}

/**
 * Ruta de cobro del comprobante si hoy se puede cobrar en caja; `null` si no
 * aplica (sin permiso, caja cerrada, día anterior pendiente o no cobrable).
 */
export function useRutaCobroCaja(comprobanteId: string, folio: string): string | null {
  const puede = usePuedeOperarCaja();
  const cobrables = useComprobantesCobrables(folio, puede);
  return puede && esCobrable(cobrables.data, comprobanteId)
    ? rutaCobroCaja(comprobanteId, folio)
    : null;
}

/**
 * Aviso de emisión con la acción «Cobrar en caja» (86e3tfdwu): muestra el
 * toast de éxito y, si el comprobante timbrado se puede cobrar ya, le agrega la
 * acción que lleva a Mi caja por el router (sin recargar). Pensado para formas
 * que se cierran al emitir (sheet del anticipo).
 *
 * <para>La sesión se lee de la caché al emitir (la forma ya la consultó para la
 * sucursal) en lugar de suscribirse: un segundo observador re-dispararía la
 * consulta fallida y regresaría la forma al estado de carga.</para>
 */
export function useAvisoEmisionConCobro() {
  const permiso = useHasPermission(PermisosCanonicos.FacturacionCajaOperar);
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  return (mensaje: string, comprobante: { id: string; folio: string } | null) => {
    const sesion = queryClient.getQueryData<SesionActualResponse>(facturacionKeys.sesionActual());
    if (!permiso || !sesionOperable(sesion) || comprobante == null) {
      toast.success(mensaje);
      return;
    }
    void queryClient
      .fetchQuery(cobrablesQueryOptions(comprobante.folio))
      .then(
        (items) => esCobrable(items, comprobante.id),
        () => false,
      )
      .then((cobrable) => {
        if (!cobrable) {
          toast.success(mensaje);
          return;
        }
        const href = rutaCobroCaja(comprobante.id, comprobante.folio);
        toast.success(mensaje, {
          duration: 15_000,
          action: { label: 'Cobrar en caja', onClick: () => void navigate({ href }) },
        });
      });
  };
}
