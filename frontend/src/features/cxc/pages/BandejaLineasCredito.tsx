import {
  ListadoModulo,
  TabsEstado,
  BarraFiltros,
  PaginacionTabla,
  PageHeader,
  EmptyState,
  ErrorState,
  TableSkeleton,
} from '@/components/erp';
import { Input } from '@/components/ui/input';

import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { useMemo } from 'react';
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

import { CreditCard } from 'lucide-react';
import { useClientesLookupCxc, useLineasCredito } from '@/features/cxc/api/useLineasCredito';
import { ChipEstadoLinea } from '@/features/cxc/components/ChipEstadoLinea';
import { useNuevaLineaCredito } from '@/features/cxc/components/nueva-linea-credito-context';
import { EstadoLineaCredito, MONEDAS_LINEA_CREDITO } from '@/features/cxc/api/types';
import {
  ETIQUETA_ESTADO_LINEA,
  ETIQUETA_ORIGEN_LINEA,
  formatoMonto,
} from '@/features/cxc/lib/glosario';
import type { LineasSearch } from '@/features/cxc/lib/lineas-search-schema';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';

const FROM = '/_app/cxc/lineas-credito/' as const;
const SENTINEL_ALL = '__all__';

const OPCIONES_ESTADO = [
  EstadoLineaCredito.Activa,
  EstadoLineaCredito.Bloqueada,
  EstadoLineaCredito.Suspendida,
] as const;

/**
 * <c>Bandeja de líneas de crédito</c> (CXC-FE-PR2, P1 §6.6). Tabla con
 * filtros server-side (estado / moneda) + búsqueda client-side por
 * razón social / RFC / clave del cliente (lookup CxC por ids). "Ver"
 * navega al master-detail; "Nueva línea" abre el Sheet (P4).
 */
export function BandejaLineasCredito() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const nuevaLinea = useNuevaLineaCredito();
  const puedeGestionar = useHasPermission(PermisosCanonicos.CuentasPorCobrarLineasCreditoGestionar);

  const query = useLineasCredito({
    estado: search.estado,
    moneda: search.moneda,
    clienteId: search.clienteId,
    limit: search.limit ?? 10,
    offset: search.offset ?? 0,
  });

  // Resolución de nombres de la página visible (lookup por ids).
  const clienteIds = useMemo(
    () => Array.from(new Set((query.data?.items ?? []).map((l) => l.clienteId))).sort(),
    [query.data],
  );
  const lookup = useClientesLookupCxc({ ids: clienteIds }, { enabled: clienteIds.length > 0 });
  const nombres = useMemo(() => {
    const m = new Map<string, { razonSocial: string; rfc: string | null }>();
    for (const c of lookup.data ?? []) {
      m.set(c.id, { razonSocial: c.razonSocial, rfc: c.rfc });
    }
    return m;
  }, [lookup.data]);

  function actualizarSearch(parcial: Partial<LineasSearch>) {
    navigate({ to: '/cxc/lineas-credito', search: { ...search, offset: 0, ...parcial } });
  }

  const q = (search.q ?? '').trim().toLowerCase();
  const items = (query.data?.items ?? []).filter((l) => {
    if (q.length === 0) return true;
    const cliente = nombres.get(l.clienteId);
    return (
      (cliente?.razonSocial ?? '').toLowerCase().includes(q) ||
      (cliente?.rfc ?? '').toLowerCase().includes(q) ||
      l.clienteId.toLowerCase().includes(q)
    );
  });

  return (
    <div className="flex min-h-0 flex-col gap-5">
      <PageHeader
        titulo={<>Líneas de crédito</>}
        descripcion={<>Crédito por cliente y moneda: límite, plazo, origen y estado.</>}
        acciones={
          <>
            {puedeGestionar && (
              <Button onClick={() => nuevaLinea.abrir()}>
                <Plus className="mr-2 h-4 w-4" />
                Nueva línea
              </Button>
            )}
          </>
        }
      />

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={OPCIONES_ESTADO.map((e) => ({ valor: e, label: ETIQUETA_ESTADO_LINEA[e] }))}
            valor={search.estado}
            conteos={undefined}
            onChange={(estado) =>
              actualizarSearch({ estado: estado as EstadoLineaCredito | undefined, offset: 0 })
            }
          />
        }
        filtros={
          <BarraFiltros
            busqueda={
              <Input
                aria-label="Buscar en los registros recibidos"
                placeholder="Buscar en los registros recibidos"
                value={search.q ?? ''}
                onChange={(e) => actualizarSearch({ q: e.target.value || undefined })}
              />
            }
            filtros={
              <div className="flex flex-wrap items-end gap-3">
                <div className="space-y-1">
                  <label className="text-xs text-ink-muted">Moneda</label>
                  <Select
                    value={search.moneda ?? SENTINEL_ALL}
                    onValueChange={(v) =>
                      actualizarSearch({
                        moneda: v === SENTINEL_ALL ? undefined : (v as LineasSearch['moneda']),
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
                {(search.estado != null || search.moneda != null) && (
                  <Button
                    variant="ghost"
                    onClick={() => actualizarSearch({ estado: undefined, moneda: undefined })}
                  >
                    Limpiar
                  </Button>
                )}
              </div>
            }
          />
        }
        pie={
          <PaginacionTabla
            total={query.data?.total ?? 0}
            offset={search.offset ?? 0}
            limit={search.limit ?? 10}
            tamanos={[]}
            onChange={(offset) => actualizarSearch({ offset })}
          />
        }
      >
        {query.isError ? (
          <ErrorState
            title="No se pudieron cargar las líneas de crédito"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton
            rows={6}
            columns={[
              { width: 'w-56' },
              { width: 'w-16' },
              { width: 'w-32' },
              { width: 'w-20' },
              { width: 'w-24' },
              { width: 'w-24' },
              { width: 'w-16' },
            ]}
          />
        ) : items.length === 0 ? (
          <EmptyState
            icon={<CreditCard className="h-10 w-10" />}
            title={
              q
                ? `Ninguna línea coincide con "${search.q}".`
                : 'Sin líneas de crédito con los filtros actuales.'
            }
            description={
              puedeGestionar
                ? 'Crea la primera línea con "Nueva línea".'
                : 'Ajusta los filtros para ver resultados.'
            }
          />
        ) : (
          <div className="overflow-x-auto rounded-md border">
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50 text-left text-xs uppercase tracking-wide text-ink-muted">
                <TableRow>
                  <TableHead className="px-3 py-2 font-medium">Cliente</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Moneda</TableHead>
                  <TableHead className="px-3 py-2 text-right font-medium">Límite</TableHead>
                  <TableHead className="px-3 py-2 text-right font-medium">Plazo</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Clasif.</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Origen</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Estado</TableHead>
                  <TableHead className="px-3 py-2" />
                </TableRow>
              </TableHeader>
              <TableBody className="divide-y">
                {items.map((l) => {
                  const cliente = nombres.get(l.clienteId);
                  return (
                    <TableRow key={l.id} className="h-12 hover:bg-surface-subtle">
                      <TableCell className="max-w-72 px-3 py-2">
                        <p className="truncate">
                          {cliente?.razonSocial ??
                            (lookup.isLoading ? '…' : l.clienteId.slice(0, 8))}
                        </p>
                        {cliente?.rfc && (
                          <p className="font-mono text-xs text-ink-muted">{cliente.rfc}</p>
                        )}
                      </TableCell>
                      <TableCell className="px-3 py-2 font-mono">{l.moneda}</TableCell>
                      <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                        {formatoMonto(l.limite, l.moneda)}
                      </TableCell>
                      <TableCell className="px-3 py-2 text-right tabular-nums">
                        {l.plazoDias} d
                      </TableCell>
                      <TableCell className="px-3 py-2">{l.clasificacion ?? '—'}</TableCell>
                      <TableCell className="px-3 py-2">{ETIQUETA_ORIGEN_LINEA[l.origen]}</TableCell>
                      <TableCell className="px-3 py-2">
                        <ChipEstadoLinea estado={l.estado} />
                      </TableCell>
                      <TableCell className="px-3 py-2 text-right">
                        <Button variant="outline" size="sm" asChild>
                          <Link
                            to="/cxc/lineas-credito/$id"
                            params={{ id: l.id }}
                            search={search}
                            state={{ bandejaSearch: search } as never}
                          >
                            Ver
                          </Link>
                        </Button>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </div>
        )}
      </ListadoModulo>

      {query.data != null && query.data.total > (query.data.items?.length ?? 0) && (
        <p className="text-xs text-ink-muted">
          Mostrando {query.data.items.length} de {query.data.total} líneas.
        </p>
      )}
    </div>
  );
}
