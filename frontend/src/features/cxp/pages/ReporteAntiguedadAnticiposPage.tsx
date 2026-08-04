import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { ProveedorSelector, ReporteShell } from '@/components/erp';
import { useReporteAntiguedadAnticipos } from '@/features/cxp/api/useReportes';
import { backendToShellShape } from '@/features/cxp/lib/reportes-adapter';
import { esApiError } from '@/lib/api';

/**
 * Reporte de antigüedad de anticipos por proveedor (FE-F7-PR1).
 * Muestra entregado/amortizado/saldo amortizable por anticipo.
 */
export function ReporteAntiguedadAnticiposPage() {
  const [fechaCorte, setFechaCorte] = useState<string>('');
  const [proveedorId, setProveedorId] = useState<string>('');
  const [submitted, setSubmitted] = useState(false);

  const query = useReporteAntiguedadAnticipos(
    {
      fechaCorte: fechaCorte || undefined,
      proveedorId: proveedorId || undefined,
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
          <Button onClick={() => setSubmitted(true)}>Ejecutar</Button>
        </div>
      }
    />
  );
}
