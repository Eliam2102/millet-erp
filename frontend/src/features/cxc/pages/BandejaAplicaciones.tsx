import { useMemo } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { FileText, Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { useClientesLookupCxc } from '@/features/cxc/api/useLineasCredito';
import { usePropuestasAplicacion } from '@/features/cxc/api/useAplicaciones';
import { ChipEstadoPropuesta } from '@/features/cxc/components/ChipEstadoPropuesta';
import { useNuevaPropuesta } from '@/features/cxc/components/nueva-propuesta-context';
import { EstadoPropuestaAplicacion } from '@/features/cxc/api/types';
import {
  ETIQUETA_ESTADO_PROPUESTA,
  formatoMonto,
} from '@/features/cxc/lib/glosario';
import type { AplicacionesSearch } from '@/features/cxc/lib/aplicaciones-search-schema';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';

const FROM = '/_app/cxc/aplicaciones/' as const;
const SENTINEL_ALL = '__all__';

const OPCIONES_ESTADO = [
  EstadoPropuestaAplicacion.Propuesta,
  EstadoPropuestaAplicacion.Confirmada,
  EstadoPropuestaAplicacion.Rechazada,
] as const;

/**
 * <c>Bandeja de propuestas de aplicación</c> (CXC-FE-PR6, P1 §6.6).
 * Tabla con filtro por estado; "Ver" monta el master-detail; "Nueva
 * propuesta" abre el Sheet del matching (gate
 * <c>aplicacion-pago.proponer</c>).
 */
export function BandejaAplicaciones() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const nuevaPropuesta = useNuevaPropuesta();
  const puedeProponer = useHasPermission(
    PermisosCanonicos.CuentasPorCobrarAplicacionPagoProponer,
  );

  const query = usePropuestasAplicacion({
    estado: search.estado,
    clienteId: search.clienteId,
    limit: search.limit ?? 200,
  });

  const clienteIds = useMemo(
    () =>
      Array.from(
        new Set((query.data?.items ?? []).map((p) => p.clienteId)),
      ).sort(),
    [query.data],
  );
  const lookup = useClientesLookupCxc(
    { ids: clienteIds },
    { enabled: clienteIds.length > 0 },
  );
  const nombres = useMemo(() => {
    const m = new Map<string, string>();
    for (const c of lookup.data ?? []) m.set(c.id, c.razonSocial);
    return m;
  }, [lookup.data]);

  function actualizarSearch(parcial: Partial<AplicacionesSearch>) {
    navigate({ to: '/cxc/aplicaciones', search: { ...search, ...parcial } });
  }

  const q = (search.q ?? '').trim().toLowerCase();
  const items = (query.data?.items ?? []).filter((p) => {
    if (q.length === 0) return true;
    return (
      p.depositoRef.toLowerCase().includes(q) ||
      p.remittanceRef.toLowerCase().includes(q) ||
      (nombres.get(p.clienteId) ?? '').toLowerCase().includes(q)
    );
  });

  return (
    <div className="space-y-4 px-4 py-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Aplicación de pagos
          </h1>
          <p className="text-sm text-muted-foreground">
            Propuestas de matching depósito ↔ facturas; Ingresos confirma o
            rechaza.
          </p>
        </div>
        {puedeProponer && (
          <Button onClick={() => nuevaPropuesta.abrir()}>
            <Plus className="mr-2 h-4 w-4" />
            Nueva propuesta
          </Button>
        )}
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">Estado</label>
          <Select
            value={search.estado != null ? String(search.estado) : SENTINEL_ALL}
            onValueChange={(v) =>
              actualizarSearch({
                estado:
                  v === SENTINEL_ALL
                    ? undefined
                    : (Number(v) as EstadoPropuestaAplicacion),
              })
            }
          >
            <SelectTrigger aria-label="Filtrar por estado" className="w-44">
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
              {OPCIONES_ESTADO.map((e) => (
                <SelectItem key={e} value={String(e)}>
                  {ETIQUETA_ESTADO_PROPUESTA[e]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las propuestas"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-40' },
            { width: 'w-48' },
            { width: 'w-28' },
            { width: 'w-24' },
            { width: 'w-24' },
            { width: 'w-16' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          icon={<FileText className="h-10 w-10" />}
          title={
            q
              ? `Ninguna propuesta coincide con "${search.q}".`
              : 'Sin propuestas de aplicación con los filtros actuales.'
          }
          description={
            puedeProponer
              ? 'Crea la primera con "Nueva propuesta".'
              : 'Aún no hay propuestas por revisar.'
          }
        />
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-3 py-2 font-medium">Depósito</th>
                <th className="px-3 py-2 font-medium">Cliente</th>
                <th className="px-3 py-2 text-right font-medium">Monto</th>
                <th className="px-3 py-2 text-right font-medium">Ajuste</th>
                <th className="px-3 py-2 font-medium">Facturas</th>
                <th className="px-3 py-2 font-medium">Estado</th>
                <th className="px-3 py-2" />
              </tr>
            </thead>
            <tbody className="divide-y">
              {items.map((p) => (
                <tr key={p.id} className="hover:bg-muted/30">
                  <td className="px-3 py-2">
                    <p className="font-mono text-xs">{p.depositoRef}</p>
                    <p className="max-w-48 truncate text-[11px] text-muted-foreground">
                      {p.remittanceRef}
                    </p>
                  </td>
                  <td className="max-w-56 truncate px-3 py-2">
                    {nombres.get(p.clienteId) ?? p.clienteId.slice(0, 8)}
                  </td>
                  <td className="px-3 py-2 text-right font-mono tabular-nums">
                    {formatoMonto(p.montoDeposito, p.moneda)}
                  </td>
                  <td className="px-3 py-2 text-right font-mono tabular-nums">
                    {p.ajusteNoFiscal !== 0
                      ? formatoMonto(p.ajusteNoFiscal, p.moneda)
                      : '—'}
                  </td>
                  <td className="px-3 py-2 tabular-nums">
                    {p.facturas.length}
                  </td>
                  <td className="px-3 py-2">
                    <ChipEstadoPropuesta estado={p.estado} />
                  </td>
                  <td className="px-3 py-2 text-right">
                    <Button variant="outline" size="sm" asChild>
                      <Link
                        to="/cxc/aplicaciones/$id"
                        params={{ id: p.id }}
                        search={search}
                        state={{ bandejaSearch: search } as never}
                      >
                        Ver
                      </Link>
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
