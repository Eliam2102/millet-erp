import {
  BarraFiltros,
  EmptyState,
  ErrorState,
  PaginacionTabla,
  TableSkeleton,
  TabsEstado,
} from '@/components/erp';
import { BusquedaFolio } from '@/components/erp/data/BusquedaFolio';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { PageHeader } from '@/components/erp/PageHeader';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { EstadoTimbrado } from '@/features/facturacion/api/types';
import { useListarRepp } from '@/features/facturacion/api/useRepp';
import { BadgeSinAsignar } from '@/features/facturacion/components/BadgeSinAsignar';
import { useNuevoRepp } from '@/features/facturacion/components/nuevo-repp-context';
import type { ReppSearch } from '@/features/facturacion/lib/repp-search-schema';
import { ChipTimbrado } from '@/features/facturacion/pages/BandejaFacturas';
import { esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { enmascararCuenta } from '../lib/cuentas-pago-sat';
import { estadoUi } from '../lib/estados-ui';
import { usePaginacionLocal } from '../lib/use-paginacion-local';

const FROM = '/_app/facturacion/repp/' as const;

/**
 * <c>Bandeja de complementos de pago (REPP)</c> (FE-F6). Lista los REPP
 * con su estado de timbrado; "Ver" abre el detalle (facturas cubiertas).
 * "Nuevo REPP" abre el Sheet de emisión.
 */
export function BandejaRepp() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const nuevoRepp = useNuevoRepp();
  const puedeEmitir = useHasPermission(PermisosCanonicos.FacturacionReppEmitir);

  const query = useListarRepp(search.estado, search.alcance);

  function actualizarSearch(parcial: Partial<ReppSearch>) {
    navigate({ to: '/facturacion/repp', search: { ...search, ...parcial } });
  }

  const q = (search.q ?? '').trim().toLowerCase();
  const items = (query.data?.items ?? []).filter((r) => {
    if (q.length === 0) return true;
    return (
      r.folio.toLowerCase().includes(q) ||
      (r.uuid ?? '').toLowerCase().includes(q) ||
      r.receptorNombre.toLowerCase().includes(q)
    );
  });

  const paginacion = usePaginacionLocal(items, JSON.stringify(search));
  return (
    <div className="space-y-5">
      <PageHeader
        titulo={<>Complementos de pago (REPP)</>}
        descripcion={<>Recibos electrónicos de pago (Pago 2.0) emitidos.</>}
        acciones={
          <>
            {puedeEmitir && (
              <Button onClick={() => nuevoRepp.abrir()}>
                <Plus className="mr-2 h-4 w-4" />
                Nuevo REPP
              </Button>
            )}
          </>
        }
      />

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={Object.entries(EstadoTimbrado)
              .filter(([, valor]) =>
                [
                  EstadoTimbrado.Borrador,
                  EstadoTimbrado.TimbradoEnProceso,
                  EstadoTimbrado.Timbrado,
                  EstadoTimbrado.TimbradoFallido,
                  EstadoTimbrado.Cancelado,
                ].some((estado) => estado === valor),
              )
              .map(([nombre, valor]) => ({
                valor,
                label: estadoUi('rep', nombre).texto,
              }))}
            valor={search.estado}
            onChange={(estado) => actualizarSearch({ estado: estado as ReppSearch['estado'] })}
            conteos={undefined}
          />
        }
        filtros={
          <BarraFiltros
            busqueda={
              <BusquedaFolio
                aria-label="Folio, UUID o receptor"
                placeholder="Folio, UUID o receptor"
                valor={search.q}
                onCommit={(q) => actualizarSearch({ q })}
              />
            }
            filtros={
              <>
                {search.estado != null && (
                  <Button variant="ghost" onClick={() => actualizarSearch({ estado: undefined })}>
                    Limpiar
                  </Button>
                )}
                <BadgeSinAsignar
                  count={query.data?.sinAsignarCount ?? null}
                  activo={search.alcance === 'sin-asignar'}
                  onToggle={() =>
                    actualizarSearch({
                      alcance: search.alcance === 'sin-asignar' ? undefined : 'sin-asignar',
                    })
                  }
                />
              </>
            }
          />
        }
        pie={
          <PaginacionTabla
            total={items.length}
            offset={paginacion.offset}
            limit={10}
            tamanos={[]}
            onChange={paginacion.onChange}
          />
        }
      >
        {query.isError ? (
          <ErrorState
            title="No se pudieron cargar los complementos de pago"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton
            rows={6}
            columns={[
              { width: 'w-28' },
              { width: 'w-48' },
              { width: 'w-32' },
              { width: 'w-28' },
              { width: 'w-16' },
            ]}
          />
        ) : items.length === 0 ? (
          <EmptyState
            title="Sin complementos de pago"
            description={
              search.estado != null || q.length > 0
                ? 'No hay REPP que coincidan con los filtros.'
                : 'Aún no hay complementos de pago. Emite uno con "Nuevo REPP".'
            }
          />
        ) : (
          <div className="overflow-x-auto rounded-md border border-line">
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50">
                <TableRow>
                  <TableHead className="px-3 py-2 text-left">Folio</TableHead>
                  <TableHead className="px-3 py-2 text-left">Receptor</TableHead>
                  <TableHead className="px-3 py-2 text-right tabular-nums">Importe</TableHead>
                  <TableHead className="px-3 py-2 text-left">Estado</TableHead>
                  <TableHead className="px-3 py-2 text-left">Fecha pago</TableHead>
                  <TableHead className="px-3 py-2 text-left">Cuentas del pago</TableHead>
                  <TableHead className="px-3 py-2 text-right tabular-nums">Acción</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {paginacion.visibles.map((r) => (
                  <TableRow key={r.id} className="border-t hover:bg-surface-muted/30 border-line">
                    <TableCell className="px-3 py-2 font-mono text-xs">{r.folio}</TableCell>
                    <TableCell className="px-3 py-2">{r.receptorNombre}</TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      {r.importeTotalPago.toFixed(2)}
                    </TableCell>
                    <TableCell className="px-3 py-2">
                      <ChipTimbrado estado={r.estado} />
                    </TableCell>
                    <TableCell className="px-3 py-2 text-xs text-ink-muted">
                      {new Date(r.fechaPago).toLocaleDateString('es-MX')}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-xs">
                      {r.cuentaOrdenante || r.cuentaBeneficiaria ? (
                        <span className="flex flex-col font-mono">
                          {r.cuentaOrdenante && (
                            <span>Ordenante {enmascararCuenta(r.cuentaOrdenante)}</span>
                          )}
                          {r.cuentaBeneficiaria && (
                            <span>Beneficiaria {enmascararCuenta(r.cuentaBeneficiaria)}</span>
                          )}
                        </span>
                      ) : (
                        <span className="text-ink-muted">Sin cuenta</span>
                      )}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right tabular-nums">
                      <Link
                        to="/facturacion/repp/$id"
                        params={{ id: r.id }}
                        className="text-sm font-medium text-brand hover:underline"
                      >
                        Ver
                      </Link>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </ListadoModulo>
    </div>
  );
}
