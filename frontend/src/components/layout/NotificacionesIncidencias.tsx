import { Link } from '@tanstack/react-router';
import { ClipboardList } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useResumenMisIncidencias } from '@/features/incidencias/api/useIncidencias';

/**
 * Acceso a «Mis incidencias» en el topbar con el conteo de pendientes asignadas (abiertas + en
 * atención). Sin pendientes no se muestra, igual que los avisos de Compras y Almacén.
 */
export function NotificacionesIncidencias() {
  const usuarioId = useAuthStore((s) => s.user?.id);
  const empresaId = useAuthStore((s) => s.currentEmpresaId);
  const resumen = useResumenMisIncidencias(Boolean(usuarioId && empresaId));
  const abiertas = resumen.data?.abiertas ?? 0;
  const pendientes = abiertas + (resumen.data?.enAtencion ?? 0);
  if (pendientes === 0) return null;
  return (
    <Button variant="ghost" size="icon" className="relative" asChild
      aria-label={`Mis incidencias: ${pendientes} pendientes, ${abiertas} sin atender`}>
      <Link to="/incidencias">
        <ClipboardList className="h-4 w-4" />
        <span className={`absolute right-0 top-0 rounded-full px-1 text-2xs tabular-nums ${abiertas > 0 ? 'bg-danger-bg text-danger-fg' : 'bg-neutral-bg text-ink-secondary'}`}>
          {pendientes}
        </span>
      </Link>
    </Button>
  );
}
