import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { ReporteShell } from '@/components/erp';
import { TarjetaSelector } from '@/features/cxp/components/TarjetaSelector';
import { useReporteEstadosCuentaTcConsolidado } from '@/features/cxp/api/useReportes';
import { backendToShellShape } from '@/features/cxp/lib/reportes-adapter';
import { EstadoCuentaTcStatus } from '@/features/cxp/api/types';
import { esApiError } from '@/lib/api';

const SENTINEL_ALL = '__all__';

/**
 * Reporte consolidado de estados de cuenta TC (FE-F7-PR1).
 * Muestra totales matched/sugerencias/sin-match + factura banco.
 */
export function ReporteEstadosCuentaTcPage() {
  const [tarjetaId, setTarjetaId] = useState('');
  const [estado, setEstado] = useState<EstadoCuentaTcStatus | undefined>();
  const [periodoDesde, setPeriodoDesde] = useState('');
  const [periodoHasta, setPeriodoHasta] = useState('');
  const [submitted, setSubmitted] = useState(false);

  const query = useReporteEstadosCuentaTcConsolidado(
    {
      tarjetaId: tarjetaId || undefined,
      estado,
      periodoDesde: periodoDesde || undefined,
      periodoHasta: periodoHasta || undefined,
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
            <Label className="text-xs">Estado</Label>
            <Select
              value={estado != null ? String(estado) : SENTINEL_ALL}
              onValueChange={(v) =>
                setEstado(
                  v === SENTINEL_ALL
                    ? undefined
                    : (Number(v) as EstadoCuentaTcStatus),
                )
              }
            >
              <SelectTrigger
                aria-label="Filtrar por estado"
                className="w-48"
              >
                <SelectValue placeholder="Todos" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
                <SelectItem
                  value={String(EstadoCuentaTcStatus.EnConciliacion)}
                >
                  En conciliación
                </SelectItem>
                <SelectItem value={String(EstadoCuentaTcStatus.Conciliado)}>
                  Conciliado
                </SelectItem>
                <SelectItem value={String(EstadoCuentaTcStatus.Cerrado)}>
                  Cerrado
                </SelectItem>
                <SelectItem value={String(EstadoCuentaTcStatus.PagadoBanco)}>
                  Pagado banco
                </SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1">
            <Label className="text-xs">Periodo desde</Label>
            <Input
              type="date"
              value={periodoDesde}
              onChange={(e) => setPeriodoDesde(e.target.value)}
              className="w-40"
            />
          </div>
          <div className="space-y-1">
            <Label className="text-xs">Periodo hasta</Label>
            <Input
              type="date"
              value={periodoHasta}
              onChange={(e) => setPeriodoHasta(e.target.value)}
              className="w-40"
            />
          </div>
          <Button onClick={() => setSubmitted(true)}>Ejecutar</Button>
        </div>
      }
    />
  );
}
