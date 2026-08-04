import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  ProveedorSelector,
  ReporteShell,
  SucursalSelector,
} from '@/components/erp';
import { useReportePasivosObras } from '@/features/cxp/api/useReportes';
import { backendToShellShape } from '@/features/cxp/lib/reportes-adapter';
import { esApiError } from '@/lib/api';

/**
 * Reporte de pasivos por sucursal/obra (FE-F7-PR1).
 * Requiere <c>sucursalId</c> obligatorio. Para integración futura
 * con módulo Obras.
 */
export function ReportePasivosObrasPage() {
  const [sucursalId, setSucursalId] = useState('');
  const [fechaCorte, setFechaCorte] = useState('');
  const [proveedorId, setProveedorId] = useState('');
  const [submitted, setSubmitted] = useState(false);

  const query = useReportePasivosObras(
    {
      sucursalId,
      fechaCorte: fechaCorte || undefined,
      proveedorId: proveedorId || undefined,
    },
    submitted && sucursalId !== '',
  );

  const sucursalRequerida = submitted && !sucursalId;

  return (
    <div className="space-y-4">
      {sucursalRequerida && (
        <div className="rounded-md border border-destructive/40 bg-destructive/10 px-3 py-2 text-sm text-destructive">
          La sucursal es obligatoria para este reporte.
        </div>
      )}
      <ReporteShell
        reporte={query.data ? backendToShellShape(query.data) : undefined}
        isLoading={submitted && query.isLoading}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => query.refetch()}
        filtrosUi={
          <div className="flex flex-wrap items-end gap-3">
            <div className="space-y-1">
              <Label className="text-xs">
                Sucursal / Obra <span className="text-destructive">*</span>
              </Label>
              <SucursalSelector
                value={sucursalId || null}
                onChange={(id) => setSucursalId(id ?? '')}
                placeholder="Selecciona sucursal"
                className="w-56"
              />
            </div>
            <div className="space-y-1">
              <Label className="text-xs">Fecha de corte</Label>
              <Input
                type="date"
                value={fechaCorte}
                onChange={(e) => setFechaCorte(e.target.value)}
                className="w-44"
              />
            </div>
            <div className="space-y-1">
              <Label className="text-xs">Proveedor</Label>
              <ProveedorSelector
                value={proveedorId || null}
                onChange={(id) => setProveedorId(id ?? '')}
                placeholder="Todos los proveedores"
                className="w-56"
              />
            </div>
            <Button onClick={() => setSubmitted(true)}>Ejecutar</Button>
          </div>
        }
      />
    </div>
  );
}
