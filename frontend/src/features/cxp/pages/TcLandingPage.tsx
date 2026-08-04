import { AppLauncherCard } from '@/components/layout/AppLauncherCard';
import { useAuthStore } from '@/lib/auth/auth-store';
import { filtrarModuloPorPermisos, type NavModulo } from '@/lib/nav';
import {
  CreditCard,
  FileSpreadsheet,
  Receipt,
  Wallet,
} from 'lucide-react';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Landing del sub-módulo TC empresarial — <c>/cxp/tc</c>. Cards a las
 * 3 secciones: Tarjetas (admin), Movimientos (titular/auxiliar),
 * Estados de cuenta (conciliación).
 */
export function TcLandingPage() {
  const permisos = useAuthStore((s) => s.permisos);
  const filtrado = filtrarModuloPorPermisos(moduloTcLanding, permisos);
  const hayCards = filtrado.secciones.some((s) => s.cards.length > 0);

  return (
    <div className="mx-auto max-w-5xl space-y-6 px-4 py-8">
      <header className="space-y-1">
        <h1 className="flex items-center gap-2 text-2xl font-semibold">
          <CreditCard className="h-6 w-6 text-primary" />
          Tarjetas de crédito empresariales
        </h1>
        <p className="text-sm text-muted-foreground">
          Master de tarjetas, captura de movimientos (Flujos A y B),
          conciliación con estado de cuenta del banco y cierre.
        </p>
      </header>

      {!hayCards && (
        <div
          className="rounded-md bg-muted/50 px-4 py-6 text-center text-sm text-muted-foreground"
          data-testid="tc-sin-cards"
        >
          Sin pantallas TC disponibles para tu rol. Contacta a tu
          administrador para asignar permisos.
        </div>
      )}

      {hayCards &&
        filtrado.secciones.map((seccion) => (
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
                    /* navegación directa por Link */
                  }}
                />
              ))}
            </div>
          </section>
        ))}
    </div>
  );
}

const moduloTcLanding: NavModulo = {
  moduloId: 'cxp-tc',
  label: 'TC empresariales',
  icon: CreditCard,
  secciones: [
    {
      label: 'Operación',
      cards: [
        {
          label: 'Movimientos',
          description:
            'Bandeja de cargos. Captura Flujo A (con CFDI → factura) o Flujo B (sin CFDI → solo movimiento).',
          to: '/cxp/tc/movimientos',
          icon: Receipt,
          permission: PermisosCanonicos.CuentasPorPagarTcLeer,
        },
        {
          label: 'Estados de cuenta',
          description:
            'Conciliación periódica con el archivo del banco. Cierre genera factura agregada.',
          to: '/cxp/tc/estados-cuenta',
          icon: FileSpreadsheet,
          permission: PermisosCanonicos.CuentasPorPagarTcLeer,
        },
      ],
    },
    {
      label: 'Configuración',
      cards: [
        {
          label: 'Tarjetas',
          description:
            'Master de tarjetas: alta, bloqueo, cancelación y gestión de usuarios autorizados.',
          to: '/cxp/tc/tarjetas',
          icon: Wallet,
          permission: PermisosCanonicos.CuentasPorPagarTcAdministrar,
        },
      ],
    },
  ],
};
