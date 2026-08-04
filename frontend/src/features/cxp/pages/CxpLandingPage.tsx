import { AppLauncherCard } from '@/components/layout/AppLauncherCard';
import { useAuthStore } from '@/lib/auth/auth-store';
import { filtrarModuloPorPermisos, type NavModulo } from '@/lib/nav';
import {
  BarChart3,
  ClipboardCheck,
  CreditCard,
  FileBadge,
  FileText,
  HandCoins,
  Inbox,
  Plane,
  Receipt,
  ReceiptText,
  Sliders,
  Users,
  Wallet,
} from 'lucide-react';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Landing del módulo Cuentas por Pagar — <c>/cxp</c>. Mismo patrón que
 * Almacén / Compras: el sidebar abre el modal con cards y, si el
 * usuario llega por URL directa, esta landing renderiza el mismo
 * conjunto.
 *
 * <para>El gate de permiso fino está en cada card; aquí solo
 * verificamos que el usuario tenga al menos uno de los permisos
 * <c>cuentas_por_pagar.*</c>. Sin cards visibles → mensaje neutro
 * (no <c>AccessDenied</c>, porque <c>/_app</c> ya gateó la
 * autenticación; "0 cards visibles" es el equivalente del modal vacío
 * en el AppLauncher).</para>
 */
export function CxpLandingPage() {
  const permisos = useAuthStore((s) => s.permisos);
  const filtrado = filtrarModuloPorPermisos(moduloCxpLanding, permisos);
  const hayCards = filtrado.secciones.some((s) => s.cards.length > 0);

  return (
    <div className="mx-auto max-w-5xl space-y-6 px-4 py-8">
      <header className="space-y-1">
        <h1 className="flex items-center gap-2 text-2xl font-semibold">
          <CreditCard className="h-6 w-6 text-primary" />
          Cuentas por Pagar
        </h1>
        <p className="text-sm text-muted-foreground">
          Ciclo del pasivo proveedor: CFDIs, facturas, notas de crédito,
          comprobaciones, viáticos, tarjetas corporativas y reportes.
        </p>
      </header>

      {!hayCards && (
        <div
          className="rounded-md bg-muted/50 px-4 py-6 text-center text-sm text-muted-foreground"
          data-testid="cxp-sin-cards"
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
 * <c>nav.ts</c>. Idéntico shape al usado en <c>nav.ts</c>.
 */
const moduloCxpLanding: NavModulo = {
  moduloId: 'cxp',
  label: 'Cuentas por Pagar',
  icon: CreditCard,
  secciones: [
    {
      label: 'Operación',
      cards: [
        {
          label: 'CFDIs recibidos',
          description:
            'Bandeja de CFDIs recibidos. Marcar duplicados, descartar y abrir captura.',
          to: '/cxp/cfdis',
          icon: FileBadge,
          permission: PermisosCanonicos.CuentasPorPagarCfdisLeer,
        },
        {
          label: 'Facturas',
          description:
            'Bandeja general — captura, conciliación, revisión y autorización.',
          to: '/cxp/facturas',
          icon: ReceiptText,
          permission: PermisosCanonicos.CuentasPorPagarFacturasLeer,
        },
        {
          label: 'Revisión por área',
          description:
            'Facturas asignadas a tu área en espera de liberación. Indicadores de SLA.',
          to: '/cxp/revision',
          icon: Inbox,
          permission: PermisosCanonicos.CuentasPorPagarFacturasLiberarRevision,
        },
        {
          label: 'Notas de crédito',
          description:
            'NC recibidas del proveedor. Aplicación a facturas y monitoreo de NC en espera.',
          to: '/cxp/notas-credito',
          icon: Receipt,
          permission: PermisosCanonicos.CuentasPorPagarNotasCreditoLeer,
        },
        {
          label: 'Anticipos',
          description:
            'Anticipos a proveedores (CFDI serie FANT). Captura y aplicación a facturas.',
          to: '/cxp/anticipos',
          icon: HandCoins,
          permission: PermisosCanonicos.CuentasPorPagarAnticiposLeer,
        },
        {
          label: 'Notas de cargo',
          description:
            'Cargos internos al proveedor (devoluciones, garantías, fletes). Autorización Dirección.',
          to: '/cxp/notas-cargo',
          icon: FileText,
          permission: PermisosCanonicos.CuentasPorPagarNotasCargoLeer,
        },
        {
          label: 'Comprobaciones',
          description:
            'Caja Chica y Aduanales (doble autorización Comercio Exterior + DF).',
          to: '/cxp/comprobaciones',
          icon: ClipboardCheck,
          permission: PermisosCanonicos.CuentasPorPagarComprobacionesLeer,
        },
        {
          label: 'Viáticos',
          description:
            'Solicitudes electrónicas: empleado solicita, jefe autoriza, CxP libera al regreso.',
          to: '/cxp/viaticos',
          icon: Plane,
          permission: PermisosCanonicos.CuentasPorPagarViaticosLeer,
        },
        {
          label: 'Tarjetas de crédito',
          description:
            'TC empresariales: movimientos, conciliación con estado de cuenta y cierre.',
          to: '/cxp/tc',
          icon: CreditCard,
          permission: PermisosCanonicos.CuentasPorPagarTcLeer,
        },
      ],
    },
    {
      label: 'Reportes',
      cards: [
        {
          label: 'Antigüedad de saldos',
          description:
            'Cartera viva por buckets 0-30 / 31-60 / 61-90 / +90 días. Exportable PDF/Excel.',
          to: '/cxp/reportes/antiguedad',
          icon: BarChart3,
          permission: PermisosCanonicos.CuentasPorPagarReportesAntiguedad,
        },
        {
          label: 'Cartera por proveedor',
          description:
            'Saldo total y por bucket por proveedor. Drill-down a facturas vivas.',
          to: '/cxp/reportes/cartera',
          icon: Wallet,
          permission: PermisosCanonicos.CuentasPorPagarReportesCartera,
        },
        {
          label: 'Estados de cuenta TC',
          description:
            'Reporte agregado de estados de cuenta de tarjetas con sus pasivos asociados.',
          to: '/cxp/reportes/tc',
          icon: CreditCard,
          permission: PermisosCanonicos.CuentasPorPagarReportesTc,
        },
      ],
    },
    {
      label: 'Configuración',
      cards: [
        {
          label: 'Aprobadores',
          description:
            'Catálogo de aprobadores con monto máximo por tipo de gasto (Caja chica / Viáticos / TC / Otros sin OC).',
          to: '/cxp/admin/aprobadores',
          icon: Users,
          permission:
            PermisosCanonicos.CuentasPorPagarCatalogosAprobadoresAdministrar,
        },
        {
          label: 'Políticas de viáticos',
          description:
            'Tabuladores por puesto y destino (Nacional / Internacional).',
          to: '/cxp/admin/politicas-viaticos',
          icon: Sliders,
          permission:
            PermisosCanonicos.CuentasPorPagarCatalogosPoliticasAdministrar,
        },
        // PLATFORM-TODO(<CxpTolerancias>): vivirá en Datos Maestros.
      ],
    },
  ],
};
