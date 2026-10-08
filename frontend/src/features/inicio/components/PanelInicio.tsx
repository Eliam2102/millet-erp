import { ShieldAlert } from 'lucide-react';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EncabezadoInicio } from './EncabezadoInicio';
import { MisPendientes } from './MisPendientes';
import { AccesosRapidos } from './AccesosRapidos';

export function PanelInicio() {
  const sinEmpresas = useAuthStore((s) => s.empresas.length === 0);
  return (
    <div className="space-y-6 text-sm lg:p-2">
      <EncabezadoInicio />
      {sinEmpresas && (
        <Alert className="border-line bg-warning-bg text-warning-fg [&>svg]:text-warning-fg">
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
      <div className="grid grid-cols-1 items-start gap-6 lg:grid-cols-[minmax(0,1.55fr)_minmax(0,1fr)]">
        <MisPendientes />
        <AccesosRapidos />
      </div>
    </div>
  );
}
