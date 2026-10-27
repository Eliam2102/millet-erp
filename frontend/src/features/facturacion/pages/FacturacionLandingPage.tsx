import { PageHeader } from '@/components/erp/PageHeader';
import { AppLauncherCard } from '@/components/layout/AppLauncherCard';
import { Button } from '@/components/ui/button';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { filtrarModuloPorPermisos, type NavModulo } from '@/lib/nav';
import { Link } from '@tanstack/react-router';
import {
  AlertTriangle,
  Banknote,
  BarChart3,
  Building2,
  ClipboardCheck,
  ClipboardList,
  FileText,
  HandCoins,
  ReceiptText,
  Truck,
  Wallet,
} from 'lucide-react';

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
    <div className="space-y-5">
      <div className="space-y-1">
        <PageHeader
          titulo={<>Facturación</>}
          acciones={
            filtrado.secciones.some((s) => s.cards.some((c) => c.to === '/facturacion/pedidos')) ? (
              <Button asChild>
                <Link to="/facturacion/pedidos">Pedidos facturables</Link>
              </Button>
            ) : undefined
          }
          descripcion={
            <>
              Emisión de CFDI 4.0: pedidos facturables, cobro y emisión, anticipos, notas de
              crédito, REPP, Carta Porte y reportes.
            </>
          }
        />
      </div>

      {!hayCards && (
        <div
          className="rounded-md bg-surface-muted/50 px-4 py-6 text-center text-sm text-ink-muted"
          data-testid="facturacion-sin-cards"
        >
          Sin pantallas disponibles para tu rol en este módulo. Contacta a tu administrador para que
          te asigne los permisos correspondientes.
        </div>
      )}

      {hayCards && (
        <div className="space-y-8">
          {filtrado.secciones.map((seccion) => (
            <section key={seccion.label} className="space-y-3">
              <h2 className="text-xs font-semibold uppercase tracking-wide text-ink-muted">
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
      label: 'Venta',
      cards: [
        {
          label: 'Pedidos facturables',
          description:
            'Bandeja de pedidos por facturar (A+W / Planta Pintura / manual). Facturar toma soft-lock; o capturar un pedido manual.',
          to: '/facturacion/pedidos',
          icon: ClipboardList,
          permissionsAny: [
            PermisosCanonicos.FacturacionFacturasEmitir,
            PermisosCanonicos.FacturacionPedidosCapturar,
            PermisosCanonicos.FacturacionPedidosImportar,
          ],
        },
        {
          label: 'Facturas',
          description:
            'Bandeja de comprobantes emitidos. Cadena de relaciones CFDI, historial del pedido, descargas XML/PDF y cancelación.',
          to: '/facturacion/facturas',
          icon: ReceiptText,
          permission: PermisosCanonicos.FacturacionFacturasLeer,
        },
        {
          label: 'Anticipos',
          description:
            'Control de Anticipos por cliente: emisión, vinculación a la factura final (relación 07) y saldos.',
          to: '/facturacion/anticipos',
          icon: HandCoins,
          permission: PermisosCanonicos.FacturacionAnticiposLeer,
        },
        {
          label: 'Facturas de anticipo',
          description:
            'Bandeja de CFDIs de anticipo: detalle con saldo y cadena, descargas XML/PDF, cancelación y reintento.',
          to: '/facturacion/anticipos/facturas',
          icon: ReceiptText,
          permission: PermisosCanonicos.FacturacionAnticiposLeer,
        },
      ],
    },
    {
      label: 'Caja',
      cards: [
        {
          label: 'Mi caja',
          description:
            'Sesión de efectivo del cajero: apertura con fondo, cobros de mostrador, movimientos y arqueo de cierre.',
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
      label: 'Complementos de pago',
      cards: [
        {
          label: 'Pendientes de REP',
          description: 'Revisión de pagos bancarios y emisión manual de complementos de pago.',
          to: '/facturacion/repp/pendientes',
          icon: HandCoins,
          permission: PermisosCanonicos.FacturacionFacturasLeer,
        },
        {
          label: 'Complementos de pago (REPP)',
          description:
            'Recibos electrónicos de pago (Pago 2.0). Emisión multi-factura y consulta de facturas cubiertas.',
          to: '/facturacion/repp',
          icon: HandCoins,
          permissionsAny: [
            PermisosCanonicos.FacturacionReppEmitir,
            PermisosCanonicos.FacturacionFacturasLeer,
          ],
        },
      ],
    },
    {
      label: 'Otros documentos',
      cards: [
        {
          label: 'Carta Porte',
          description:
            'Bandeja de Carta Portes 3.1. Captura de tramo (vehículo, operador, mercancías) y "Crear siguiente tramo".',
          to: '/facturacion/carta-porte',
          icon: Truck,
          permission: PermisosCanonicos.FacturacionCartaPorteLeer,
        },
        {
          label: 'Autorización de activos',
          description:
            'El Director de Administración y Finanzas autoriza la venta de activos fijos (valor neto + utilidad/pérdida) antes de timbrar.',
          to: '/facturacion/activos',
          icon: Building2,
          permission: PermisosCanonicos.FacturacionActivosAutorizar,
        },
      ],
    },
    {
      label: 'Revisión',
      cards: [
        {
          label: 'Excepciones de ingesta',
          description:
            'Pedidos que fallaron la importación (cliente no existe, producto sin clave SAT, almacén no asignado). Resolución inline.',
          to: '/facturacion/pedidos/excepciones',
          icon: AlertTriangle,
          permission: PermisosCanonicos.FacturacionPedidosExcepcionesResolver,
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
          description:
            'Cierre de caja: facturado vs cobrado por cajero y fecha. Exportable PDF/Excel.',
          to: '/facturacion/reportes/liquidacion-caja',
          icon: Banknote,
          permission: PermisosCanonicos.FacturacionReportesLeer,
        },
        {
          label: 'Cierre del día por sucursal',
          description:
            'Monitor de CFDI del día (timbrados, cancelados, sin timbrar, estatus SAT) y facturado de contado contra ingresado en caja.',
          to: '/facturacion/reportes/cierre-dia',
          icon: ClipboardCheck,
          permission: PermisosCanonicos.FacturacionReportesLeer,
        },
        {
          label: 'Estados de facturas de anticipo',
          description:
            'Reporte de anticipos emitidos, facturas vinculadas, NCs de amortización y saldo por cliente.',
          to: '/facturacion/reportes/estados-anticipos',
          icon: BarChart3,
          permission: PermisosCanonicos.FacturacionReportesLeer,
        },
      ],
    },
  ],
};
