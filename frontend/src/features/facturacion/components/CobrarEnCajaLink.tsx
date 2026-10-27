import { Button } from '@/components/ui/button';
import { useNavigate } from '@tanstack/react-router';
import { useRutaCobroCaja } from '../lib/cobro-en-caja';

/**
 * Botón «Cobrar en caja» del comprobante: solo aparece con permiso de operar
 * caja, sesión Abierta sin día anterior pendiente y comprobante cobrable con
 * importe (condición compartida en <c>useRutaCobroCaja</c>).
 */
export function CobrarEnCajaLink({
  comprobanteId,
  folio,
}: {
  comprobanteId: string;
  folio: string;
}) {
  const href = useRutaCobroCaja(comprobanteId, folio);
  if (href == null) return null;
  return <BotonCobrarEnCaja href={href} />;
}

/**
 * Aviso tras emitir una factura sin salir de la pantalla (86e3tfdwu, detalle
 * del pedido): ofrece cobrarla en caja. No se muestra si el botón no aplica.
 */
export function AvisoCobrarEnCaja({
  comprobanteId,
  folio,
}: {
  comprobanteId: string;
  folio: string;
}) {
  const href = useRutaCobroCaja(comprobanteId, folio);
  if (href == null) return null;
  return (
    <div
      role="status"
      className="flex flex-wrap items-center justify-between gap-3 rounded-md bg-success-bg px-3 py-2.5 text-sm text-success-fg"
    >
      <p>
        Factura <span className="font-mono">{folio}</span> timbrada. Puedes cobrarla ahora en tu
        caja.
      </p>
      <BotonCobrarEnCaja href={href} />
    </div>
  );
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
