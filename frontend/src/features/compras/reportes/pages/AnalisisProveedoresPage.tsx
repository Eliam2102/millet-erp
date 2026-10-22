import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { useState } from 'react';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Button } from '@/components/ui/button';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import {
  ArticuloSelector,
  DepartamentoSelector,
  ProveedorSelector,
  ReporteShell,
  SucursalSelector,
} from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { EstadoOrdenCompra, estadoOcToString } from '@/features/compras/ordenes/api/types';
import {
  useAnalisisCompras,
  useAnalisisCumplimiento,
  useAnalisisSaldos,
  type AnalisisResponse,
  type FiltrosAnalisis,
  type IndicadorDrill,
} from '@/features/compras/reportes/api/useAnalisisProveedores';
import {
  analisisToShell,
  type FilaAnalisis,
} from '@/features/compras/reportes/lib/analisis-adapter';
import {
  AGRUPAR_POR,
  errorPeriodo,
  trimestreActual,
  type AnalisisProveedoresSearch,
} from '@/features/compras/reportes/lib/analisis-proveedores-search-schema';
import {
  DrillOrdenesSheet,
  type DrillTarget,
} from '@/features/compras/reportes/pages/DrillOrdenesSheet';
import { esApiError } from '@/lib/api';

const FROM = '/_app/compras/reportes/analisis-proveedores' as const;

const SELECT_CLASS =
  'flex h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3 text-sm text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand';

const ETIQUETA_AGRUPAR: Record<(typeof AGRUPAR_POR)[number], string> = {
  proveedor: 'Proveedor',
  articulo: 'Artículo',
  departamento: 'Departamento',
  mes: 'Mes',
};

const INDICADORES_CUMPLIMIENTO: readonly IndicadorDrill[] = [
  'a-tiempo',
  'completo',
  'incidencias',
  'diferencias-precio',
];

/**
 * Compras → Análisis de proveedores (F1-COM-13, solo lectura). Tres vistas
 * (Compras / Saldos / Cumplimiento) sobre `<ReporteShell/>` con filtros en la
 * URL, exportación PDF/Excel y detalle de órdenes en un Sheet (ADR-0036).
 */
export function AnalisisProveedoresPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const [drill, setDrill] = useState<DrillTarget | null>(null);

  const trimestre = trimestreActual();
  const filtros: FiltrosAnalisis = {
    desde: search.desde ?? trimestre.desde,
    hasta: search.hasta ?? trimestre.hasta,
    sucursalId: search.sucursalId,
    proveedorId: search.proveedorId,
    articuloId: search.articuloId,
    departamentoId: search.departamentoId,
    moneda: search.moneda,
    estado: search.estado,
  };
  const errPeriodo = errorPeriodo(filtros.desde, filtros.hasta);
  const activo = errPeriodo == null;

  function setSearch(patch: Partial<AnalisisProveedoresSearch>) {
    navigate({ to: '/compras/reportes/analisis-proveedores', search: { ...search, ...patch } });
  }

  const compras = useAnalisisCompras(
    { ...filtros, agruparPor: search.agruparPor },
    activo && search.tab === 'compras',
  );
  const saldos = useAnalisisSaldos(filtros, activo && search.tab === 'saldos');
  const cumplimiento = useAnalisisCumplimiento(filtros, activo && search.tab === 'cumplimiento');

  const abrirTotal = (indicador: IndicadorDrill, moneda: string, indicadores?: readonly IndicadorDrill[]) =>
    setDrill({ titulo: `Total ${moneda}`, indicador, moneda, indicadores });

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        titulo="Análisis de proveedores"
        descripcion="Compras por periodo, saldos pendientes por orden y cumplimiento de entrega de cada proveedor."
      />

      <FiltrosAnalisisUi filtros={filtros} moneda={search.moneda} errorPeriodo={errPeriodo} setSearch={setSearch} />

      <Tabs value={search.tab} onValueChange={(v) => setSearch({ tab: v as AnalisisProveedoresSearch['tab'] })}>
        <TabsList aria-label="Vistas del análisis">
          <TabsTrigger value="compras">Compras</TabsTrigger>
          <TabsTrigger value="saldos">Saldos</TabsTrigger>
          <TabsTrigger value="cumplimiento">Cumplimiento</TabsTrigger>
        </TabsList>

        <TabsContent value="compras" className="mt-4 space-y-3">
          <div className="max-w-56 space-y-1">
            <Label htmlFor="agruparPor" className="text-xs">Agrupar por</Label>
            <select
              id="agruparPor"
              className={SELECT_CLASS}
              value={search.agruparPor}
              onChange={(e) => setSearch({ agruparPor: e.target.value as AnalisisProveedoresSearch['agruparPor'] })}
            >
              {AGRUPAR_POR.map((a) => (
                <option key={a} value={a}>{ETIQUETA_AGRUPAR[a]}</option>
              ))}
            </select>
          </div>
          <Vista
            query={compras}
            activo={activo}
            nombreArchivo="analisis-compras"
            accion={(fila) => {
              if (fila.__total) {
                return <BotonVer fila={fila} onClick={() => abrirTotal('compras', fila.__total!)} />;
              }
              if (search.agruparPor !== 'proveedor' || !fila.grupoId) return null;
              return (
                <BotonVer
                  fila={fila}
                  etiqueta={String(fila.grupo)}
                  onClick={() =>
                    setDrill({
                      titulo: String(fila.grupo),
                      indicador: 'compras',
                      proveedorId: String(fila.grupoId),
                      moneda: String(fila.moneda ?? '') || undefined,
                    })
                  }
                />
              );
            }}
          />
        </TabsContent>

        <TabsContent value="saldos" className="mt-4 space-y-3">
          {saldos.data?.saldoInicial && saldos.data.saldoInicial.length > 0 && (
            <section
              aria-labelledby="saldo-inicial"
              className="rounded-lg bg-surface-card p-4 shadow-card-flat"
            >
              <h3 id="saldo-inicial" className="text-md font-semibold text-ink">Saldo inicial por moneda</h3>
              <dl className="mt-2 grid gap-3 sm:grid-cols-3">
                {saldos.data.saldoInicial.map((s) => (
                  <div key={s.moneda} className="space-y-0.5">
                    <dt className="text-xs text-ink-muted">{s.moneda} · {s.ordenes} órdenes</dt>
                    <dd className="text-sm font-medium tabular-nums text-ink">
                      {s.pendienteFisico.toLocaleString('es-MX', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
                      <span className="ml-1 text-xs font-normal text-ink-muted">pendiente físico</span>
                    </dd>
                  </div>
                ))}
              </dl>
            </section>
          )}
          <Vista
            query={saldos}
            activo={activo}
            nombreArchivo="analisis-saldos"
            accion={(fila) =>
              fila.__total ? (
                <BotonVer fila={fila} onClick={() => abrirTotal('saldos', fila.__total!)} />
              ) : fila.ocId ? (
                <span className="inline-flex gap-3 text-xs font-medium">
                  <Link to="/compras/ordenes/$id" params={{ id: String(fila.ocId) }} className="text-brand hover:underline">
                    Orden<span className="sr-only"> {String(fila.folio)}</span>
                  </Link>
                  <Link to="/compras/trazabilidad/oc/$id" params={{ id: String(fila.ocId) }} className="text-brand hover:underline">
                    Trazabilidad<span className="sr-only"> {String(fila.folio)}</span>
                  </Link>
                </span>
              ) : null
            }
          />
        </TabsContent>

        <TabsContent value="cumplimiento" className="mt-4 space-y-3">
          <Vista
            query={cumplimiento}
            activo={activo}
            nombreArchivo="analisis-cumplimiento"
            accion={(fila) =>
              fila.__total ? (
                <BotonVer
                  fila={fila}
                  onClick={() => abrirTotal('a-tiempo', fila.__total!, INDICADORES_CUMPLIMIENTO)}
                />
              ) : fila.proveedorId ? (
                <BotonVer
                  fila={fila}
                  etiqueta={String(fila.proveedor)}
                  onClick={() =>
                    setDrill({
                      titulo: String(fila.proveedor),
                      indicador: 'a-tiempo',
                      proveedorId: String(fila.proveedorId),
                      indicadores: INDICADORES_CUMPLIMIENTO,
                    })
                  }
                />
              ) : null
            }
          />
          {cumplimiento.data && (
            <section aria-labelledby="formulas" className="rounded-lg bg-surface-subtle p-4 text-xs text-ink-secondary">
              <h3 id="formulas" className="text-sm font-semibold text-ink">Fórmulas</h3>
              <ul className="mt-1.5 list-disc space-y-1 pl-4">
                {(cumplimiento.data.formulas ?? []).map((f) => (
                  <li key={f}>{f}</li>
                ))}
              </ul>
              <p className="mt-2 text-ink-muted">
                n/d: sin líneas que evaluar en el periodo, no hay porcentaje que calcular.
              </p>
            </section>
          )}
        </TabsContent>
      </Tabs>

      <DrillOrdenesSheet target={drill} filtros={filtros} onClose={() => setDrill(null)} />
    </div>
  );
}

function BotonVer({
  fila,
  etiqueta,
  onClick,
}: {
  fila: FilaAnalisis;
  etiqueta?: string;
  onClick: () => void;
}) {
  const quien = etiqueta ?? `total ${fila.__total}`;
  return (
    <Button type="button" variant="outline" size="sm" onClick={onClick} aria-label={`Ver órdenes de ${quien}`}>
      Ver órdenes
    </Button>
  );
}

function Vista({
  query,
  activo,
  nombreArchivo,
  accion,
}: {
  query: { data?: AnalisisResponse; isLoading: boolean; error: unknown; refetch: () => unknown };
  activo: boolean;
  nombreArchivo: string;
  accion: (fila: FilaAnalisis) => React.ReactNode;
}) {
  const shell = query.data ? analisisToShell(query.data) : undefined;
  return (
    <ReporteShell<FilaAnalisis>
      nivelTitulo="h2"
      reporte={shell?.reporte}
      notasExport={shell?.notasExport}
      isLoading={activo && query.isLoading}
      error={esApiError(query.error) ? query.error : undefined}
      onRetry={() => query.refetch()}
      nombreArchivo={nombreArchivo}
      accionFila={accion}
      etiquetaAccion="Ver órdenes"
    />
  );
}

interface FiltrosUiProps {
  filtros: FiltrosAnalisis;
  moneda: string | undefined;
  errorPeriodo: string | null;
  setSearch: (patch: Partial<AnalisisProveedoresSearch>) => void;
}

function FiltrosAnalisisUi({ filtros, moneda, errorPeriodo, setSearch }: FiltrosUiProps) {
  return (
    <form
      aria-label="Filtros del análisis"
      onSubmit={(e) => e.preventDefault()}
      className="rounded-lg bg-surface-card p-4 shadow-card-flat"
    >
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Campo id="f-desde" label="Desde">
          <Input id="f-desde" type="date" value={filtros.desde} onChange={(e) => setSearch({ desde: e.target.value || undefined })} aria-invalid={errorPeriodo != null} />
        </Campo>
        <Campo id="f-hasta" label="Hasta">
          <Input id="f-hasta" type="date" value={filtros.hasta} onChange={(e) => setSearch({ hasta: e.target.value || undefined })} aria-invalid={errorPeriodo != null} />
        </Campo>
        <Campo id="f-sucursal" label="Sucursal">
          <SucursalSelector value={filtros.sucursalId} onChange={(id) => setSearch({ sucursalId: id ?? undefined })} placeholder="Todas las sucursales" triggerProps={{ id: 'f-sucursal' }} />
        </Campo>
        <Campo id="f-proveedor" label="Proveedor">
          <ProveedorSelector value={filtros.proveedorId} onChange={(id) => setSearch({ proveedorId: id ?? undefined })} placeholder="Todos los proveedores" triggerProps={{ id: 'f-proveedor' }} />
        </Campo>
        <Campo id="f-articulo" label="Artículo">
          <ArticuloSelector value={filtros.articuloId} onChange={(id) => setSearch({ articuloId: id ?? undefined })} placeholder="Todos los artículos" triggerProps={{ id: 'f-articulo' }} />
        </Campo>
        <Campo id="f-departamento" label="Departamento">
          <DepartamentoSelector value={filtros.departamentoId} onChange={(id) => setSearch({ departamentoId: id ?? undefined })} placeholder="Todos los departamentos" triggerProps={{ id: 'f-departamento' }} />
        </Campo>
        <Campo id="f-moneda" label="Moneda">
          <select id="f-moneda" className={SELECT_CLASS} value={moneda ?? ''} onChange={(e) => setSearch({ moneda: e.target.value || undefined })}>
            <option value="">Todas</option>
            <option value="MXN">MXN</option>
            <option value="USD">USD</option>
          </select>
        </Campo>
        <Campo id="f-estado" label="Estado de la OC">
          <select id="f-estado" className={SELECT_CLASS} value={filtros.estado ?? ''} onChange={(e) => setSearch({ estado: e.target.value || undefined })}>
            <option value="">Todos</option>
            {Object.entries(EstadoOrdenCompra).map(([clave, valor]) => (
              <option key={clave} value={clave}>{estadoOcToString(valor)}</option>
            ))}
          </select>
        </Campo>
      </div>
      {errorPeriodo && (
        <p role="alert" className="mt-3 text-sm font-medium text-danger-fg">{errorPeriodo}</p>
      )}
    </form>
  );
}

function Campo({ id, label, children }: { id: string; label: string; children: React.ReactNode }) {
  return (
    <div className="min-w-0 space-y-1.5">
      <Label htmlFor={id} className="text-xs font-medium text-ink-strong">{label}</Label>
      {children}
    </div>
  );
}
