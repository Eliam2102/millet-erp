import { Link } from '@tanstack/react-router';
import { ArrowUpRight, BookCheck, Lock } from 'lucide-react';
import { PageHeader } from '@/components/erp/PageHeader';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

interface ReporteAuditor {
  etiqueta: string;
  modulo: string;
  to: string;
  search?: Record<string, string>;
  permiso: string;
  nota?: string;
}

interface GrupoAuditor {
  titulo: string;
  reportes: ReporteAuditor[];
}

const CONTA = PermisosCanonicos.ContabilidadPolizasLeer;

/**
 * F1-CON-08 · K10.10 (R24): reportes que pide el auditor, en un solo lugar. Cada uno vive en el módulo dueño del dato; aquí solo
 * se enlazan con su permiso. Para el cierre se generan con el mismo periodo.
 */
const GRUPOS: GrupoAuditor[] = [
  {
    titulo: 'Libros y estados financieros',
    reportes: [
      { etiqueta: 'Libro diario', modulo: 'Contabilidad', to: '/contabilidad/reportes/diario', permiso: CONTA },
      { etiqueta: 'Mayor por cuenta', modulo: 'Contabilidad', to: '/contabilidad/reportes/mayor', permiso: CONTA },
      { etiqueta: 'Balanza de comprobación', modulo: 'Contabilidad', to: '/contabilidad/reportes/balanza', permiso: CONTA },
      { etiqueta: 'Balance general', modulo: 'Contabilidad', to: '/contabilidad/reportes/balance-general', permiso: CONTA },
      { etiqueta: 'Estado de resultados', modulo: 'Contabilidad', to: '/contabilidad/reportes/estado-resultados', permiso: CONTA },
    ],
  },
  {
    titulo: 'Integraciones',
    reportes: [
      { etiqueta: 'Integración de ingresos', modulo: 'Contabilidad', to: '/contabilidad/reportes/integracion', search: { tipo: 'Ingresos' }, permiso: CONTA },
      { etiqueta: 'Integración de costo', modulo: 'Contabilidad', to: '/contabilidad/reportes/integracion', search: { tipo: 'Costo' }, permiso: CONTA },
      { etiqueta: 'Integración de gastos', modulo: 'Contabilidad', to: '/contabilidad/reportes/integracion', search: { tipo: 'Gastos' }, permiso: CONTA },
      {
        etiqueta: 'Integración de costo de financiamiento',
        modulo: 'Contabilidad',
        to: '/contabilidad/reportes/integracion',
        search: { tipo: 'Financiamiento' },
        permiso: CONTA,
      },
      { etiqueta: 'Integración de compras', modulo: 'Contabilidad', to: '/contabilidad/reportes/integracion', search: { tipo: 'Compras' }, permiso: CONTA },
      { etiqueta: 'Auxiliar por cliente, proveedor o banco', modulo: 'Contabilidad', to: '/contabilidad/reportes/auxiliar', permiso: CONTA },
    ],
  },
  {
    titulo: 'IVA',
    reportes: [
      { etiqueta: 'Liquidación de IVA', modulo: 'Contabilidad', to: '/contabilidad/reportes/liquidacion-iva', permiso: CONTA },
      {
        etiqueta: 'Relación para IVA de importaciones',
        modulo: 'Comercio exterior',
        to: '/comercio-exterior/relacion-iva',
        permiso: PermisosCanonicos.ComercioExteriorRelacionIvaLeer,
      },
    ],
  },
  {
    titulo: 'Saldos de clientes y proveedores',
    reportes: [
      { etiqueta: 'Antigüedad de saldos de clientes', modulo: 'Cuentas por cobrar', to: '/cxc/cartera', permiso: PermisosCanonicos.CuentasPorCobrarCarteraLeer },
      { etiqueta: 'Estado de cuenta de clientes', modulo: 'Cuentas por cobrar', to: '/cxc/estado-cuenta', permiso: PermisosCanonicos.CuentasPorCobrarCarteraLeer },
      { etiqueta: 'Cartera por proveedor', modulo: 'Cuentas por pagar', to: '/cxp/reportes/cartera', permiso: PermisosCanonicos.CuentasPorPagarReportesCartera },
      {
        etiqueta: 'Antigüedad de saldos de proveedores',
        modulo: 'Cuentas por pagar',
        to: '/cxp/reportes/antiguedad',
        permiso: PermisosCanonicos.CuentasPorPagarReportesAntiguedad,
      },
      {
        etiqueta: 'Conciliación de auxiliares contra contabilidad',
        modulo: 'Contabilidad',
        to: '/contabilidad/reportes/conciliacion-auxiliares',
        permiso: CONTA,
        nota: 'Cuadra los saldos de CxC y CxP contra las cuentas colectivas.',
      },
    ],
  },
  {
    titulo: 'Anticipos',
    reportes: [
      { etiqueta: 'Anticipos de clientes', modulo: 'Cuentas por cobrar', to: '/cxc/anticipos', permiso: PermisosCanonicos.CuentasPorCobrarCarteraLeer },
      {
        etiqueta: 'Estados de facturas de anticipo',
        modulo: 'Facturación',
        to: '/facturacion/reportes/estados-anticipos',
        permiso: PermisosCanonicos.FacturacionReportesLeer,
      },
      {
        etiqueta: 'Antigüedad de anticipos a proveedores',
        modulo: 'Cuentas por pagar',
        to: '/cxp/reportes/antiguedad-anticipos',
        permiso: PermisosCanonicos.CuentasPorPagarReportesAntiguedad,
      },
    ],
  },
  {
    titulo: 'Inventarios y bancos',
    reportes: [
      { etiqueta: 'Saldos de inventario', modulo: 'Almacén', to: '/almacen/saldos', permiso: PermisosCanonicos.AlmacenAlmacenesRead },
      { etiqueta: 'Kárdex', modulo: 'Almacén', to: '/almacen/reportes/kardex', permiso: PermisosCanonicos.AlmacenReportesAlfak },
      {
        etiqueta: 'Auxiliar de bancos (conciliación bancaria)',
        modulo: 'Tesorería',
        to: '/tesoreria/reportes/auxiliar-bancos',
        permiso: PermisosCanonicos.TesoreriaReportesVer,
      },
    ],
  },
];

/** Paquete del auditor (R24): índice de los reportes del cierre con el módulo dueño y acceso según permisos. */
export function PaqueteAuditorPage() {
  const permisos = useAuthStore((s) => s.permisos);
  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        titulo={
          <>
            <BookCheck className="mr-2 inline-block size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            Paquete del auditor
          </>
        }
        descripcion="Los reportes que pide el auditor al cierre, en un solo lugar. Cada uno se genera en su módulo y se exporta a PDF o Excel; usa el mismo periodo en todos."
      />
      <div className="grid gap-4 md:grid-cols-2">
        {GRUPOS.map((g) => (
          <section
            key={g.titulo}
            aria-labelledby={`auditor-${g.titulo}`}
            className="flex flex-col gap-3 rounded-lg bg-surface-card px-5 py-4 shadow-card"
          >
            <h2 id={`auditor-${g.titulo}`} className="text-base font-semibold text-ink">
              {g.titulo}
            </h2>
            <ul className="flex flex-col gap-2">
              {g.reportes.map((r) => {
                const conAcceso = permisos.includes(r.permiso);
                return (
                  <li key={r.etiqueta} className="flex items-start justify-between gap-3">
                    <div className="flex flex-col">
                      {conAcceso ? (
                        <Link
                          to={r.to as never}
                          search={r.search as never}
                          className="inline-flex items-center gap-1 text-sm font-medium text-brand hover:underline"
                        >
                          {r.etiqueta}
                          <ArrowUpRight className="size-3.5" strokeWidth={1.6} aria-hidden="true" />
                        </Link>
                      ) : (
                        <span className="inline-flex items-center gap-1 text-sm text-ink-muted">
                          <Lock className="size-3.5" strokeWidth={1.6} aria-hidden="true" />
                          {r.etiqueta}
                          <span className="sr-only">(sin permiso)</span>
                        </span>
                      )}
                      {r.nota && <span className="text-xs text-ink-muted">{r.nota}</span>}
                    </div>
                    <span className="shrink-0 text-xs text-ink-muted">{r.modulo}</span>
                  </li>
                );
              })}
            </ul>
          </section>
        ))}
      </div>
    </div>
  );
}
