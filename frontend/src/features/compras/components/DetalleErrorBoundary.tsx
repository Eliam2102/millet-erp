import { Lock, Compass } from 'lucide-react';
import { Link } from '@tanstack/react-router';
import { Button } from '@/components/ui/button';
import { EmptyState } from '@/components/erp';
import { ErrorState } from '@/components/erp/feedback/ErrorState';
import { esApiError, type ApiError } from '@/lib/api';
import {
  DEFAULT_BANDEJA_SEARCH,
  type BandejaSearch,
} from '@/features/compras/lib/bandeja-search-schema';

/**
 * Render dedicado para los 3 fallos canónicos del detalle (P3) según
 * doc 05 §13.6:
 *
 * <list>
 *   <item><b>403</b>: usuario sin permiso para ver esta RQ. Página
 *   completa con icono de candado + CTA volver a bandeja. Loguea
 *   <c>traceId</c> en consola para que sea bug-ueable (es bug de UI
 *   si llega).</item>
 *   <item><b>404</b>: RQ inexistente o de otra empresa. Página neutra
 *   sin distinguir entre los dos casos (no leak de existencia
 *   cross-empresa).</item>
 *   <item><b>5xx u otros</b>: <c>&lt;ErrorState/&gt;</c> con
 *   reintentar.</item>
 * </list>
 */
export interface DetalleErrorBoundaryProps {
  error: unknown;
  onRetry?: () => void;
  /** Search params de la bandeja para preservar al volver. Default = vacío. */
  bandejaSearch?: BandejaSearch;
}

export function DetalleErrorBoundary({
  error,
  onRetry,
  bandejaSearch = DEFAULT_BANDEJA_SEARCH,
}: DetalleErrorBoundaryProps) {
  if (esApiError(error)) {
    if (error.status === 403) {
      return <Page403 error={error} bandejaSearch={bandejaSearch} />;
    }
    if (error.status === 404) {
      return <Page404 bandejaSearch={bandejaSearch} />;
    }
  }

  // 5xx, network, etc.
  const problem = esApiError(error) ? error.problem : undefined;
  return (
    <div className="space-y-4">
      <ErrorState problem={problem} onRetry={onRetry} />
    </div>
  );
}

function Page403({
  error,
  bandejaSearch,
}: {
  error: ApiError;
  bandejaSearch: BandejaSearch;
}) {
  // Loguear para diagnóstico — un 403 que llega aquí suele ser bug de UI
  // (botón visible para usuario sin permiso). Ver doc 05 §13.6.
  console.warn(
    '[Compras P3] 403 recibido del backend:',
    error.traceId ?? '(sin traceId)',
    error.problem,
  );

  return (
    <div className="space-y-4">
      <EmptyState
        icon={<Lock className="h-12 w-12" />}
        title="No tienes permiso para ver esta requisición."
        description={
          'Si crees que es un error, contacta a tu administrador.' +
          (error.traceId ? ` Código de soporte: ${error.traceId}.` : '')
        }
        action={
          <Button asChild>
            <Link to="/compras/requisiciones" search={bandejaSearch}>
              Volver a bandeja
            </Link>
          </Button>
        }
      />
    </div>
  );
}

function Page404({ bandejaSearch }: { bandejaSearch: BandejaSearch }) {
  return (
    <div className="space-y-4">
      <EmptyState
        icon={<Compass className="h-12 w-12" />}
        title="Esta requisición no existe o no pertenece a tu empresa actual."
        description="Verifica el folio o cambia de empresa si tienes acceso a varias."
        action={
          <Button asChild>
            <Link to="/compras/requisiciones" search={bandejaSearch}>
              Volver a bandeja
            </Link>
          </Button>
        }
      />
    </div>
  );
}
