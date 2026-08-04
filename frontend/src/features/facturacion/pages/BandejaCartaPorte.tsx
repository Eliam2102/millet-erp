import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { useListarCartaPorte } from '@/features/facturacion/api/useCartaPorte';
import { useNuevaCartaPorte } from '@/features/facturacion/components/nueva-carta-porte-context';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { EstadoTimbrado } from '@/features/facturacion/api/types';
import { ChipTimbrado } from '@/features/facturacion/pages/BandejaFacturas';
import type { CartaPorteSearch } from '@/features/facturacion/lib/carta-porte-search-schema';

const FROM = '/_app/facturacion/carta-porte/' as const;
const SENTINEL_ALL = '__all__';

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

  return (
    <div className="space-y-4 px-4 py-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Carta Porte</h1>
          <p className="text-sm text-muted-foreground">
            CFDIs de Traslado (T) e Ingreso (I) con complemento Carta Porte 3.1.
          </p>
        </div>
        {puedeEmitir && (
          <Button onClick={() => nueva.abrir()}>
            <Plus className="mr-2 h-4 w-4" />
            Nueva Carta Porte
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
                    : (Number(v) as CartaPorteSearch['estado']),
              })
            }
          >
            <SelectTrigger aria-label="Filtrar por estado" className="w-48">
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
              <SelectItem value={String(EstadoTimbrado.Timbrado)}>Timbrado</SelectItem>
              <SelectItem value={String(EstadoTimbrado.TimbradoEnProceso)}>
                En proceso
              </SelectItem>
              <SelectItem value={String(EstadoTimbrado.TimbradoFallido)}>
                Fallido
              </SelectItem>
              <SelectItem value={String(EstadoTimbrado.Cancelado)}>
                Cancelado
              </SelectItem>
            </SelectContent>
          </Select>
        </div>
        {search.estado != null && (
          <Button variant="ghost" onClick={() => actualizarSearch({ estado: undefined })}>
            Limpiar
          </Button>
        )}
      </div>

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
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">Folio</th>
                <th className="px-3 py-2 text-left">Tipo</th>
                <th className="px-3 py-2 text-left">Tramo</th>
                <th className="px-3 py-2 text-left">Estado</th>
                <th className="px-3 py-2 text-left">Salida</th>
                <th className="px-3 py-2 text-right">Acción</th>
              </tr>
            </thead>
            <tbody>
              {items.map((c) => (
                <tr key={c.id} className="border-t hover:bg-muted/30">
                  <td className="px-3 py-2 font-mono text-xs">{c.folio}</td>
                  <td className="px-3 py-2">{c.tipo}</td>
                  <td className="px-3 py-2">{c.tramo}</td>
                  <td className="px-3 py-2">
                    <ChipTimbrado estado={c.estado} />
                  </td>
                  <td className="px-3 py-2 text-xs text-muted-foreground">
                    {new Date(c.fechaSalida).toLocaleDateString('es-MX')}
                  </td>
                  <td className="px-3 py-2 text-right">
                    <Link
                      to="/facturacion/carta-porte/$id"
                      params={{ id: c.id }}
                      className="text-sm font-medium text-primary hover:underline"
                    >
                      Ver
                    </Link>
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
