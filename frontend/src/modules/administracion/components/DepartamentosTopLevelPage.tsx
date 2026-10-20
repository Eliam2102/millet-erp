import { PageHeader } from '@/components/erp/PageHeader';
import { Link } from '@tanstack/react-router';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { useEmpresa } from '@/modules/administracion/api';
import { esApiError } from '@/lib/api';
import { useAuth } from '@/lib/auth/useAuth';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { DepartamentosPanel } from '@/modules/administracion/components/DepartamentosPanel';

/**
 * <c>/admin/departamentos</c> — catálogo top-level de Departamentos
 * (ADR-0051). Espejo de <c>SucursalesTopLevelPage</c>: mismo modelo de
 * datos (bundle de <c>GET /admin/empresas/{id}</c> contra la empresa
 * activa de la sesión, sin selector de empresa). La asociación de un
 * departamento del catálogo a una sucursal específica vive en el tab
 * "Departamentos" de <c>/admin/sucursales/$id</c>
 * (<c>SucursalDepartamentosTab</c>) — esta página gestiona el catálogo
 * en sí (alta/edición), no las asignaciones.
 */
export function DepartamentosTopLevelPage() {
  const { currentEmpresaId } = useAuth();
  const empresaQuery = useEmpresa(currentEmpresaId);
  const canVerDatosEmpresa = useHasPermission(PermisosCanonicos.AdminEmpresasLeer);

  if (empresaQuery.isError) {
    const problem = esApiError(empresaQuery.error) ? empresaQuery.error.problem : undefined;
    return (
      <div className="">
        <ErrorState problem={problem} onRetry={() => empresaQuery.refetch()} />
      </div>
    );
  }

  if (empresaQuery.isLoading || empresaQuery.data == null) {
    return (
      <div className="space-y-5">
        <TableSkeleton
          rows={4}
          columns={[{ width: 'w-48' }, { width: 'w-64' }, { width: 'w-32' }]}
        />
      </div>
    );
  }

  const { empresa, departamentos } = empresaQuery.data;

  return (
    <div className="space-y-5">
      <PageHeader
        titulo={<>Departamentos</>}
        descripcion={
          <>
            Catálogo organizacional de {empresa.razonSocial} — se asigna por sucursal desde el
            detalle de cada sucursal.
          </>
        }
        acciones={
          <>
            {canVerDatosEmpresa && (
              <Link
                to="/admin/empresas/$id"
                params={{ id: empresa.id }}
                className="text-xs text-ink-muted hover:text-ink hover:underline"
              >
                Datos de la empresa (avanzado) →
              </Link>
            )}
          </>
        }
      />

      <DepartamentosPanel empresaId={empresa.id} departamentos={departamentos} />
    </div>
  );
}
