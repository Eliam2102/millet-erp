import { useState } from 'react';
import { ShieldAlert } from 'lucide-react';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EncabezadoInicio } from './EncabezadoInicio';
import { MisPendientes } from './MisPendientes';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useResumenInicio } from '../api/useResumenInicio';
import { IndicadoresInicio } from './IndicadoresInicio';
import { RecientesInicio } from './RecientesInicio';
import { VisualesInicio } from './VisualesInicio';
import { CrearRapido } from './CrearRapido';
import { usePiezasInicio, type PeriodoInicio } from '../api/usePiezasInicio';
import { Button } from '@/components/ui/button';

export function PanelInicio() {
  const sinEmpresas = useAuthStore((s) => s.empresas.length === 0);
  const usuarioId = useAuthStore((s) => s.user?.id);
  const empresaId = useAuthStore((s) => s.currentEmpresaId);
  const puedeVerActividad = useAuthStore((s) => s.permisos.includes(P.AdminAuditoriaLeer));
  const resumen = useResumenInicio();
  const [periodo, setPeriodo] = useState<PeriodoInicio>('Mes');
  const piezas = usePiezasInicio(periodo);
  return (
    <div data-inicio-layout="cuatro-franjas" className="grid min-w-0 grid-cols-1 gap-3 p-4 font-sans text-sm text-ink lg:h-[calc(100dvh-3.5rem)] lg:min-h-0 lg:grid-rows-[auto_auto_minmax(0,1fr)_auto] lg:overflow-hidden lg:px-6 lg:py-5">
      <div className="min-w-0 space-y-2">
      <div className="flex flex-wrap items-center justify-between gap-3">
      <EncabezadoInicio urgentes={resumen.filas.filter(f => f.prioridad.urgente).reduce((n, f) => n + f.prioridad.total, 0)} pendientes={resumen.filas.reduce((n, f) => n + f.prioridad.total, 0)} cargando={resumen.cargando} />
      {piezas.visuales.some(v => v.pieza.id !== 'flujo') && <div role="group" aria-label="Periodo de los circuitos" className="flex gap-2">
        {(['Hoy', 'Semana', 'Mes'] as const).map(p => <Button key={p} variant={p === periodo ? 'secondary' : 'outline'} size="sm" aria-pressed={p === periodo} onClick={() => setPeriodo(p)}>{p}</Button>)}
      </div>}
      </div>
      {sinEmpresas && (
        <Alert className="border-line bg-warning-bg text-warning-fg [&>svg]:text-warning-fg">
          <ShieldAlert className="size-4" strokeWidth={1.6} aria-hidden="true" />
          <AlertTitle className="font-semibold">
            Cuenta activa sin asignaciones de empresa o rol
          </AlertTitle>
          <AlertDescription className="text-xs">
            Tu usuario aún no tiene empresas o roles asignados dentro del ERP. Solicita a tu
            administrador que configure tus permisos desde{' '}
            <strong>Administración &gt; Empleados</strong>.
          </AlertDescription>
        </Alert>
      )}
      </div>
      <div className="min-w-0"><IndicadoresInicio items={piezas.indicadores} /></div>
      <div className="grid min-h-0 min-w-0 gap-3 lg:grid-cols-[minmax(0,1.55fr)_minmax(0,1fr)]">
        <MisPendientes resumen={resumen} />
        <VisualesInicio items={piezas.visuales} />
      </div>
      <div className={`grid min-w-0 gap-3 ${puedeVerActividad ? 'lg:grid-cols-[minmax(0,1.55fr)_minmax(0,1fr)]' : 'lg:grid-cols-1'}`}>
        {puedeVerActividad && usuarioId && empresaId && (
          <RecientesInicio
            key={`${usuarioId}:${empresaId}`}
            usuarioId={usuarioId}
            empresaId={empresaId}
          />
        )}
        <CrearRapido />
      </div>
    </div>
  );
}
