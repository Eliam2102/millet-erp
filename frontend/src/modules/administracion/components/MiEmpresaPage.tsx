import { useAuthStore } from '@/lib/auth/auth-store';
import { rutaPermitida } from '@/lib/nav';
import { EmpresaDetallePorId } from './EmpresaDetalle';

export function MiEmpresaPage() {
  const empresaId = useAuthStore((s) => s.currentEmpresaId);
  const permisos = useAuthStore((s) => s.permisos);
  if (!rutaPermitida('/admin/mi-empresa', permisos)) {
    return (
      <p role="alert" className="text-sm text-ink-muted">
        No tienes permiso para consultar Mi empresa.
      </p>
    );
  }
  if (!empresaId) {
    return (
      <p role="alert" className="text-sm text-ink-muted">
        No hay empresa en la sesión. Vuelve a iniciar sesión.
      </p>
    );
  }
  return <EmpresaDetallePorId key={empresaId} id={empresaId} miEmpresa />;
}
