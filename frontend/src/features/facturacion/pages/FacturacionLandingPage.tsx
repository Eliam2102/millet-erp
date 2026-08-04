import { AppLauncherCard } from '@/components/layout/AppLauncherCard';
import { useAuthStore } from '@/lib/auth/auth-store';
import { filtrarModuloPorPermisos, type NavModulo } from '@/lib/nav';
import {
  AlertTriangle,
  Banknote,
  BarChart3,
  Building2,
  ClipboardList,
  FileText,
  HandCoins,
  ReceiptText,
  Truck,
  Wallet,
} from 'lucide-react';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Landing del módulo Facturación — <c>/facturacion</c>. Mismo patrón que
 * Almacén / Compras / CxP: el sidebar abre el modal con cards y, si el
 * usuario llega por URL directa, esta landing renderiza el mismo
 * conjunto.
 *
 * <para>El gate de permiso fino está en cada card; aquí solo verificamos
 * que el usuario tenga al menos uno de los permisos
 * <c>facturacion.*</c>. Sin cards visibles → mensaje neutro (no
 * <c>AccessDenied</c>, porque <c>/_app</c> ya gateó la autenticación;
 * "0 cards visibles" es el equivalente del modal vacío en el
 * AppLauncher).</para>
 */
export function FacturacionLandingPage() {
  const permisos = useAuthStore((s) => s.permisos);
  const filtrado = filtrarModuloPorPermisos(moduloFacturacionLanding, permisos);
  const hayCards = filtrado.secciones.some((s) => s.cards.length > 0);

  return (
    <div className="mx-auto max-w-5xl space-y-6 px-4 py-8">
      <header className="space-y-1">
        <h1 className="flex items-center gap-2 text-2xl font-semibold">
          <FileText className="h-6 w-6 text-primary" />
          Facturación
        </h1>
        <p className="text-sm text-muted-foreground">
          Emisión de CFDI 4.0: pedidos facturables, cobro y emisión,
          anticipos, notas de crédito, REPP, Carta Porte y reportes.
        </p>
      </header>

      {!hayCards && (
        <div
          className="rounded-md bg-muted/50 px-4 py-6 text-center text-sm text-muted-foreground"
          data-testid="facturacion-sin-cards"
        >
          Sin pantallas disponibles para tu rol en este módulo. Contacta a tu
          administrador para que te asigne los permisos correspondientes.
        </div>
      )}

      {hayCards && (
        <div className="space-y-8">
          {filtrado.secciones.map((seccion) => (
            <section key={seccion.label} className="space-y-3">
              <h2 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                {seccion.label}
              </h2>
              <div className="grid grid-cols-1 gap-3 md:grid-cols-2 lg:grid-cols-3">
                {seccion.cards.map((card) => (
                  <AppLauncherCard
                    key={card.to}
                    card={card}
                    onSelect={() => {
                      /* Sin modal contenedor; navegación directa por Link. */
                    }}
                  />
                ))}
              </div>
            </section>
          ))}
        </div>
      )}
    </div>
  );
}

/**
 * Copia local del módulo nav para evitar dep circular con
 * <c>nav.ts</c>. Idéntico shape al de <c>moduloFacturacion</c> en
 * <c>nav.ts</c> — mantener en sincronía.
 */
const moduloFacturacionLanding: NavModulo = {
  moduloId: 'facturacion',
  label: 'Facturación',
  icon: FileText,
  secciones: [
    {
      label: 'Operación',
      cards: [
        {
          label: 'Pedidos facturables',
          description:
            'Bandeja de pedidos por facturar (A+W / Planta Pintura / manual). Facturar o capturar un pedido manual.',
          to: '/facturacion/pedidos',
          icon: ClipboardList,
          permissionsAny: [
            PermisosCanonicos.FacturacionFacturasEmitir,
            PermisosCanonicos.FacturacionPedidosCapturar,
            PermisosCanonicos.FacturacionPedidosImportar,
          ],
        },
        {
          label: 'Excepciones de ingesta',
          description:
            'Pedidos que fallaron la importación. Resolución inline.',
          to: '/facturacion/pedidos/excepciones',
          icon: AlertTriangle,
          permission: PermisosCanonicos.FacturacionPedidosExcepcionesResolver,
        },
        {
          label: 'Facturas',
          description:
            'Bandeja de comprobantes emitidos. Cadena CFDI, descargas y cancelación.',
          to: '/facturacion/facturas',
          icon: ReceiptText,
          permission: PermisosCanonicos.FacturacionFacturasLeer,
        },
        {
          label: 'Anticipos',
          description:
            'Control de Anticipos por cliente: emisión, vinculación (relación 07) y saldos.',
          to: '/facturacion/anticipos',
          icon: HandCoins,
          permission: PermisosCanonicos.FacturacionAnticiposLeer,
        },
        {
          label: 'Complementos de pago (REPP)',
          description:
            'Recibos electrónicos de pago (Pago 2.0). Emisión multi-factura y facturas cubiertas.',
          to: '/facturacion/repp',
          icon: HandCoins,
          permissionsAny: [
            PermisosCanonicos.FacturacionReppEmitir,
            PermisosCanonicos.FacturacionFacturasLeer,
          ],
        },
        {
          label: 'Carta Porte',
          description:
            'Bandeja de Carta Portes 3.1. Captura de tramo y "Crear siguiente tramo".',
          to: '/facturacion/carta-porte',
          icon: Truck,
          permission: PermisosCanonicos.FacturacionCartaPorteLeer,
        },
        {
          label: 'Autorización de activos',
          description:
            'El Contador General autoriza la venta de activos fijos antes de timbrar.',
          to: '/facturacion/activos',
          icon: Building2,
          permission: PermisosCanonicos.FacturacionActivosAutorizar,
        },
        {
          label: 'Mi caja',
          description:
            'Sesión de efectivo: apertura con fondo, cobros, movimientos y arqueo de cierre.',
          to: '/facturacion/caja',
          icon: Wallet,
          permissionsAny: [
            PermisosCanonicos.FacturacionCajaOperar,
            PermisosCanonicos.FacturacionCajaSupervisar,
            PermisosCanonicos.FacturacionCajaLiquidar,
          ],
        },
      ],
    },
    {
      label: 'Configuración',
      cards: [
        {
          label: 'Cajas',
          description:
            'Administra las cajas del módulo: alcance por sucursal × canal, cajeros con acceso y concesiones por usuario.',
          to: '/facturacion/cajas',
          icon: Wallet,
          permission: PermisosCanonicos.FacturacionCajaAdministrar,
        },
      ],
    },
    {
      label: 'Reportes',
      cards: [
        {
          label: 'Liquidación de caja',
          description: 'Facturado vs cobrado por cajero y fecha. PDF/Excel.',
          to: '/facturacion/reportes/liquidacion-caja',
          icon: Banknote,
          permission: PermisosCanonicos.FacturacionReportesLeer,
        },
        {
          label: 'Estados de facturas de anticipo',
          description:
            'Anticipos, facturas vinculadas, NCs de amortización y saldo por cliente.',
          to: '/facturacion/reportes/estados-anticipos',
          icon: BarChart3,
          permission: PermisosCanonicos.FacturacionReportesLeer,
        },
      ],
    },
  ],
};
