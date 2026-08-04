import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  ProveedorSelector,
  ReporteShell,
  SucursalSelector,
} from '@/components/erp';
import { useReporteAntiguedadSaldos } from '@/features/cxp/api/useReportes';
import { backendToShellShape } from '@/features/cxp/lib/reportes-adapter';
import { esApiError } from '@/lib/api';

/**
 * Reporte de antigüedad de saldos por proveedor (FE-F7-PR1, ADR-0036).
 * Buckets 0-30 / 31-60 / 61-90 / +90 días.
 */
export function ReporteAntiguedadSaldosPage() {
  const [fechaCorte, setFechaCorte] = useState<string>('');
  const [proveedorId, setProveedorId] = useState<string>('');
  const [sucursalId, setSucursalId] = useState<string>('');
  const [submitted, setSubmitted] = useState(false);

  const query = useReporteAntiguedadSaldos(
    {
      fechaCorte: fechaCorte || undefined,
      proveedorId: proveedorId || undefined,
      sucursalId: sucursalId || undefined,
    },
    submitted,
  );

  return (
    <div className="space-y-4">
      <ReporteShell
        reporte={query.data ? backendToShellShape(query.data) : undefined}
        isLoading={submitted && query.isLoading}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => query.refetch()}
        filtrosUi={
          <div className="flex flex-wrap items-end gap-3">
            <Filtro label="Fecha de corte">
              <Input
                aria-label="Fecha de corte"
                type="date"
                value={fechaCorte}
                onChange={(e) => setFechaCorte(e.target.value)}
                className="w-44"
              />
            </Filtro>
            <Filtro label="Proveedor">
              <ProveedorSelector
                value={proveedorId || null}
                onChange={(id) => setProveedorId(id ?? '')}
                placeholder="Todos los proveedores"
                className="w-56"
              />
            </Filtro>
            <Filtro label="Sucursal">
              <SucursalSelector
                value={sucursalId || null}
                onChange={(id) => setSucursalId(id ?? '')}
                placeholder="Todas las sucursales"
                className="w-56"
              />
            </Filtro>
            <Button onClick={() => setSubmitted(true)}>Ejecutar</Button>
          </div>
        }
      />
    </div>
  );
}

function Filtro({
  label,
  children,
}: {
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1">
      <Label className="text-xs">{label}</Label>
      {children}
    </div>
  );
}
