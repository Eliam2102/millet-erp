import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { ReporteShell, UsuarioSelector } from '@/components/erp';
import { TarjetaSelector } from '@/features/cxp/components/TarjetaSelector';
import { useReporteMovimientosTcPendientes } from '@/features/cxp/api/useReportes';
import { backendToShellShape } from '@/features/cxp/lib/reportes-adapter';
import { esApiError } from '@/lib/api';

/**
 * Reporte de movimientos TC en estado <c>Registrado</c> pendientes de
 * conciliar con estado de cuenta del banco (FE-F7-PR1).
 */
export function ReporteMovimientosTcPendientesPage() {
  const [tarjetaId, setTarjetaId] = useState('');
  const [usuarioQueUsoId, setUsuarioQueUsoId] = useState('');
  const [fechaDesde, setFechaDesde] = useState('');
  const [fechaHasta, setFechaHasta] = useState('');
  const [submitted, setSubmitted] = useState(false);

  const query = useReporteMovimientosTcPendientes(
    {
      tarjetaId: tarjetaId || undefined,
      usuarioQueUsoId: usuarioQueUsoId || undefined,
      fechaDesde: fechaDesde || undefined,
      fechaHasta: fechaHasta || undefined,
    },
    submitted,
  );

  return (
    <ReporteShell
      reporte={query.data ? backendToShellShape(query.data) : undefined}
      isLoading={submitted && query.isLoading}
      error={esApiError(query.error) ? query.error : undefined}
      onRetry={() => query.refetch()}
      filtrosUi={
        <div className="flex flex-wrap items-end gap-3">
          <div className="space-y-1">
            <Label className="text-xs">Tarjeta</Label>
            <TarjetaSelector
              value={tarjetaId || null}
              onChange={(id) => setTarjetaId(id ?? '')}
              placeholder="Todas las tarjetas"
              className="w-56"
              soloActivas={false}
            />
          </div>
          <div className="space-y-1">
            <Label className="text-xs">Usuario</Label>
            <UsuarioSelector
              value={usuarioQueUsoId || null}
              onChange={(id) => setUsuarioQueUsoId(id ?? '')}
              placeholder="Todos los usuarios"
              className="w-56"
            />
          </div>
          <div className="space-y-1">
            <Label className="text-xs">Desde</Label>
            <Input
              type="date"
              value={fechaDesde}
              onChange={(e) => setFechaDesde(e.target.value)}
              className="w-40"
            />
          </div>
          <div className="space-y-1">
            <Label className="text-xs">Hasta</Label>
            <Input
              type="date"
              value={fechaHasta}
              onChange={(e) => setFechaHasta(e.target.value)}
              className="w-40"
            />
          </div>
          <Button onClick={() => setSubmitted(true)}>Ejecutar</Button>
        </div>
      }
    />
  );
}
