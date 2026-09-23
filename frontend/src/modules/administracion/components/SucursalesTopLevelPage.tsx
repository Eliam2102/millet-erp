import { SucursalesLayout } from '@/modules/administracion/components/SucursalesLayout';

/**
 * <c>/admin/sucursales</c> — home top-level de Sucursales (ADR-0051).
 * Implementa el patrón Master-Detail (P3 del ERP) análogo a EmpresasLayout.
 */
export function SucursalesTopLevelPage() {
  return <SucursalesLayout idActivo={null} />;
}
