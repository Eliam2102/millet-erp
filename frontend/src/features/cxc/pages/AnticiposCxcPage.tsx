import {
  ListadoModulo,
  TabsEstado,
  BarraFiltros,
  PaginacionTabla,
  KpiCard,
  PageHeader,
  EmptyState,
  ErrorState,
  TableSkeleton,
} from '@/components/erp';

import { usePaginacionLocal } from '../lib/use-paginacion-local';

import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip';
import { Fragment, useMemo, useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { ChevronRight, HandCoins } from 'lucide-react';
import { Label } from '@/components/ui/label';

import { useAplicacionesAnticipo } from '@/features/cxc/api/useCartera';
import type {
  AnticipoConAplicacionesItem,
  AplicacionAnticipoItem,
} from '@/features/cxc/api/types';
import { ClienteSelectorCxc } from '@/features/cxc/components/ClienteSelectorCxc';
import { formatoMonto } from '@/features/cxc/lib/glosario';
import type { AnticiposCxcSearch } from '@/features/cxc/lib/cartera-search-schema';
import { esApiError } from '@/lib/api';
import { formatDate } from '@/lib/datetime';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { cn } from '@/lib/utils';
import { EstadoCxcBadge } from '../components/EstadoCxcBadge';

const FROM = '/_app/cxc/anticipos/' as const;

/** Tabs por estado derivado (mismo cálculo que la comparativa F6.2). */
const ESTADOS = ['Pendiente', 'Parcial', 'Aplicado', 'Cancelado'] as const;

/**
 * <c>Anticipos de clientes</c> (CXC-FE-PR5 + F1-CXC-05) — saldo de cada
 * anticipo y su aplicación a la factura final (factura final + CFDI de egreso
 * tipo 07), leídos de Facturación vía read port. Solo consulta: la emisión y la
 * amortización viven en Facturación. El cliente es opcional: sin él se listan
 * todos los clientes con anticipos (filtrados por las sucursales del usuario).
 * Totales POR MONEDA — nunca se suman divisas distintas (§5).
 */
export function AnticiposCxcPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const clienteId = search.clienteId ?? null;
  const puedeVerComparativa = useHasPermission(PermisosCanonicos.FacturacionReportesLeer);

  const query = useAplicacionesAnticipo({ clienteId: clienteId ?? undefined });

  function actualizar(parcial: Partial<AnticiposCxcSearch>) {
    navigate({ to: '/cxc/anticipos', search: { ...search, ...parcial } });
  }

  const [estado, setEstado] = useState<number | undefined>();
  const [expandidos, setExpandidos] = useState<ReadonlySet<string>>(new Set());

  const anticipos = useMemo(() => query.data?.anticipos ?? [], [query.data]);
  const conteos = useMemo(() => {
    const c: Record<number, number> = {};
    ESTADOS.forEach((e, i) => {
      c[i] = anticipos.filter((a) => a.estado === e).length;
    });
    return c;
  }, [anticipos]);
  const items = anticipos.filter((a) => estado === undefined || a.estado === ESTADOS[estado]);
  const pagina = usePaginacionLocal(items, JSON.stringify([clienteId, estado]));

  const mostrarCliente = clienteId == null;
  const columnas = mostrarCliente ? 13 : 12;

  function alternar(id: string) {
    setExpandidos((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  return (
    <div className="flex min-h-0 flex-col gap-5">
      <div>
        <PageHeader
          titulo={<>Anticipos de clientes</>}
          descripcion={
            <>
              Saldo de cada anticipo y su aplicación a la factura final (la emisión y la amortización
              viven en Facturación; aquí solo se consultan).
            </>
          }
        />
      </div>

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={ESTADOS.map((label, valor) => ({ label, valor }))}
            valor={estado}
            conteos={query.data ? conteos : undefined}
            onChange={setEstado}
          />
        }
        filtros={
          <BarraFiltros
            filtros={
              <div className="flex w-full flex-wrap items-end justify-between gap-3">
                <div className="w-full max-w-md space-y-1">
                  <Label className="text-xs text-ink-muted">Cliente</Label>
                  <ClienteSelectorCxc
                    value={clienteId}
                    onChange={(item) => actualizar({ clienteId: item?.id ?? undefined })}
                    placeholder="Todos los clientes"
                  />
                </div>
                {puedeVerComparativa && (
                  <Link
                    to="/facturacion/reportes/comparativa-anticipos"
                    search={clienteId ? { clienteId } : {}}
                    className="text-xs font-medium text-brand hover:underline"
                  >
                    Ver comparativa en Facturación
                  </Link>
                )}
              </div>
            }
          />
        }
        pie={
          <PaginacionTabla
            total={items.length}
            offset={pagina.offset}
            limit={10}
            tamanos={[]}
            onChange={pagina.cambiar}
          />
        }
      >
        {query.isError ? (
          <ErrorState
            title="No se pudieron cargar los anticipos"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton
            rows={4}
            columns={[
              { width: 'w-24' },
              { width: 'w-32' },
              { width: 'w-28' },
              { width: 'w-28' },
              { width: 'w-28' },
              { width: 'w-24' },
            ]}
          />
        ) : anticipos.length === 0 ? (
          <EmptyState
            icon={<HandCoins className="h-10 w-10" />}
            title={
              clienteId == null ? 'No hay anticipos registrados.' : 'El cliente no tiene anticipos.'
            }
            description="Los anticipos se emiten desde Facturación."
          />
        ) : items.length === 0 ? (
          <EmptyState
            icon={<HandCoins className="h-10 w-10" />}
            title="Sin anticipos en este estado."
            description="Cambia de pestaña para ver los demás anticipos."
          />
        ) : (
          <TooltipProvider delayDuration={150}>
            <div className="overflow-x-auto rounded-md border">
              <Table className="w-full text-sm">
                <TableHeader className="bg-surface-muted/50 text-left text-xs uppercase tracking-wide text-ink-muted">
                  <TableRow>
                    <TableHead className="w-9 px-2 py-2">
                      <span className="sr-only">Aplicaciones</span>
                    </TableHead>
                    <TableHead className="px-3 py-2 font-medium">Anticipo</TableHead>
                    {mostrarCliente && (
                      <TableHead className="px-3 py-2 font-medium">Cliente</TableHead>
                    )}
                    <TableHead className="px-3 py-2 font-medium">Fecha</TableHead>
                    <TableHead className="px-3 py-2 font-medium">Sucursal</TableHead>
                    <TableHead className="px-3 py-2 font-medium">Moneda</TableHead>
                    <TableHead className="px-3 py-2 text-right font-medium">Cobrado</TableHead>
                    <TableHead className="px-3 py-2 text-right font-medium">Aplicado</TableHead>
                    <TableHead className="px-3 py-2 text-right font-medium">Comprometido</TableHead>
                    <TableHead className="px-3 py-2 text-right font-medium">Saldo</TableHead>
                    <TableHead className="px-3 py-2 font-medium">Estado</TableHead>
                    <TableHead className="px-3 py-2 font-medium">Pedido origen</TableHead>
                    <TableHead className="px-3 py-2 font-medium">Observación</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody className="divide-y">
                  {pagina.visibles.map((a) => {
                    const abierto = expandidos.has(a.anticipoId);
                    return (
                      <Fragment key={a.anticipoId}>
                        <FilaAnticipo
                          anticipo={a}
                          abierto={abierto}
                          mostrarCliente={mostrarCliente}
                          onAlternar={() => alternar(a.anticipoId)}
                        />
                        {abierto && (
                          <TableRow className="bg-surface-subtle hover:bg-surface-subtle">
                            <TableCell colSpan={columnas} className="px-3 py-3">
                              <AplicacionesAnticipo anticipo={a} />
                            </TableCell>
                          </TableRow>
                        )}
                      </Fragment>
                    );
                  })}
                </TableBody>
              </Table>
            </div>

            <div className="flex flex-col gap-2">
              <p className="text-xs text-ink-muted">
                Totales por moneda, sin anticipos cancelados.
              </p>
              <div className="flex flex-wrap gap-4">
                {(query.data?.totalesPorMoneda ?? []).map((t) => (
                  <div key={t.moneda} className="grid gap-3 sm:grid-cols-3">
                    <KpiCard
                      etiqueta={`Cobrado ${t.moneda}`}
                      valor={formatoMonto(t.cobrado, t.moneda)}
                    />
                    <KpiCard
                      etiqueta={`Aplicado ${t.moneda}`}
                      valor={formatoMonto(t.aplicado, t.moneda)}
                    />
                    <KpiCard
                      etiqueta={`Saldo ${t.moneda}`}
                      valor={formatoMonto(t.saldo, t.moneda)}
                      tono="info"
                    />
                  </div>
                ))}
              </div>
            </div>
          </TooltipProvider>
        )}
      </ListadoModulo>
    </div>
  );
}

function FilaAnticipo({
  anticipo: a,
  abierto,
  mostrarCliente,
  onAlternar,
}: {
  anticipo: AnticipoConAplicacionesItem;
  abierto: boolean;
  mostrarCliente: boolean;
  onAlternar: () => void;
}) {
  return (
    <TableRow className={cn('h-12 hover:bg-surface-subtle', abierto && 'bg-surface-row-focus')}>
      <TableCell className="px-2 py-2">
        <button
          type="button"
          onClick={onAlternar}
          aria-expanded={abierto}
          aria-label={`Ver aplicaciones de ${a.folio}`}
          className="inline-flex h-7 w-7 items-center justify-center rounded-md text-ink-muted hover:bg-surface-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand"
        >
          <ChevronRight
            className={cn('h-4 w-4 transition-transform', abierto && 'rotate-90')}
            aria-hidden="true"
          />
        </button>
      </TableCell>
      <TableCell className="px-3 py-2">
        <ConUuid texto={a.folio} uuid={a.uuid} />
      </TableCell>
      {mostrarCliente && (
        <TableCell className="max-w-56 truncate px-3 py-2" title={a.clienteNombre}>
          {a.clienteNombre}
        </TableCell>
      )}
      <TableCell className="px-3 py-2 text-ink-secondary">
        {a.fechaTimbrado ? formatDate(a.fechaTimbrado) : '—'}
      </TableCell>
      <TableCell className="px-3 py-2 text-ink-secondary">
        {a.sucursalNombre ?? <span className="text-ink-muted">Sin sucursal</span>}
      </TableCell>
      <TableCell className="px-3 py-2 font-mono text-xs">{a.moneda}</TableCell>
      <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
        {formatoMonto(a.cobrado, a.moneda)}
      </TableCell>
      <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
        {formatoMonto(a.aplicado, a.moneda)}
      </TableCell>
      <TableCell className="px-3 py-2 text-right font-mono tabular-nums text-ink-muted">
        {formatoMonto(a.comprometido, a.moneda)}
      </TableCell>
      <TableCell className="px-3 py-2 text-right font-mono font-medium tabular-nums">
        {formatoMonto(a.saldo, a.moneda)}
      </TableCell>
      <TableCell className="px-3 py-2">
        <EstadoCxcBadge entidad="anticipoAplicacion" estado={a.estado} />
      </TableCell>
      <TableCell className="px-3 py-2 font-mono text-xs">{a.pedidoOrigenRef ?? '—'}</TableCell>
      <TableCell className="px-3 py-2 text-xs text-warning-fg">{a.observacion ?? ''}</TableCell>
    </TableRow>
  );
}

/** Folio en mono con el UUID en tooltip (folio como cara al usuario). */
function ConUuid({ texto, uuid }: { texto: string | null; uuid: string | null }) {
  if (!texto) return <span className="text-ink-muted">—</span>;
  if (!uuid) return <span className="font-mono text-xs font-medium">{texto}</span>;
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span className="cursor-help font-mono text-xs font-medium" tabIndex={0}>
          {texto}
        </span>
      </TooltipTrigger>
      <TooltipContent>
        <span className="font-mono">UUID {uuid}</span>
      </TooltipContent>
    </Tooltip>
  );
}

function AplicacionesAnticipo({ anticipo }: { anticipo: AnticipoConAplicacionesItem }) {
  return (
    <div className="flex flex-col gap-2">
      <p className="text-xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
        Aplicaciones a factura final
      </p>
      {anticipo.aplicaciones.length === 0 ? (
        <p className="text-sm text-ink-muted">Sin aplicaciones</p>
      ) : (
        <div className="overflow-x-auto rounded-md border bg-surface-card">
          <Table className="w-full text-sm">
            <TableHeader className="text-left text-xs uppercase tracking-wide text-ink-muted">
              <TableRow>
                <TableHead className="px-3 py-2 font-medium">Factura final</TableHead>
                <TableHead className="px-3 py-2 font-medium">Egreso 07</TableHead>
                <TableHead className="px-3 py-2 font-medium">UUID egreso</TableHead>
                <TableHead className="px-3 py-2 font-medium">Fecha</TableHead>
                <TableHead className="px-3 py-2 text-right font-medium">Importe</TableHead>
                <TableHead className="px-3 py-2 font-medium">Estado del egreso</TableHead>
                <TableHead className="px-3 py-2 font-medium">Tipo</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody className="divide-y">
              {anticipo.aplicaciones.map((ap, i) => (
                <FilaAplicacion key={i} aplicacion={ap} moneda={anticipo.moneda} />
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}

function FilaAplicacion({
  aplicacion: ap,
  moneda,
}: {
  aplicacion: AplicacionAnticipoItem;
  moneda: string;
}) {
  // Solo «Aplicación» resta saldo; el egreso cancelado se tacha y la
  // vinculación sin egreso (comprometido) se atenúa.
  const cancelado = ap.tipo === 'Egreso cancelado';
  const comprometido = ap.tipo === 'Vinculación sin egreso';
  return (
    <TableRow className="h-10">
      <TableCell className="px-3 py-2">
        <ConUuid texto={ap.facturaFinalFolio} uuid={ap.facturaFinalUuid} />
      </TableCell>
      <TableCell className="px-3 py-2 font-mono text-xs font-medium">
        {ap.egresoFolio ?? <span className="text-ink-muted">Sin egreso</span>}
      </TableCell>
      <TableCell className="px-3 py-2 font-mono text-2xs text-ink-secondary">
        {ap.egresoUuid ?? '—'}
      </TableCell>
      <TableCell className="px-3 py-2 text-ink-secondary">
        {ap.fechaAplicacion ? formatDate(ap.fechaAplicacion) : '—'}
      </TableCell>
      <TableCell
        className={cn(
          'px-3 py-2 text-right font-mono tabular-nums',
          comprometido && 'text-ink-muted',
          cancelado && 'text-ink-muted line-through',
        )}
      >
        {formatoMonto(ap.importe, moneda)}
      </TableCell>
      <TableCell className="px-3 py-2 text-xs">{ap.estadoEgreso ?? '—'}</TableCell>
      <TableCell className="px-3 py-2 text-xs text-ink-secondary">{ap.tipo}</TableCell>
    </TableRow>
  );
}
