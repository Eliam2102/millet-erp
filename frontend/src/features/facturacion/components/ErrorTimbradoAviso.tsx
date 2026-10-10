import { Link } from '@tanstack/react-router';
import { TriangleAlert } from 'lucide-react';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { mensajeErrorTimbrado } from '../lib/error-timbrado';

export function ErrorTimbradoAviso({ error }: { error: unknown }) {
  const puedeConsultar = useHasPermission(PermisosCanonicos.IntegracionesFiscalLeer);
  if (error == null) return null;
  return (
    <Alert role="note" aria-live="polite" className="rounded-md border-0 bg-warning-note-bg px-3 py-2.5 text-warning-note-fg [&>svg]:left-3 [&>svg]:top-2.5 [&>svg]:text-warning-note-fg">
      <TriangleAlert className="size-4" aria-hidden="true" />
      <AlertDescription>
        <p>{mensajeErrorTimbrado(error)}</p>
        {esApiError(error) && error.code === 'CONFIG_PAC_NO_DISPONIBLE' && puedeConsultar && (
          <Link to="/admin/integraciones/fiscal" className="font-medium text-brand underline">
            Abrir integraciones fiscales
          </Link>
        )}
      </AlertDescription>
    </Alert>
  );
}
