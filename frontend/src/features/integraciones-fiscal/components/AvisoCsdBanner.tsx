import { Link } from '@tanstack/react-router';
import { useEstadoCsd } from '@/features/integraciones-fiscal/api/useIntegracionesFiscal';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasAnyPermission, useHasPermission } from '@/lib/auth/useHasPermission';

/**
 * <c>&lt;AvisoCsdBanner/&gt;</c> — aviso en el ERP del vencimiento del CSD
 * (F6.9, R24): a 30 días o menos, vencido o revocado. Lo ven quienes timbran
 * o administran la configuración fiscal. Vencido o revocado, el ERP no timbra.
 */
export function AvisoCsdBanner() {
  const interesado = useHasAnyPermission([
    PermisosCanonicos.IntegracionesFiscalLeer,
    PermisosCanonicos.FacturacionFacturasEmitir,
  ]);
  const puedeConfigurar = useHasPermission(PermisosCanonicos.IntegracionesFiscalLeer);
  const estado = useEstadoCsd(interesado).data;
  if (!estado || estado.estado === 'NoConfigurado' || estado.estado === 'Vigente') return null;

  const vence = estado.venceEn ? new Date(estado.venceEn).toLocaleDateString('es-MX') : '';
  const texto =
    estado.estado === 'Revocado'
      ? 'El CSD de la empresa está revocado: no se puede timbrar hasta capturar uno vigente.'
      : estado.estado === 'Vencido'
        ? `El CSD de la empresa venció el ${vence}: no se puede timbrar hasta capturar el renovado.`
        : `El CSD de la empresa vence el ${vence} (${estado.diasRestantes} ${estado.diasRestantes === 1 ? 'día' : 'días'}). Renuévalo antes para no detener la facturación.`;

  return (
    <div
      role="alert"
      data-print="hidden"
      className={`flex flex-wrap items-center justify-between gap-2 border-b px-4 py-2 text-sm md:px-6 ${
        estado.bloqueaTimbrado ? 'bg-danger-bg text-danger-fg' : 'bg-warning-note-bg text-warning-note-fg'
      }`}
    >
      <span>{texto}</span>
      {puedeConfigurar && (
        <Link to="/admin/integraciones/fiscal" className="font-medium underline">
          Ver certificados
        </Link>
      )}
    </div>
  );
}
