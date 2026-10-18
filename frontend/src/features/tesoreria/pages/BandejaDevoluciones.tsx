import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Inbox } from 'lucide-react';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  BarraFiltros,
  EmptyState,
  ErrorState,
  PaginacionTabla,
  TableSkeleton,
  TabsEstado,
  useConteosPorEstado,
} from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { offsetLimitAPagina, paginaAOffsetLimit } from '@/components/erp/data/paginacion';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { tesoreriaKeys } from '@/features/tesoreria/api/keys';
import { useDevoluciones } from '@/features/tesoreria/api/useTesoreria';
import {
  ESTADO_DEVOLUCION_LABELS,
  ORIGEN_DEVOLUCION_LABELS,
  type EstadoDevolucionCliente,
  type OrigenDevolucionCliente,
} from '@/features/tesoreria/api/types';
import { EstadoDevolucionBadge } from '@/features/tesoreria/components/Badges';
import type { DevolucionesSearch } from '@/features/tesoreria/lib/devoluciones-search-schema';
import { esApiError } from '@/lib/api';

const FROM = '/_app/tesoreria/devoluciones/' as const;
const TODOS = 'todos';
const TABS_ESTADO = (Object.entries(ESTADO_DEVOLUCION_LABELS) as [string, string][]).map(
  ([valor, label]) => ({ valor: Number(valor), label }),
);

/**
 * <c>Devoluciones a clientes</c> (T9.6, P2): depósitos erróneos y reembolsos
 * de notas de crédito. Solicitud, autorización (otra persona) y pago con
 * contramovimiento ligado.
 */
export function BandejaDevoluciones() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const { offset, limit } = paginaAOffsetLimit(search);

  const query = useDevoluciones({ estado: search.estado, origen: search.origen, offset, limit });
  const conteos = useConteosPorEstado(
    tesoreriaKeys.devoluciones(),
    '/api/v1/tesoreria/devoluciones',
    { origen: search.origen },
  );

  function actualizarSearch(parcial: Partial<DevolucionesSearch>) {
    navigate({ to: '/tesoreria/devoluciones', search: { ...search, page: undefined, ...parcial } });
  }

  const items = query.data?.items ?? [];

  return (
    <div className="space-y-4">
      <PageHeader
        titulo="Devoluciones a clientes"
        descripcion="Depósitos erróneos y reembolsos de notas de crédito: solicitud, autorización y pago con contramovimiento ligado."
      />

      <TabsEstado
        opciones={TABS_ESTADO}
        valor={search.estado}
        onChange={(estado) => actualizarSearch({ estado: estado as EstadoDevolucionCliente | undefined })}
        conteos={conteos.data}
      />

      <BarraFiltros
        aria-label="Filtros de devoluciones"
        filtros={
          <Select
            value={search.origen != null ? String(search.origen) : TODOS}
            onValueChange={(v) =>
              actualizarSearch({ origen: v === TODOS ? undefined : (Number(v) as OrigenDevolucionCliente) })
            }
          >
            <SelectTrigger className="h-ctl-md w-48 text-xs" aria-label="Origen">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={TODOS}>Todos los orígenes</SelectItem>
              {Object.entries(ORIGEN_DEVOLUCION_LABELS).map(([valor, label]) => (
                <SelectItem key={valor} value={valor}>
                  {label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        }
        onLimpiar={search.origen != null ? () => actualizarSearch({ origen: undefined }) : undefined}
        total={
          query.data
            ? `${query.data.total} ${query.data.total === 1 ? 'devolución' : 'devoluciones'}`
            : undefined
        }
      />

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las devoluciones"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[{ width: 'w-56' }, { width: 'w-32' }, { width: 'w-28' }, { width: 'w-40' }, { width: 'w-24' }, { width: 'w-24' }]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          icon={<Inbox className="h-10 w-10" />}
          title="Sin devoluciones"
          description={
            search.estado != null || search.origen != null
              ? 'No hay devoluciones que coincidan con los filtros.'
              : 'Solicita una devolución desde el detalle de un depósito (movimiento de ingreso).'
          }
        />
      ) : (
        <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
          <TableContainer>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Cliente</TableHead>
                  <TableHead>Origen</TableHead>
                  <TableHead className="text-right">Importe</TableHead>
                  <TableHead>Motivo</TableHead>
                  <TableHead>Estado</TableHead>
                  <TableHead>Solicitada</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((d) => (
                  <TableRow key={d.id} className="hover:bg-surface-subtle">
                    <TableCell className="max-w-64">
                      <Link
                        to="/tesoreria/devoluciones/$id"
                        params={{ id: d.id }}
                        className="block truncate text-brand hover:underline"
                        aria-label={`Abrir devolución de ${d.clienteRazonSocial ?? d.clienteId}`}
                      >
                        {d.clienteRazonSocial ?? '—'}
                      </Link>
                      <span className="font-mono text-xs text-ink-muted">{d.clienteClave ?? ''}</span>
                    </TableCell>
                    <TableCell className="text-xs">{ORIGEN_DEVOLUCION_LABELS[d.origen]}</TableCell>
                    <TableCell className="text-right">
                      <MoneyDisplay amount={d.importe} currency={d.moneda} strong />
                    </TableCell>
                    <TableCell className="max-w-48 truncate text-xs" title={d.motivo}>
                      {d.motivo}
                    </TableCell>
                    <TableCell>
                      <EstadoDevolucionBadge estado={d.estado} />
                    </TableCell>
                    <TableCell className="text-xs">
                      <DateTimeDisplay value={d.solicitadaEn} variant="corta" />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
          <PaginacionTabla
            total={query.data?.total ?? 0}
            offset={query.data?.offset ?? 0}
            limit={query.data?.limit ?? limit}
            onChange={(o, l) => actualizarSearch(offsetLimitAPagina(o, l))}
          />
        </div>
      )}
    </div>
  );
}
