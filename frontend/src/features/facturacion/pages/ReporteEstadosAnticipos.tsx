import { useNavigate, useSearch } from '@tanstack/react-router';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { ReporteShell } from '@/components/erp';
import { ClienteSelector } from '@/features/facturacion/components/selectors/ClienteSelector';
import {
  useEstadosAnticipos,
  type ReporteFila,
} from '@/features/facturacion/api/useReportes';
import { esApiError } from '@/lib/api';
import { EstadoAnticipo } from '@/features/facturacion/api/types';
import type { EstadosAnticiposSearch } from '@/features/facturacion/lib/reportes-search-schema';

const FROM = '/_app/facturacion/reportes/estados-anticipos' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>Reporte de Estados de facturas de anticipo</c> (FE-F9). Anticipos
 * emitidos, facturas vinculadas, NCs de amortización y saldo, vía
 * <c>&lt;ReporteShell&gt;</c> (ADR-0036).
 */
export function ReporteEstadosAnticipos() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();

  const query = useEstadosAnticipos({
    clienteId: search.clienteId,
    estado: search.estado,
  });

  function actualizar(parcial: Partial<EstadosAnticiposSearch>) {
    navigate({
      to: '/facturacion/reportes/estados-anticipos',
      search: { ...search, ...parcial },
    });
  }

  const filtrosUi = (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <Label className="text-xs text-muted-foreground">Estado</Label>
        <Select
          value={search.estado != null ? String(search.estado) : SENTINEL_ALL}
          onValueChange={(v) =>
            actualizar({
              estado:
                v === SENTINEL_ALL ? undefined : (Number(v) as EstadoAnticipo),
            })
          }
        >
          <SelectTrigger aria-label="Filtrar por estado" className="w-44">
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            <SelectItem value={String(EstadoAnticipo.Abierto)}>Abierto</SelectItem>
            <SelectItem value={String(EstadoAnticipo.Amortizado)}>
              Amortizado
            </SelectItem>
            <SelectItem value={String(EstadoAnticipo.Cancelado)}>
              Cancelado
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1">
        <Label className="text-xs text-muted-foreground">Cliente</Label>
        <ClienteSelector
          value={search.clienteId ?? null}
          onChange={(item) => actualizar({ clienteId: item?.id ?? undefined })}
          className="w-72"
        />
      </div>
    </div>
  );

  return (
    <div className="space-y-4 px-4 py-6">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight">
          Estados de facturas de anticipo
        </h1>
        <p className="text-sm text-muted-foreground">
          Anticipos, facturas vinculadas, NCs de amortización y saldo por cliente.
        </p>
      </header>

      <ReporteShell<ReporteFila>
        reporte={query.data}
        isLoading={query.isLoading}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => query.refetch()}
        filtrosUi={filtrosUi}
        nombreArchivo="estados-facturas-anticipo"
      />
    </div>
  );
}
