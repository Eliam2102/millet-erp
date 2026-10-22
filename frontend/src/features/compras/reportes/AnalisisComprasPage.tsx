import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { apiRequest, esApiError } from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EmpresaSelector } from '@/components/auth/EmpresaSelector';
import { ProveedorSelector } from '@/components/erp/selectors/ProveedorSelector';
import { ArticuloSelector } from '@/components/erp/selectors/ArticuloSelector';
import { DepartamentoSelector } from '@/components/erp/selectors/DepartamentoSelector';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { ReporteShell } from '@/components/erp/reportes/ReporteShell';
import { PaginacionTabla } from '@/components/erp/data/PaginacionTabla';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

interface Movimiento {
  documentoId: string;
  folio: string;
  fecha: string;
  cantidad: number;
  esDevolucion: boolean;
}
interface Partida {
  ordenCompraId: string;
  lineaId: string;
  folio: string;
  proveedorId: string;
  proveedor: string;
  articulo: string;
  departamento: string;
  moneda: string;
  cantidad: number;
  unidad: string;
  importeCompra: number;
  recibidoNeto: number;
  porRecibir: number;
  porFacturar: number;
  porPagar: number;
  diferenciaPrecio: number;
  incidencias: number;
  compromiso: string | null;
  diasAtraso: number;
  movimientos: Movimiento[];
}
interface Proveedor {
  proveedorId: string;
  proveedor: string;
  moneda: string;
  partidas: number;
  partidasEvaluadas: number;
  porcentajeATiempo: number | null;
  porcentajeCompleto: number | null;
  incidencias: number;
  importeCompra: number;
  porPagar: number;
  diferenciaPrecio: number;
  lineasIds: string[];
}
interface Analisis {
  generadoEn: string;
  partidas: Partida[];
  proveedores: Proveedor[];
}
const texto = (clave: string, etiqueta: string) => ({ clave, etiqueta, tipo: 'texto' as const });
const numero = (clave: string, etiqueta: string) => ({ clave, etiqueta, tipo: 'numero' as const });
const columnasProveedor = [
  texto('proveedor', 'Proveedor'),
  texto('moneda', 'Moneda'),
  numero('partidas', 'Partidas'),
  numero('partidasEvaluadas', 'Partidas evaluadas'),
  numero('porcentajeATiempo', '% a tiempo'),
  numero('porcentajeCompleto', '% completo'),
  numero('incidencias', 'Incidencias'),
  numero('importeCompra', 'Compras'),
  numero('porPagar', 'Por pagar'),
  numero('diferenciaPrecio', 'Diferencia de precio'),
];
const columnasPartida = [
  texto('folio', 'Orden de compra'),
  texto('articulo', 'Artículo'),
  texto('departamento', 'Departamento'),
  texto('moneda', 'Moneda'),
  texto('unidad', 'Unidad'),
  numero('cantidad', 'Pedido'),
  numero('recibidoNeto', 'Recibido neto'),
  numero('porRecibir', 'Por recibir'),
  numero('porFacturar', 'Por facturar'),
  numero('importeCompra', 'Compra'),
  numero('porPagar', 'Por pagar'),
  numero('diferenciaPrecio', 'Diferencia de precio'),
  numero('incidencias', 'Incidencias'),
  texto('compromiso', 'Compromiso vigente'),
  numero('diasAtraso', 'Días de atraso'),
  texto('documentos', 'Movimientos al corte'),
];

function celdaAnalisis(fila: Proveedor | Partida, clave: string) {
  const valor = (fila as unknown as Record<string, unknown>)[clave];
  return typeof valor === 'number' &&
    ['importeCompra', 'porPagar', 'diferenciaPrecio'].includes(clave)
    ? valor.toLocaleString('es-MX', { style: 'currency', currency: fila.moneda })
    : valor;
}

export function AnalisisComprasPage() {
  const empresaId = useAuthStore((s) => s.currentEmpresaId);
  const sucursalId = useAuthStore((s) => s.currentSucursalId);
  return (
    <AnalisisContenido
      key={`${empresaId}:${sucursalId}`}
      empresaId={empresaId}
      sucursalInicial={sucursalId}
    />
  );
}
function AnalisisContenido({
  empresaId,
  sucursalInicial,
}: {
  empresaId: string | null;
  sucursalInicial: string | null;
}) {
  const empresas = useAuthStore((s) => s.empresas);
  const [desde, setDesde] = useState('');
  const [hasta, setHasta] = useState('');
  const [proveedorId, setProveedor] = useState<string | null>(null);
  const [articuloId, setArticulo] = useState<string | null>(null);
  const [departamentoId, setDepartamento] = useState<string | null>(null);
  const [sucursalId, setSucursal] = useState(sucursalInicial);
  const [consulta, setConsulta] = useState<Record<string, string> | null>(null);
  const [seleccion, setSeleccion] = useState<string[] | null>(null);
  const [offset, setOffset] = useState(0);
  const [detalleOffset, setDetalleOffset] = useState(0);
  const query = useQuery({
    queryKey: ['compras', 'analisis', empresaId, consulta],
    enabled: consulta != null,
    queryFn: async ({ signal }) =>
      (
        await apiRequest<Analisis>(
          `/api/v1/compras/ordenes/analisis?${new URLSearchParams(consulta ?? {})}`,
          { signal },
        )
      ).data,
  });
  const filtrosAplicados = { Desde: consulta?.desde ?? null, Hasta: consulta?.hasta ?? null };
  const datos = query.data;
  const detalle =
    datos?.partidas
      .filter((p) => seleccion?.includes(p.lineaId))
      .map((p) => ({
        ...p,
        documentos:
          p.movimientos
            .map(
              (m) =>
                `${m.folio}: ${m.esDevolucion ? '−' : '+'}${m.cantidad} ${p.unidad} (${m.fecha})`,
            )
            .join('; ') || 'Sin movimientos',
      })) ?? [];
  const totales = [...new Set(datos?.partidas.map((p) => p.moneda))].map((moneda) => {
    const partidas = datos?.partidas.filter((p) => p.moneda === moneda) ?? [];
    return {
      moneda,
      partidas,
      compra: partidas.reduce((s, p) => s + p.importeCompra, 0),
      pagar: partidas.reduce((s, p) => s + p.porPagar, 0),
      diferencia: partidas.reduce((s, p) => s + p.diferenciaPrecio, 0),
    };
  });
  function abrir(ids: string[]) {
    setSeleccion(ids);
    setDetalleOffset(0);
  }
  function consultar(e: React.FormEvent) {
    e.preventDefault();
    setSeleccion(null);
    setOffset(0);
    setConsulta({
      desde,
      hasta,
      ...(proveedorId ? { proveedorId } : {}),
      ...(articuloId ? { articuloId } : {}),
      ...(departamentoId ? { departamentoId } : {}),
      ...(sucursalId ? { sucursalId } : {}),
    });
  }
  return (
    <div className="flex min-h-0 flex-col gap-5 text-sm tabular-nums">
      <h1 className="text-3xl font-semibold text-ink">Análisis de compras</h1>
      <p className="text-ink-muted">
        Órdenes autorizadas por fecha de documento, con movimientos hasta el fin del periodo.
        Importes separados por moneda.
      </p>
      <ReporteShell
        reporte={
          datos
            ? {
                titulo: 'Cumplimiento por proveedor',
                generadoEn: datos.generadoEn,
                filtrosAplicados,
                columnas: columnasProveedor,
                filas: datos.proveedores,
              }
            : undefined
        }
        filasVisibles={datos?.proveedores.slice(offset, offset + 10)}
        isLoading={query.isFetching}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => void query.refetch()}
        obtenerCelda={celdaAnalisis}
        nombreArchivo="analisis-compras-proveedores"
        accionFila={(p) => (
          <Button
            variant="ghost"
            size="sm"
            aria-label={`Ver sustento de ${p.proveedor} en ${p.moneda}`}
            onClick={() => abrir(p.lineasIds)}
          >
            Ver sustento
          </Button>
        )}
        filtrosUi={
          <form onSubmit={consultar} className="flex flex-wrap items-end gap-3">
            <div>
              <EmpresaSelector />
              <span className="text-xs text-ink-muted">
                {empresas.find((e) => e.id === empresaId)?.razonSocial ?? 'Empresa de la sesión'}
              </span>
            </div>
            <div className="space-y-1">
              <Label htmlFor="analisis-desde">Desde</Label>
              <Input
                id="analisis-desde"
                type="date"
                required
                value={desde}
                onChange={(e) => setDesde(e.target.value)}
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="analisis-hasta">Hasta</Label>
              <Input
                id="analisis-hasta"
                type="date"
                required
                min={desde}
                value={hasta}
                onChange={(e) => setHasta(e.target.value)}
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="analisis-proveedor">Proveedor</Label>
              <ProveedorSelector
                triggerProps={{ id: 'analisis-proveedor' }}
                value={proveedorId}
                onChange={setProveedor}
                placeholder="Todos los proveedores"
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="analisis-sucursal">Sucursal</Label>
              <SucursalSelector
                triggerProps={{ id: 'analisis-sucursal' }}
                value={sucursalId}
                onChange={setSucursal}
                placeholder="Sucursales permitidas"
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="analisis-articulo">Artículo</Label>
              <ArticuloSelector
                triggerProps={{ id: 'analisis-articulo' }}
                value={articuloId}
                onChange={setArticulo}
                placeholder="Todos los artículos"
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="analisis-departamento">Departamento</Label>
              <DepartamentoSelector
                triggerProps={{ id: 'analisis-departamento' }}
                value={departamentoId}
                onChange={setDepartamento}
                placeholder="Todos los departamentos"
                sucursalId={sucursalId}
              />
            </div>
            <Button type="submit">Consultar</Button>
          </form>
        }
      />
      {datos && !query.isFetching && !query.error && (
        <>
          <PaginacionTabla
            total={datos.proveedores.length}
            offset={offset}
            limit={10}
            tamanos={[]}
            onChange={setOffset}
          />
          <p className="text-xs text-ink-muted">
            A tiempo: partidas completas entregadas dentro del compromiso vigente, entre partidas
            entregadas o vencidas con fecha conocida. Completo: partidas recibidas netas entre
            partidas físicas. Los servicios no se evalúan por recepción. Sin fecha: no evaluada.
          </p>
          <div className="flex flex-wrap gap-3" aria-label="Totales por moneda">
            {totales.map((t) => (
              <section key={t.moneda} className="rounded-lg bg-surface-card p-4 shadow-card-flat">
                <h2 className="font-semibold">Totales {t.moneda}</h2>
                {(
                  [
                    ['Compras', t.compra],
                    ['Por pagar', t.pagar],
                    ['Diferencia de precio', t.diferencia],
                  ] as const
                ).map(([label, valor]) => (
                  <Button
                    key={label}
                    variant="ghost"
                    className="tabular-nums"
                    onClick={() => abrir(t.partidas.map((p) => p.lineaId))}
                  >
                    {label}:{' '}
                    {valor.toLocaleString('es-MX', { style: 'currency', currency: t.moneda })}
                  </Button>
                ))}
              </section>
            ))}
          </div>
          {seleccion && (
            <section aria-label="Documentos de sustento" className="space-y-3">
              <ReporteShell
                reporte={{
                  titulo: 'Órdenes y movimientos de sustento',
                  generadoEn: datos.generadoEn,
                  filtrosAplicados,
                  columnas: columnasPartida,
                  filas: detalle,
                }}
                isLoading={false}
                error={undefined}
                obtenerCelda={celdaAnalisis}
                filasVisibles={detalle.slice(detalleOffset, detalleOffset + 10)}
                nombreArchivo="analisis-compras-sustento"
                accionFila={(p) => (
                  <div className="flex flex-col gap-1 text-left">
                    <a
                      className="font-mono text-brand underline"
                      href={`/compras/ordenes/${p.ordenCompraId}`}
                    >
                      {p.folio}
                    </a>
                    {p.movimientos.map((m, i) => (
                      <div key={`${m.documentoId}:${i}`}>
                        <a
                          className="font-mono text-brand underline"
                          href={`/almacen/${m.esDevolucion ? 'devoluciones/proveedor' : 'recepciones'}/${m.documentoId}`}
                        >
                          {m.folio}
                        </a>
                        <span className="ml-2 tabular-nums">
                          {m.esDevolucion ? 'Devolución −' : 'Recepción +'}
                          {m.cantidad} {p.unidad} · {m.fecha}
                        </span>
                      </div>
                    ))}
                    {p.movimientos.length === 0 && (
                      <span className="text-ink-muted">Sin recepciones al corte</span>
                    )}
                  </div>
                )}
              />
              <PaginacionTabla
                total={detalle.length}
                offset={detalleOffset}
                limit={10}
                tamanos={[]}
                onChange={setDetalleOffset}
              />
              <p className="text-xs text-ink-muted">
                Por recibir y por facturar son cantidades en la unidad de cada partida. Por pagar
                incluye aplicaciones y se distribuye según el importe neto de las partidas de la
                factura. Compras incluye impuestos y ajustes de la orden, distribuidos por importe
                neto. Diferencia de precio compara el importe neto facturado con el precio neto de
                la orden.
              </p>
            </section>
          )}
        </>
      )}
    </div>
  );
}
