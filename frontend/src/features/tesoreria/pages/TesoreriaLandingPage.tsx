import { AppLauncherCard } from '@/components/layout/AppLauncherCard';
import { useAuthStore } from '@/lib/auth/auth-store';
import { filtrarModuloPorPermisos, type NavModulo } from '@/lib/nav';
import {
  ArrowRightLeft,
  Banknote,
  BarChart3,
  ClipboardList,
  FileText,
  HandCoins,
  Inbox,
  Landmark,
  ReceiptText,
  Scale,
  Wallet,
} from 'lucide-react';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Landing del módulo Tesorería — <c>/tesoreria</c> (TES-FE-PR1). Mismo
 * patrón que CxC / Facturación / Compras: el sidebar abre el modal con
 * cards y, si el usuario llega por URL directa, esta landing renderiza
 * el mismo conjunto gateado por permisos <c>tesoreria.*</c>. Sin cards
 * visibles → mensaje neutro.
 */
export function TesoreriaLandingPage() {
  const permisos = useAuthStore((s) => s.permisos);
  const filtrado = filtrarModuloPorPermisos(moduloTesoreriaLanding, permisos);
  const hayCards = filtrado.secciones.some((s) => s.cards.length > 0);

  return (
    <div className="mx-auto max-w-5xl space-y-6 px-4 py-8">
      <header className="space-y-1">
        <h1 className="flex items-center gap-2 text-2xl font-semibold">
          <Landmark className="h-6 w-6 text-primary" />
          Tesorería
        </h1>
        <p className="text-sm text-muted-foreground">
          El hecho bancario en ambos sentidos: pagos a proveedor, pagos a
          cuenta, confirmación de depósitos, conciliación y REPP.
        </p>
      </header>

      {!hayCards && (
        <div
          className="rounded-md bg-muted/50 px-4 py-6 text-center text-sm text-muted-foreground"
          data-testid="tesoreria-sin-cards"
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
 * <c>nav.ts</c>. Idéntico shape al de <c>moduloTesoreria</c> en
 * <c>nav.ts</c> — mantener en sincronía.
 */
const moduloTesoreriaLanding: NavModulo = {
  moduloId: 'tesoreria',
  label: 'Tesorería',
  icon: Landmark,
  secciones: [
    {
      label: 'Operación',
      cards: [
        {
          label: 'Pagos a proveedor',
          description:
            'Bandeja de pasivos autorizados por CxP: ejecutar pagos, revertir y solicitar cancelación.',
          to: '/tesoreria/pagos',
          icon: Banknote,
          permission: PermisosCanonicos.TesoreriaPasivosVer,
        },
        {
          label: 'Pagos a cuenta',
          description:
            'Egresos sin documento (máximo uno abierto por proveedor) con antigüedad y liga tardía al pasivo.',
          to: '/tesoreria/pagos-cuenta',
          icon: HandCoins,
          permissionsAny: [
            PermisosCanonicos.TesoreriaMovimientosVer,
            PermisosCanonicos.TesoreriaPagosCuentaRegistrar,
            PermisosCanonicos.TesoreriaPagosCuentaLigar,
          ],
        },
        {
          label: 'Corridas de pago',
          description:
            'Lotes de pasivos autorizables como unidad con oficio de cartera imprimible.',
          to: '/tesoreria/corridas',
          icon: ClipboardList,
          permissionsAny: [
            PermisosCanonicos.TesoreriaCorridasCrear,
            PermisosCanonicos.TesoreriaCorridasAutorizar,
            PermisosCanonicos.TesoreriaCorridasEjecutar,
          ],
        },
        {
          label: 'Depósitos por confirmar',
          description:
            'Propuestas de aplicación de CxC y expectativas de Caja: confirmar el hecho bancario que dispara el REPP.',
          to: '/tesoreria/depositos',
          icon: Inbox,
          permissionsAny: [
            PermisosCanonicos.TesoreriaDepositosConfirmar,
            PermisosCanonicos.TesoreriaDepositosRechazar,
          ],
        },
        {
          label: 'Movimientos bancarios',
          description:
            'Libro de movimientos por cuenta: ingresos, egresos, aplicaciones y contramovimientos.',
          to: '/tesoreria/movimientos',
          icon: ArrowRightLeft,
          permission: PermisosCanonicos.TesoreriaMovimientosVer,
        },
        {
          label: 'Conciliación bancaria',
          description:
            'Carga de extracto por perfil de banco, matching asistido y cierre con acta (saldo cuadrado).',
          to: '/tesoreria/conciliacion',
          icon: Scale,
          permissionsAny: [
            PermisosCanonicos.TesoreriaConciliacionOperar,
            PermisosCanonicos.TesoreriaConciliacionCerrar,
          ],
        },
        {
          label: 'REPP de proveedor',
          description:
            'Pagos PPD sin complemento recibido (SLA 5 días) y registro del REPP que libera la revisión en CxP.',
          to: '/tesoreria/repp',
          icon: ReceiptText,
          permission: PermisosCanonicos.TesoreriaReppRegistrar,
        },
        {
          label: 'Cuentas bancarias',
          description:
            'Catálogo de cuentas propias con saldo: alta, edición y activación (número/CLABE enmascarados).',
          to: '/tesoreria/cuentas',
          icon: Wallet,
          permission: PermisosCanonicos.TesoreriaCuentasVer,
        },
      ],
    },
    {
      label: 'Reportes',
      cards: [
        {
          label: 'Flujo de efectivo',
          description:
            'Ingresos y egresos clasificados por concepto (Operación / Inversión / Financiamiento). ADR-0036.',
          to: '/tesoreria/reportes/flujo-efectivo',
          icon: BarChart3,
          permission: PermisosCanonicos.TesoreriaReportesVer,
        },
        {
          label: 'Auxiliar de bancos',
          description:
            'Libro cronológico por cuenta y período con saldo acumulado. Exportable PDF/Excel.',
          to: '/tesoreria/reportes/auxiliar-bancos',
          icon: FileText,
          permission: PermisosCanonicos.TesoreriaReportesVer,
        },
      ],
    },
  ],
};
