import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { ProveedorSelector, ReporteShell, SucursalSelector } from '@/components/erp';
import { useReportePasivosObras } from '@/features/cxp/api/useReportes';
import { backendToShellShape } from '@/features/cxp/lib/reportes-adapter';
import { esApiError } from '@/lib/api';

export function ReportePasivosObrasPage() {
  const [sucursalId, setSucursalId] = useState('');
  const [obra, setObra] = useState('');
  const [fechaCorte, setFechaCorte] = useState('');
  const [proveedorId, setProveedorId] = useState('');
  const [submitted, setSubmitted] = useState(false);
  const query = useReportePasivosObras(
    {
      sucursalId: sucursalId || undefined,
      obra: obra || undefined,
      fechaCorte: fechaCorte || undefined,
      proveedorId: proveedorId || undefined,
    },
    submitted,
  );
  return (
    <div className="space-y-4 text-sm">
      <p className="text-ink-muted">Filtra los pasivos por la obra registrada en cada factura.</p>
      <ReporteShell
        reporte={query.data ? backendToShellShape(query.data) : undefined}
        isLoading={submitted && query.isLoading}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => query.refetch()}
        filtrosUi={
          <div className="flex flex-wrap items-end gap-3">
            <div className="space-y-1">
              <Label htmlFor="p8-obra">Obra</Label>
              <Input
                id="p8-obra"
                value={obra}
                onChange={(e) => setObra(e.target.value)}
                maxLength={120}
                placeholder="Todas las obras"
              />
            </div>
            <div className="space-y-1">
              <Label>Sucursal</Label>
              <SucursalSelector
                value={sucursalId || null}
                onChange={(id) => setSucursalId(id ?? '')}
                placeholder="Todas las sucursales"
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="p8-corte">Fecha de corte</Label>
              <Input
                id="p8-corte"
                type="date"
                value={fechaCorte}
                onChange={(e) => setFechaCorte(e.target.value)}
              />
            </div>
            <div className="space-y-1">
              <Label>Proveedor</Label>
              <ProveedorSelector
                value={proveedorId || null}
                onChange={(id) => setProveedorId(id ?? '')}
                placeholder="Todos los proveedores"
              />
            </div>
            <Button onClick={() => setSubmitted(true)}>Ejecutar</Button>
          </div>
        }
      />
    </div>
  );
}
