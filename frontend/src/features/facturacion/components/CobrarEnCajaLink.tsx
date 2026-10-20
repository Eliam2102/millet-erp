import { useNavigate } from '@tanstack/react-router';
import { Button } from '@/components/ui/button';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useSesionActual, useComprobantesCobrables } from '../api/useCajaSesiones';

export function CobrarEnCajaLink({
  comprobanteId,
  folio,
}: {
  comprobanteId: string;
  folio: string;
}) {
  const permiso = useHasPermission(PermisosCanonicos.FacturacionCajaOperar);
  const sesion = useSesionActual();
  const abierta =
    permiso && sesion.data?.sesion?.estado === 'Abierta' && !sesion.data?.diaAnteriorPendiente;
  const cobrables = useComprobantesCobrables(folio, abierta);
  const comprobante = cobrables.data?.find((c) => c.comprobanteId === comprobanteId);
  if (!abierta || !comprobante || comprobante.total <= 0) return null;
  const search = new URLSearchParams({ cobrar: comprobanteId, folio });
  return <BotonCobrarEnCaja href={`/facturacion/caja?${search.toString()}`} />;
}

/**
 * Navegación del router, sin recargar la aplicación: en sucursal cada recarga
 * cuesta 30–40 s (sesión con Millet, 19-oct). Sólo se monta cuando el botón
 * aplica, así las vistas sin router (pruebas de detalle) no lo necesitan.
 */
function BotonCobrarEnCaja({ href }: { href: string }) {
  const navigate = useNavigate();
  return (
    <Button type="button" variant="secondary" onClick={() => navigate({ href })}>
      Cobrar en caja
    </Button>
  );
}
