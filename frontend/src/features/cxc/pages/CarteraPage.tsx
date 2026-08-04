import { useNavigate, useSearch } from '@tanstack/react-router';
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
import { useAntiguedadSaldos } from '@/features/cxc/api/useCartera';
import { ClienteSelectorCxc } from '@/features/cxc/components/ClienteSelectorCxc';
import { adaptarReporteCxc } from '@/features/cxc/lib/reporte-cxc-adapter';
import { MONEDAS_LINEA_CREDITO } from '@/features/cxc/api/types';
import type { CarteraSearch } from '@/features/cxc/lib/cartera-search-schema';
import { esApiError } from '@/lib/api';

const FROM = '/_app/cxc/cartera/' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>Antigüedad de saldos</c> (CXC-FE-PR5, ADR-0036). Reemplaza el
 * corte semanal de 45-90 min en Excel: cartera viva agrupada por
 * cliente + moneda (nunca convierte) con columna por_vencer + buckets
 * de días vencidos CONFIGURABLES — las columnas llegan dinámicas del
 * backend vía <c>adaptarReporteCxc</c>, el FE no las hardcodea.
 */
export function CarteraPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();

  const query = useAntiguedadSaldos({
    fechaCorte: search.fechaCorte,
    clienteId: search.clienteId,
    moneda: search.moneda,
  });

  function actualizar(parcial: Partial<CarteraSearch>) {
    navigate({ to: '/cxc/cartera', search: { ...search, ...parcial } });
  }

  const filtrosUi = (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <Label className="text-xs text-muted-foreground">Fecha de corte</Label>
        <Input
          type="date"
          value={search.fechaCorte ?? ''}
          onChange={(e) =>
            actualizar({ fechaCorte: e.target.value || undefined })
          }
        />
      </div>
      <div className="w-72 space-y-1">
        <Label className="text-xs text-muted-foreground">Cliente</Label>
        <ClienteSelectorCxc
          value={search.clienteId ?? null}
          onChange={(item) =>
            actualizar({ clienteId: item?.id ?? undefined })
          }
          placeholder="Todos los clientes"
        />
      </div>
      <div className="space-y-1">
        <Label className="text-xs text-muted-foreground">Moneda</Label>
        <Select
          value={search.moneda ?? SENTINEL_ALL}
          onValueChange={(v) =>
            actualizar({
              moneda:
                v === SENTINEL_ALL ? undefined : (v as CarteraSearch['moneda']),
            })
          }
        >
          <SelectTrigger aria-label="Filtrar por moneda" className="w-32">
            <SelectValue placeholder="Todas" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todas</SelectItem>
            {MONEDAS_LINEA_CREDITO.map((m) => (
              <SelectItem key={m} value={m}>
                {m}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
    </div>
  );

  return (
    <div className="space-y-4 px-4 py-6">
      <header data-print="hidden">
        <h1 className="text-2xl font-semibold tracking-tight">
          Antigüedad de saldos
        </h1>
        <p className="text-sm text-muted-foreground">
          Cartera viva por cliente y moneda: por vencer + buckets de días
          vencidos (saldo vencido = suma de buckets).
        </p>
      </header>

      <ReporteShell
        reporte={adaptarReporteCxc(query.data)}
        isLoading={query.isLoading}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => query.refetch()}
        filtrosUi={filtrosUi}
        nombreArchivo="antiguedad-saldos-cxc"
      />
    </div>
  );
}
