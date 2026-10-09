import { ShieldAlert } from 'lucide-react';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EncabezadoInicio } from './EncabezadoInicio';
import { MisPendientes } from './MisPendientes';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useResumenInicio } from '../api/useResumenInicio';
import { IndicadoresInicio } from './IndicadoresInicio';
import { RecientesInicio } from './RecientesInicio';
import { AccesosRapidos } from './AccesosRapidos';

export function PanelInicio() {
  const sinEmpresas = useAuthStore((s) => s.empresas.length === 0);
  const usuarioId = useAuthStore((s) => s.user?.id);
  const empresaId = useAuthStore((s) => s.currentEmpresaId);
  const puedeVerActividad = useAuthStore((s) => s.permisos.includes(P.AdminAuditoriaLeer));
  const resumen = useResumenInicio();
  return (
    <div className="grid min-w-0 grid-cols-1 items-start gap-6 p-4 font-sans text-sm text-ink lg:grid-cols-[minmax(0,1.55fr)_minmax(0,1fr)] lg:grid-rows-[auto_auto_1fr] lg:px-8 lg:py-7">
      <EncabezadoInicio />
      {sinEmpresas && (
        <Alert className="lg:col-span-2 border-line bg-warning-bg text-warning-fg [&>svg]:text-warning-fg">
          <ShieldAlert className="size-4" strokeWidth={1.6} aria-hidden="true" />
          <AlertTitle className="font-semibold">
            Cuenta activa sin asignaciones de empresa o rol
          </AlertTitle>
          <AlertDescription className="text-xs">
            Tu usuario aún no tiene empresas o roles asignados dentro del ERP. Solicita a tu
            administrador que configure tus permisos desde{' '}
            <strong>Administración &gt; Usuarios</strong>.
          </AlertDescription>
        </Alert>
      )}
      <IndicadoresInicio items={resumen.kpis} />
      <MisPendientes resumen={resumen} />
      <div className="min-w-0 space-y-6">
        {puedeVerActividad && usuarioId && empresaId && (
          <RecientesInicio
            key={`${usuarioId}:${empresaId}`}
            usuarioId={usuarioId}
            empresaId={empresaId}
          />
        )}
        <AccesosRapidos />
      </div>
    </div>
  );
}
