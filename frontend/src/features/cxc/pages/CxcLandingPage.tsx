import { AppLauncherCard } from '@/components/layout/AppLauncherCard';
import { IndicadoresCxc } from '@/features/cxc/components/IndicadoresCxc';
import { useAuthStore } from '@/lib/auth/auth-store';
import { filtrarModuloPorPermisos, type NavModulo } from '@/lib/nav';
import {
  BarChart3,
  Bell,
  CreditCard,
  FileText,
  HandCoins,
  PhoneCall,
  Receipt,
  Unlock,
} from 'lucide-react';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Landing del módulo Cuentas por Cobrar — <c>/cxc</c>. Mismo patrón que
 * Facturación / Compras / CxP: el sidebar abre el modal con cards y, si
 * el usuario llega por URL directa, esta landing renderiza el mismo
 * conjunto.
 *
 * <para>El gate de permiso fino está en cada card; aquí solo verificamos
 * que el usuario tenga al menos uno de los permisos
 * <c>cuentas_por_cobrar.*</c>. Sin cards visibles → mensaje neutro (no
 * <c>AccessDenied</c>, porque <c>/_app</c> ya gateó la autenticación;
 * "0 cards visibles" es el equivalente del modal vacío en el
 * AppLauncher).</para>
 */
export function CxcLandingPage() {
  const permisos = useAuthStore((s) => s.permisos);
  const filtrado = filtrarModuloPorPermisos(moduloCxcLanding, permisos);
  const hayCards = filtrado.secciones.some((s) => s.cards.length > 0);

  return (
    <div className="mx-auto max-w-5xl space-y-6 px-4 py-8">
      <header className="space-y-1">
        <h1 className="flex items-center gap-2 text-2xl font-semibold">
          <Receipt className="h-6 w-6 text-primary" />
          Cuentas por Cobrar
        </h1>
        <p className="text-sm text-muted-foreground">
          Cartera de clientes: líneas de crédito, liberación de pedidos,
          cobranza, aplicación de pagos, antigüedad de saldos y alertas.
        </p>
      </header>

      {/* Indicadores rápidos (05-frontend-diseno §2): vencido por
          moneda + alertas pendientes. Solo con cartera.leer. */}
      <IndicadoresCxc />

      {!hayCards && (
        <div
          className="rounded-md bg-muted/50 px-4 py-6 text-center text-sm text-muted-foreground"
          data-testid="cxc-sin-cards"
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
 * <c>nav.ts</c>. Idéntico shape al de <c>moduloCuentasPorCobrar</c> en
 * <c>nav.ts</c> — mantener en sincronía.
 */
const moduloCxcLanding: NavModulo = {
  moduloId: 'cxc',
  label: 'Cuentas por Cobrar',
  icon: Receipt,
  secciones: [
    {
      label: 'Operación',
      cards: [
        {
          label: 'Líneas de crédito',
          description:
            'Master de líneas por cliente y moneda: límite, plazo, origen (SOLUNION / interno) y crédito disponible.',
          to: '/cxc/lineas-credito',
          icon: CreditCard,
          permission: PermisosCanonicos.CuentasPorCobrarLineasCreditoLeer,
        },
        {
          label: 'Liberación de pedidos',
          description:
            'Decisiones de liberación con cascada serie → crédito → override y autorizaciones consumibles.',
          to: '/cxc/liberaciones',
          icon: Unlock,
          permissionsAny: [
            PermisosCanonicos.CuentasPorCobrarLiberacionDecidir,
            PermisosCanonicos.CuentasPorCobrarLiberacionOverride,
          ],
        },
        {
          label: 'Cobranza',
          description:
            'Seguimientos de cobranza por cliente (canal, resultado, promesa de pago). Bitácora append-only.',
          to: '/cxc/cobranza',
          icon: PhoneCall,
          permissionsAny: [
            PermisosCanonicos.CuentasPorCobrarCarteraLeer,
            PermisosCanonicos.CuentasPorCobrarCobranzaRegistrar,
          ],
        },
        {
          label: 'Anticipos de clientes',
          description:
            'Saldos de anticipo por cliente (datos del read port de Facturación). Reemplazo del "mapa" A+W.',
          to: '/cxc/anticipos',
          icon: HandCoins,
          permission: PermisosCanonicos.CuentasPorCobrarCarteraLeer,
        },
        {
          label: 'Aplicación de pagos',
          description:
            'Propuestas de aplicación depósito ↔ facturas con tolerancia no fiscal; confirmación de Ingresos.',
          to: '/cxc/aplicaciones',
          icon: FileText,
          permissionsAny: [
            PermisosCanonicos.CuentasPorCobrarAplicacionPagoProponer,
            PermisosCanonicos.CuentasPorCobrarAplicacionPagoConfirmar,
          ],
        },
        {
          label: 'Alertas de cartera',
          description:
            'Alertas de vencimiento, sobregiro y promesas incumplidas evaluadas por el worker diario.',
          to: '/cxc/alertas',
          icon: Bell,
          permission: PermisosCanonicos.CuentasPorCobrarCarteraLeer,
        },
      ],
    },
    {
      label: 'Reportes',
      cards: [
        {
          label: 'Antigüedad de saldos',
          description:
            'Cartera viva por buckets configurables, totales por moneda. Exportable PDF/Excel (ADR-0036).',
          to: '/cxc/cartera',
          icon: BarChart3,
          permission: PermisosCanonicos.CuentasPorCobrarCarteraLeer,
        },
        {
          label: 'Estado de cuenta',
          description:
            'Estado de cuenta por cliente: facturas, pagos, NCs y saldo corriente. Impresión limpia.',
          to: '/cxc/estado-cuenta',
          icon: FileText,
          permission: PermisosCanonicos.CuentasPorCobrarCarteraLeer,
        },
      ],
    },
  ],
};
