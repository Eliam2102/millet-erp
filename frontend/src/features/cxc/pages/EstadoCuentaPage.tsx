import { PageHeader, EmptyState } from '@/components/erp';
import { ReporteCxc } from '../components/ReporteCxc';

import { useNavigate, useSearch } from '@tanstack/react-router';
import { FileText } from 'lucide-react';
import { Label } from '@/components/ui/label';

import { useEstadoCuentaCliente } from '@/features/cxc/api/useCartera';
import { ClienteSelectorCxc } from '@/features/cxc/components/ClienteSelectorCxc';
import { adaptarReporteCxc } from '@/features/cxc/lib/reporte-cxc-adapter';
import type { EstadoCuentaSearch } from '@/features/cxc/lib/cartera-search-schema';
import { esApiError } from '@/lib/api';

const FROM = '/_app/cxc/estado-cuenta/' as const;

/**
 * <c>Estado de cuenta</c> por cliente (CXC-FE-PR5, ADR-0036): facturas,
 * pagos, NCs y saldo corriente en un solo corte imprimible (impresión
 * limpia: filtros y header llevan <c>data-print="hidden"</c>; el shell
 * ya imprime solo la tabla).
 */
export function EstadoCuentaPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const clienteId = search.clienteId ?? null;

  const query = useEstadoCuentaCliente(clienteId);

  function actualizar(parcial: Partial<EstadoCuentaSearch>) {
    navigate({ to: '/cxc/estado-cuenta', search: { ...search, ...parcial } });
  }

  const filtrosUi = (
    <div className="w-full max-w-md space-y-1">
      <Label className="text-xs text-ink-muted">Cliente *</Label>
      <ClienteSelectorCxc
        value={clienteId}
        onChange={(item) => actualizar({ clienteId: item?.id ?? undefined })}
        placeholder="Elige el cliente del estado de cuenta…"
      />
    </div>
  );

  return (
    <div className="flex min-h-0 flex-col gap-5">
      <header data-print="hidden">
        <PageHeader
          titulo={<>Estado de cuenta</>}
          descripcion={
            <>Movimientos del cliente: facturas, pagos, notas de crédito y saldo corriente.</>
          }
        />
      </header>

      {clienteId == null ? (
        <>
          <div data-print="hidden" className="rounded-md border bg-surface-muted/30 p-3">
            {filtrosUi}
          </div>
          <EmptyState
            icon={<FileText className="h-10 w-10" />}
            title="Elige un cliente para generar su estado de cuenta."
            description="El estado de cuenta siempre se genera por cliente."
          />
        </>
      ) : (
        <ReporteCxc
          reporte={adaptarReporteCxc(query.data)}
          isLoading={query.isLoading}
          error={esApiError(query.error) ? query.error : undefined}
          onRetry={() => query.refetch()}
          filtrosUi={filtrosUi}
          nombreArchivo="estado-cuenta-cxc"
        />
      )}
    </div>
  );
}
