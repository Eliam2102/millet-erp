import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Checkbox } from '@/components/ui/checkbox';
import {
  ProveedorSelector,
  ReporteShell,
  SucursalSelector,
} from '@/components/erp';
import { useReporteCartera } from '@/features/cxp/api/useReportes';
import { backendToShellShape } from '@/features/cxp/lib/reportes-adapter';
import { esApiError } from '@/lib/api';

/**
 * Reporte de cartera por categoría de revisión (FE-F7-PR1).
 * Cruz revisión × antigüedad.
 */
export function ReporteCarteraPage() {
  const [fechaCorte, setFechaCorte] = useState<string>('');
  const [proveedorId, setProveedorId] = useState<string>('');
  const [sucursalId, setSucursalId] = useState<string>('');
  const [soloEnRevision, setSoloEnRevision] = useState(false);
  const [submitted, setSubmitted] = useState(false);

  const query = useReporteCartera(
    {
      fechaCorte: fechaCorte || undefined,
      proveedorId: proveedorId || undefined,
      sucursalId: sucursalId || undefined,
      soloEnRevision: soloEnRevision || undefined,
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
          <div className="space-y-1">
            <Label className="text-xs">Sucursal</Label>
            <SucursalSelector
              value={sucursalId || null}
              onChange={(id) => setSucursalId(id ?? '')}
              placeholder="Todas las sucursales"
              className="w-56"
            />
          </div>
          <div className="flex items-center gap-2 pt-5">
            <Checkbox
              id="soloEnRevision"
              checked={soloEnRevision}
              onCheckedChange={(v) => setSoloEnRevision(v === true)}
            />
            <Label htmlFor="soloEnRevision" className="text-sm">
              Solo en revisión
            </Label>
          </div>
          <Button onClick={() => setSubmitted(true)}>Ejecutar</Button>
        </div>
      }
    />
  );
}
