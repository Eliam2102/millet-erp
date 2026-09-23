import { Lock } from 'lucide-react';
import { EmptyState, ErrorState } from '@/components/erp';
import { esApiError } from '@/lib/api';

/**
 * Estado de error compartido por las 3 tabs de <c>SucursalDetalle</c>
 * (Departamentos, Puestos, Usuarios) cuando su query de "asignados a
 * la sucursal" falla. Distingue el caso 403 <c>SUCURSAL_NO_ASOCIADA</c>
 * (guard de pertenencia, F1-ADM-01 Fase 2 sección C) del resto —
 * mismo criterio que <c>DetalleErrorBoundary</c> de Compras
 * (doc 05 §13.6), simplificado a nivel de tab (sin CTA de navegación,
 * el usuario ya está en la página correcta).
 */
export interface SucursalTabErrorStateProps {
  error: unknown;
  onRetry?: () => void;
}

export function SucursalTabErrorState({
  error,
  onRetry,
}: SucursalTabErrorStateProps) {
  if (esApiError(error) && error.status === 403) {
    return (
      <EmptyState
        icon={<Lock className="h-10 w-10" />}
        title="No tienes acceso a esta sucursal."
        description={
          'Solo puedes gestionar sucursales a las que estás asociado, ' +
          'o para las que tengas el permiso de gestión correspondiente.' +
          (error.traceId ? ` Código: ${error.traceId}.` : '')
        }
      />
    );
  }

  const problem = esApiError(error) ? error.problem : undefined;
  return <ErrorState problem={problem} onRetry={onRetry} />;
}
