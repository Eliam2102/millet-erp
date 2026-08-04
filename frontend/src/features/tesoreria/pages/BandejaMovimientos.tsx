import { useMemo } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { ArrowRightLeft } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import {
  useCuentasBancarias,
  useMovimientos,
} from '@/features/tesoreria/api/useTesoreria';
import {
  EstadoAplicacionBadge,
  EstadoConciliacionBadge,
  SentidoBadge,
} from '@/features/tesoreria/components/Badges';
import {
  SENTIDO_MOVIMIENTO_LABELS,
  SentidoMovimiento,
} from '@/features/tesoreria/api/types';
import { formatoFecha, formatoMonto } from '@/features/tesoreria/lib/formato';
import type { MovimientosSearch } from '@/features/tesoreria/lib/movimientos-search-schema';
import { esApiError } from '@/lib/api';

const FROM = '/_app/tesoreria/movimientos/' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>Libro de movimientos bancarios</c> (TES-FE-PR2, P1 §6.6): tabla con
 * filtros server-side (cuenta / sentido / fechas). "Ver" navega al
 * detalle con aplicaciones y contramovimientos.
 */
export function BandejaMovimientos() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();

  const cuentas = useCuentasBancarias(true);
  const query = useMovimientos({
    cuentaBancariaId: search.cuentaBancariaId,
    sentido: search.sentido,
    estadoAplicacion: search.estadoAplicacion,
    estadoConciliacion: search.estadoConciliacion,
    desde: search.desde,
    hasta: search.hasta,
    limit: search.limit ?? 200,
    offset: search.offset ?? 0,
  });

  const nombresCuenta = useMemo(() => {
    const m = new Map<string, string>();
    for (const c of cuentas.data ?? []) {
      m.set(c.id, `${c.banco} ${c.numeroCuenta}`);
    }
    return m;
  }, [cuentas.data]);

  function actualizarSearch(parcial: Partial<MovimientosSearch>) {
    navigate({ to: '/tesoreria/movimientos', search: { ...search, ...parcial } });
  }

  const items = query.data?.items ?? [];

  return (
    <div className="space-y-4 px-4 py-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">
          Movimientos bancarios
        </h1>
        <p className="text-sm text-muted-foreground">
          Libro por cuenta: ingresos manuales, pagos a proveedor, pagos a
          cuenta y contramovimientos.
        </p>
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">Cuenta</label>
          <Select
            value={search.cuentaBancariaId ?? SENTINEL_ALL}
            onValueChange={(v) =>
              actualizarSearch({
                cuentaBancariaId: v === SENTINEL_ALL ? undefined : v,
              })
            }
          >
            <SelectTrigger aria-label="Filtrar por cuenta" className="w-64">
              <SelectValue placeholder="Todas" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Todas</SelectItem>
              {(cuentas.data ?? []).map((c) => (
                <SelectItem key={c.id} value={c.id}>
                  {c.banco} <span className="font-mono">{c.numeroCuenta}</span>
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">Sentido</label>
          <Select
            value={search.sentido != null ? String(search.sentido) : SENTINEL_ALL}
            onValueChange={(v) =>
              actualizarSearch({
                sentido:
                  v === SENTINEL_ALL
                    ? undefined
                    : (Number(v) as SentidoMovimiento),
              })
            }
          >
            <SelectTrigger aria-label="Filtrar por sentido" className="w-36">
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
              {Object.values(SentidoMovimiento).map((s) => (
                <SelectItem key={s} value={String(s)}>
                  {SENTIDO_MOVIMIENTO_LABELS[s]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        {(search.cuentaBancariaId || search.sentido != null) && (
          <Button
            variant="ghost"
            onClick={() =>
              actualizarSearch({
                cuentaBancariaId: undefined,
                sentido: undefined,
              })
            }
          >
            Limpiar
          </Button>
        )}
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudo cargar el libro de movimientos"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={8}
          columns={[
            { width: 'w-24' },
            { width: 'w-56' },
            { width: 'w-20' },
            { width: 'w-28' },
            { width: 'w-28' },
            { width: 'w-16' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          icon={<ArrowRightLeft className="h-10 w-10" />}
          title="Sin movimientos con los filtros actuales."
          description="El libro se llena con los ingresos manuales, los pagos a proveedor y los pagos a cuenta del módulo."
        />
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-3 py-2 font-medium">Fecha</th>
                <th className="px-3 py-2 font-medium">Cuenta</th>
                <th className="px-3 py-2 font-medium">Sentido</th>
                <th className="px-3 py-2 text-right font-medium">Monto</th>
                <th className="px-3 py-2 font-medium">Referencia</th>
                <th className="px-3 py-2 font-medium">Aplicación</th>
                <th className="px-3 py-2 font-medium">Conciliación</th>
                <th className="px-3 py-2" />
              </tr>
            </thead>
            <tbody className="divide-y">
              {items.map((m) => (
                <tr key={m.id} className="hover:bg-muted/30">
                  <td className="px-3 py-2 text-xs">{formatoFecha(m.fechaValor)}</td>
                  <td className="max-w-64 truncate px-3 py-2 text-xs">
                    {nombresCuenta.get(m.cuentaBancariaId) ??
                      m.cuentaBancariaId.slice(0, 8)}
                  </td>
                  <td className="px-3 py-2">
                    <SentidoBadge sentido={m.sentido} />
                  </td>
                  <td className="px-3 py-2 text-right font-mono tabular-nums">
                    {formatoMonto(m.monto, m.moneda)}
                  </td>
                  <td className="max-w-40 truncate px-3 py-2 font-mono text-xs">
                    {m.referenciaBancaria ?? '—'}
                  </td>
                  <td className="px-3 py-2">
                    <EstadoAplicacionBadge estado={m.estadoAplicacion} />
                  </td>
                  <td className="px-3 py-2">
                    <EstadoConciliacionBadge estado={m.estadoConciliacion} />
                  </td>
                  <td className="px-3 py-2 text-right">
                    <Button variant="outline" size="sm" asChild>
                      <Link
                        to="/tesoreria/movimientos/$id"
                        params={{ id: m.id }}
                        search={search}
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

      {query.data != null &&
        query.data.total > (query.data.items?.length ?? 0) && (
          <p className="text-xs text-muted-foreground">
            Mostrando {query.data.items.length} de {query.data.total} movimientos.
          </p>
        )}
    </div>
  );
}
