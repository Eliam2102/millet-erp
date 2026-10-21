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
import { useListarCartaPorte } from '@/features/facturacion/api/useCartaPorte';
import { useNuevaCartaPorte } from '@/features/facturacion/components/nueva-carta-porte-context';
import type { CartaPorteSearch } from '@/features/facturacion/lib/carta-porte-search-schema';
import { ChipTimbrado } from '@/features/facturacion/pages/BandejaFacturas';
import { esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { estadoUi } from '../lib/estados-ui';
import { usePaginacionLocal } from '../lib/use-paginacion-local';

const FROM = '/_app/facturacion/carta-porte/' as const;

/**
 * <c>Bandeja de Carta Porte</c> (FE-F8). Lista los CFDIs de Traslado/Ingreso
 * con su tramo y estado; "Ver" abre el detalle. "Nueva Carta Porte" abre el
 * Sheet de captura.
 */
export function BandejaCartaPorte() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const nueva = useNuevaCartaPorte();
  const puedeEmitir = useHasPermission(PermisosCanonicos.FacturacionCartaPorteEmitir);

  const query = useListarCartaPorte(search.estado);

  function actualizarSearch(parcial: Partial<CartaPorteSearch>) {
    navigate({ to: '/facturacion/carta-porte', search: { ...search, ...parcial } });
  }

  const q = (search.q ?? '').trim().toLowerCase();
  const items = (query.data ?? []).filter((c) => {
    if (q.length === 0) return true;
    return (
      c.folio.toLowerCase().includes(q) ||
      (c.uuid ?? '').toLowerCase().includes(q) ||
      c.tramo.toLowerCase().includes(q)
    );
  });

  const paginacion = usePaginacionLocal(items, JSON.stringify(search));
  return (
    <div className="space-y-5">
      <PageHeader
        titulo={<>Carta Porte</>}
        descripcion={<>CFDIs de Traslado (T) e Ingreso (I) con complemento Carta Porte 3.1.</>}
        acciones={
          <>
            {puedeEmitir && (
              <Button onClick={() => nueva.abrir()}>
                <Plus className="mr-2 h-4 w-4" />
                Nueva Carta Porte
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
                label: estadoUi('cartaPorte', nombre).texto,
              }))}
            valor={search.estado}
            onChange={(estado) =>
              actualizarSearch({ estado: estado as CartaPorteSearch['estado'] })
            }
            conteos={undefined}
          />
        }
        filtros={
          <BarraFiltros
            busqueda={
              <BusquedaFolio
                aria-label="Folio, UUID o tramo"
                placeholder="Folio, UUID o tramo"
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
            title="No se pudieron cargar las Cartas Porte"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton
            rows={6}
            columns={[
              { width: 'w-28' },
              { width: 'w-16' },
              { width: 'w-56' },
              { width: 'w-28' },
              { width: 'w-16' },
            ]}
          />
        ) : items.length === 0 ? (
          <EmptyState
            title="Sin Cartas Porte"
            description={
              search.estado != null || q.length > 0
                ? 'No hay Cartas Porte que coincidan con los filtros.'
                : 'Aún no hay Cartas Porte. Emite una con "Nueva Carta Porte".'
            }
          />
        ) : (
          <div className="overflow-x-auto rounded-md border border-line">
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50">
                <TableRow>
                  <TableHead className="px-3 py-2 text-left">Folio</TableHead>
                  <TableHead className="px-3 py-2 text-left">Tipo</TableHead>
                  <TableHead className="px-3 py-2 text-left">Tramo</TableHead>
                  <TableHead className="px-3 py-2 text-left">Estado</TableHead>
                  <TableHead className="px-3 py-2 text-left">Salida</TableHead>
                  <TableHead className="px-3 py-2 text-right tabular-nums">Acción</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {paginacion.visibles.map((c) => (
                  <TableRow key={c.id} className="border-t hover:bg-surface-muted/30 border-line">
                    <TableCell className="px-3 py-2 font-mono text-xs">{c.folio}</TableCell>
                    <TableCell className="px-3 py-2">{c.tipo}</TableCell>
                    <TableCell className="px-3 py-2">{c.tramo}</TableCell>
                    <TableCell className="px-3 py-2">
                      <ChipTimbrado estado={c.estado} />
                    </TableCell>
                    <TableCell className="px-3 py-2 text-xs text-ink-muted">
                      {new Date(c.fechaSalida).toLocaleDateString('es-MX')}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right tabular-nums">
                      <Link
                        to="/facturacion/carta-porte/$id"
                        params={{ id: c.id }}
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
