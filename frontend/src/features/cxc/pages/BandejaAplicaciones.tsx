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

import { usePaginacionLocal } from '../lib/use-paginacion-local';

import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { useMemo } from 'react';
import { DepositosSinIdentificar } from '@/features/cxc/components/DepositosSinIdentificar';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { FileText, Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';

import { useClientesLookupCxc } from '@/features/cxc/api/useLineasCredito';
import {
  useDepositosPorIdentificar,
  usePropuestasAplicacion,
} from '@/features/cxc/api/useAplicaciones';
import { ChipEstadoPropuesta } from '@/features/cxc/components/ChipEstadoPropuesta';
import { useNuevaPropuesta } from '@/features/cxc/components/nueva-propuesta-context';
import { EstadoPropuestaAplicacion } from '@/features/cxc/api/types';
import { ETIQUETA_ESTADO_PROPUESTA, formatoMonto } from '@/features/cxc/lib/glosario';
import type { AplicacionesSearch } from '@/features/cxc/lib/aplicaciones-search-schema';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';

const FROM = '/_app/cxc/aplicaciones/' as const;

const OPCIONES_ESTADO = [
  EstadoPropuestaAplicacion.Propuesta,
  EstadoPropuestaAplicacion.Confirmada,
  EstadoPropuestaAplicacion.Rechazada,
] as const;

/**
 * C7.4 · CA7.9: valor centinela de la pestaña «Sin identificar» dentro de
 * TabsEstado (numérico); no es un estado de propuesta. Se refleja en el
 * search param <c>vista=sin-identificar</c> (sin ruta nueva).
 */
const TAB_SIN_IDENTIFICAR = 0;

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
  const puedeProponer = useHasPermission(PermisosCanonicos.CuentasPorCobrarAplicacionPagoProponer);

  const sinIdentificar = search.vista === 'sin-identificar';
  const depositosSinIdentificar = useDepositosPorIdentificar({ enabled: sinIdentificar && puedeProponer });
  const query = usePropuestasAplicacion({
    estado: search.estado,
    clienteId: search.clienteId,
    limit: search.limit ?? 200,
  });

  const clienteIds = useMemo(
    () => Array.from(new Set((query.data?.items ?? []).map((p) => p.clienteId))).sort(),
    [query.data],
  );
  const lookup = useClientesLookupCxc({ ids: clienteIds }, { enabled: clienteIds.length > 0 });
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

  const pagina = usePaginacionLocal(items, JSON.stringify(search));

  return (
    <div className="flex min-h-0 flex-col gap-5">
      <PageHeader
        titulo={<>Aplicación de pagos</>}
        descripcion={<>Propuestas de matching depósito ↔ facturas; Ingresos confirma o rechaza.</>}
        acciones={
          <>
            {puedeProponer && (
              <Button onClick={() => nuevaPropuesta.abrir()}>
                <Plus className="mr-2 h-4 w-4" />
                Nueva propuesta
              </Button>
            )}
          </>
        }
      />

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={[
              ...OPCIONES_ESTADO.map((e) => ({
                valor: e as number,
                label: ETIQUETA_ESTADO_PROPUESTA[e],
              })),
              ...(puedeProponer ? [{ valor: TAB_SIN_IDENTIFICAR, label: 'Sin identificar' }] : []),
            ]}
            valor={sinIdentificar ? TAB_SIN_IDENTIFICAR : search.estado}
            conteos={undefined}
            onChange={(estado) =>
              estado === TAB_SIN_IDENTIFICAR
                ? actualizarSearch({ vista: 'sin-identificar', estado: undefined })
                : actualizarSearch({
                    vista: undefined,
                    estado: estado as EstadoPropuestaAplicacion | undefined,
                  })
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
          />
        }
        pie={
          sinIdentificar ? undefined : (
          <PaginacionTabla
            total={items.length}
            offset={pagina.offset}
            limit={10}
            tamanos={[]}
            onChange={pagina.cambiar}
          />
          )
        }
      >
        {sinIdentificar ? (
          <DepositosSinIdentificar
            query={depositosSinIdentificar}
            q={q}
            onIdentificar={(deposito) => nuevaPropuesta.abrir(deposito)}
          />
        ) : query.isError ? (
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
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50 text-left text-xs uppercase tracking-wide text-ink-muted">
                <TableRow>
                  <TableHead className="px-3 py-2 font-medium">Depósito</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Cliente</TableHead>
                  <TableHead className="px-3 py-2 text-right font-medium">Monto</TableHead>
                  <TableHead className="px-3 py-2 text-right font-medium">Ajuste</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Facturas</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Estado</TableHead>
                  <TableHead className="px-3 py-2" />
                </TableRow>
              </TableHeader>
              <TableBody className="divide-y">
                {pagina.visibles.map((p) => (
                  <TableRow key={p.id} className="h-12 hover:bg-surface-subtle">
                    <TableCell className="px-3 py-2">
                      <p className="font-mono text-xs">{p.depositoRef}</p>
                      <p className="max-w-48 truncate text-2xs text-ink-muted">{p.remittanceRef}</p>
                    </TableCell>
                    <TableCell className="max-w-56 truncate px-3 py-2">
                      {nombres.get(p.clienteId) ?? '—'}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      {formatoMonto(p.montoDeposito, p.moneda)}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      {p.ajusteNoFiscal !== 0 ? formatoMonto(p.ajusteNoFiscal, p.moneda) : '—'}
                    </TableCell>
                    <TableCell className="px-3 py-2 tabular-nums">{p.facturas.length}</TableCell>
                    <TableCell className="px-3 py-2">
                      <ChipEstadoPropuesta estado={p.estado} />
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right">
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
