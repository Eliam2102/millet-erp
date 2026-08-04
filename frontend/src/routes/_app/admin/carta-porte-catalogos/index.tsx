import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { CartaPorteCatalogosPage } from '@/features/facturacion/pages/CartaPorteCatalogosPage';

/**
 * <c>/admin/carta-porte-catalogos</c> — catálogos administrables de
 * Carta Porte (módulo Facturación): Vehículos y Operadores. Lista +
 * alta + edición inline + activar/desactivar, patrón de Canales de
 * venta. Cierra PLATFORM-TODO(&lt;VehiculoPicker&gt;/&lt;OperadorPicker&gt;).
 *
 * <para><b>Guard</b>: requiere <c>facturacion.carta-porte.leer</c>
 * (mismo permiso que los GET del backend); las mutaciones dentro de la
 * página se gatean con <c>facturacion.carta-porte.emitir</c>. Sin
 * permiso, redirige a <c>/</c>.</para>
 */
export const Route = createFileRoute('/_app/admin/carta-porte-catalogos/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.FacturacionCartaPorteLeer)) {
      throw redirect({ to: '/' });
    }
  },
  component: CartaPorteCatalogosIndexRoute,
});

function CartaPorteCatalogosIndexRoute() {
  return <CartaPorteCatalogosPage />;
}
