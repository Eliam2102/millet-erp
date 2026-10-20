import { MontoMovimiento } from '@/components/erp/display/MontoMovimiento';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { BarraFiltros, TabsEstado, PaginacionTabla } from '@/components/erp';
import { estadoUi, opcionesEstado } from '../lib/estados-ui';
import { PageHeader } from '@/components/erp/PageHeader';
import {
  Table,
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
} from '@/components/ui/table';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { RegistrarIngresoSheet } from '../components/RegistrarIngresoSheet';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useMemo, useState } from 'react';
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
import { useCuentasBancarias, useMovimientos } from '@/features/tesoreria/api/useTesoreria';
import {
  EstadoAplicacionBadge,
  EstadoConciliacionBadge,
} from '@/features/tesoreria/components/Badges';
import { SENTIDO_MOVIMIENTO_LABELS, SentidoMovimiento } from '@/features/tesoreria/api/types';
import { cuentaEnmascarada } from '@/features/tesoreria/lib/formato';
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
  const [registrar, setRegistrar] = useState(false);
  const puedeRegistrar = useHasPermission(PermisosCanonicos.TesoreriaMovimientosRegistrar);
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
    limit: search.limit ?? 10,
    offset: search.offset ?? 0,
  });

  const nombresCuenta = useMemo(() => {
    const m = new Map<string, string>();
    for (const c of cuentas.data ?? []) {
      m.set(c.id, `${c.banco} · ${cuentaEnmascarada(c.numeroCuenta)}`);
    }
    return m;
  }, [cuentas.data]);

  function actualizarSearch(parcial: Partial<MovimientosSearch>) {
    navigate({ to: '/tesoreria/movimientos', search: { ...search, ...parcial } });
  }

  const items = query.data?.items ?? [];

  return (
    <div className="flex h-full min-h-0 flex-col gap-5 text-sm text-ink">
      <RegistrarIngresoSheet open={registrar} onOpenChange={setRegistrar} />
      <PageHeader
        titulo={<>Movimientos bancarios</>}
        descripcion={
          <>
            Libro por cuenta: ingresos manuales, pagos a proveedor, pagos a cuenta y
            contramovimientos.
          </>
        }
        acciones={
          <>
            {puedeRegistrar && (
              <Button onClick={() => setRegistrar(true)}>Registrar ingreso</Button>
            )}
          </>
        }
      />

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={opcionesEstado('aplicacion')}
            valor={search.estadoAplicacion}
            onChange={(estado) =>
              actualizarSearch({
                estadoAplicacion: estado as MovimientosSearch['estadoAplicacion'],
                offset: 0,
              })
            }
            conteos={undefined}
          />
        }
        filtros={
          <BarraFiltros
            filtros={
              <>
                <div className="flex flex-wrap items-end gap-3">
                  <div className="space-y-1">
                    <label className="text-xs text-ink-muted">Cuenta</label>
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
                            {c.banco} <span className="font-mono">{cuentaEnmascarada(c.numeroCuenta)}</span>
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>
                  <div className="space-y-1">
                    <label className="text-xs text-ink-muted">Sentido</label>
                    <Select
                      value={search.sentido != null ? String(search.sentido) : SENTINEL_ALL}
                      onValueChange={(v) =>
                        actualizarSearch({
                          sentido:
                            v === SENTINEL_ALL ? undefined : (Number(v) as SentidoMovimiento),
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
              </>
            }
          />
        }
        pie={
          <PaginacionTabla
            total={query.data?.total ?? 0}
            offset={search.offset ?? 0}
            limit={search.limit ?? 10}
            tamanos={[]}
            onChange={(offset, limit) => actualizarSearch({ offset, limit })}
          />
        }
      >
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
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50 text-left text-xs uppercase tracking-wide text-ink-muted">
                <TableRow>
                  <TableHead className="px-3 py-2 font-medium">Fecha</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Cuenta</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Sentido</TableHead>
                  <TableHead className="px-3 py-2 text-right font-medium">Monto</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Referencia</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Aplicación</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Conciliación</TableHead>
                  <TableHead className="px-3 py-2" />
                </TableRow>
              </TableHeader>
              <TableBody className="divide-y">
                {items.map((m) => (
                  <TableRow key={m.id} className="hover:bg-surface-muted/30">
                    <TableCell className="px-3 py-2 text-xs">
                      <DateTimeDisplay value={m.fechaValor} variant="corta" />
                    </TableCell>
                    <TableCell className="max-w-64 truncate px-3 py-2 text-xs">
                      {nombresCuenta.get(m.cuentaBancariaId) ?? m.cuentaBancariaId.slice(0, 8)}
                    </TableCell>
                    <TableCell className="px-3 py-2">
                      {estadoUi('sentido', m.sentido).texto}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      <MontoMovimiento
                        monto={m.monto}
                        moneda={m.moneda}
                        tipo={m.sentido === SentidoMovimiento.Ingreso ? 'entrada' : 'salida'}
                      />
                    </TableCell>
                    <TableCell className="max-w-40 truncate px-3 py-2 font-mono text-xs">
                      {m.referenciaBancaria ?? '—'}
                    </TableCell>
                    <TableCell className="px-3 py-2">
                      <EstadoAplicacionBadge estado={m.estadoAplicacion} />
                    </TableCell>
                    <TableCell className="px-3 py-2">
                      <EstadoConciliacionBadge estado={m.estadoConciliacion} />
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right">
                      <Button variant="outline" size="sm" asChild>
                        <Link to="/tesoreria/movimientos/$id" params={{ id: m.id }} search={search}>
                          Ver
                        </Link>
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </ListadoModulo>

      {query.data != null && query.data.total > (query.data.items?.length ?? 0) && (
        <p className="text-xs text-ink-muted">
          Mostrando {query.data.items.length} de {query.data.total} movimientos.
        </p>
      )}
    </div>
  );
}
