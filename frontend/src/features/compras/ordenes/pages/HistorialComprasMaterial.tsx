import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { BarraFiltros, PaginacionTabla } from '@/components/erp';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { useMemo, useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { RotateCcw } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
  DateTimeDisplay,
  ProveedorSelector,
  DatePickerField,
} from '@/components/erp';
import { useUltimas100ComprasMaterial } from '@/features/compras/ordenes/api/useUltimas100ComprasMaterial';
import { useArticulo, useProveedores, mapById } from '@/features/catalogos/api';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';
import { PageHeader } from '@/components/erp/PageHeader';
import { formatCantidad } from '@/lib/format-cantidad';
import { useDecimalesUnidad } from '@/components/erp/forms/useDecimalesUnidad';

/**
 * <c>P11 — Historial de últimas 100 compras del material</c>
 * (UF7-PR3, §8.5).
 *
 * <para>Tabla con filtros (proveedor, fecha desde, cantidad mínima) +
 * cap 100 líneas ordenadas por fecha doc DESC. Click en folio navega
 * al detalle de la OC.</para>
 *
 * <para>Permiso: <c>compras.ordenes.leer</c>. Entry points (UF7-PR3):</para>
 * <list>
 *   <item>Desde menú reportes (futuro nav).</item>
 *   <item>Desde editor de línea en P3 — botón "Ver historial del
 *   material" (futuro UF8-PR1 polish).</item>
 *   <item>Desde detalle de artículo cuando exista módulo de
 *   catálogos en frontend.</item>
 * </list>
 */
export function HistorialComprasMaterial() {
  const { id: articuloId } = useParams({
    from: '/_app/compras/articulos/$id/historial-compras',
  });
  const decimalesUnidad = useDecimalesUnidad();

  const [pagina, setPagina] = useState(0);
  const [proveedorId, setProveedorId] = useState<string | null>(null);
  const [fechaDesde, setFechaDesde] = useState<string | null>(null);
  const [cantidadMinima, setCantidadMinima] = useState<string>('');

  const filtros = useMemo(
    () => ({
      proveedorId: proveedorId ?? undefined,
      fechaDesde: fechaDesde ?? undefined,
      cantidadMinima: cantidadMinima.trim() === '' ? undefined : Number(cantidadMinima),
    }),
    [proveedorId, fechaDesde, cantidadMinima],
  );

  const query = useUltimas100ComprasMaterial(articuloId, filtros);
  const offset =
    Math.min(pagina, Math.max(0, Math.ceil((query.data?.items.length ?? 0) / 10) - 1)) * 10;
  // Etiqueta legible del material del header (nunca GUID de cara al
  // usuario); mientras carga o si no resuelve, cae al id truncado.
  const articuloQuery = useArticulo(articuloId);
  // limit alto + includeInactivas para no perder proveedores
  // históricos cuando esta vista cubre 100 OCs hacia atrás.
  const proveedoresQuery = useProveedores({
    limit: 1000,
    includeInactivas: true,
  });
  const proveedoresMap = useMemo(
    () => mapById(proveedoresQuery.data?.items),
    [proveedoresQuery.data],
  );

  function resolverProveedor(id: string): string {
    const p = proveedoresMap.get(id);
    if (p == null) return id.slice(0, 8) + '…';
    return p.nombreComercial ?? p.razonSocial;
  }

  function resetFiltros() {
    setProveedorId(null);
    setFechaDesde(null);
    setCantidadMinima('');
  }

  return (
    <div className="space-y-4" data-component="historial-compras-material">
      <PageHeader
        titulo="Historial de compras del material"
        descripcion={
          <>
            Últimas 100 partidas de OC para el artículo{' '}
            {articuloQuery.data ? (
              <>
                <code className="font-mono text-xs">{articuloQuery.data.clave}</code> ·{' '}
                {articuloQuery.data.nombre}
              </>
            ) : (
              <code className="font-mono text-xs" title={articuloId}>
                {articuloId.slice(0, 8)}…
              </code>
            )}
            .
          </>
        }
      />

      {/* Filtros */}
      <ListadoModulo
        pie={
          <PaginacionTabla
            total={query.data?.items.length ?? 0}
            offset={offset}
            limit={10}
            tamanos={[]}
            onChange={(next) => setPagina(next / 10)}
          />
        }
        filtros={
          <BarraFiltros
            filtros={
              <>
                <Field label="Proveedor">
                  <ProveedorSelector value={proveedorId} onChange={setProveedorId} />
                </Field>
                <Field label="Fecha desde">
                  <DatePickerField value={fechaDesde} onChange={setFechaDesde} />
                </Field>
                <Field label="Cantidad mínima">
                  <Input
                    type="number"
                    min={0}
                    value={cantidadMinima}
                    onChange={(e) => setCantidadMinima(e.target.value)}
                    placeholder="0"
                  />
                </Field>
                <div className="flex items-end">
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={resetFiltros}
                    data-action="reset-filtros"
                  >
                    <RotateCcw className="mr-1 h-3.5 w-3.5" />
                    Limpiar
                  </Button>
                </div>
              </>
            }
          />
        }
      >
        {/* Tabla */}
        {query.isLoading && <TableSkeleton rows={8} />}
        {query.isError && (
          <ErrorState
            title="No se pudo cargar el historial de compras"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => {
              void query.refetch();
            }}
          />
        )}
        {query.data &&
          (query.data.items.length === 0 ? (
            <EmptyState
              title="Sin compras registradas para este artículo con los filtros aplicados."
              description="Prueba cambiando o limpiando los filtros."
            />
          ) : (
            <div className="overflow-x-auto rounded-md border bg-surface-card">
              <Table className="w-full min-w-full text-sm">
                <TableHeader className="bg-surface-muted/50 text-xs uppercase tracking-wide text-ink-muted">
                  <TableRow>
                    <TableHead className="px-3 py-2 text-left font-medium">Fecha</TableHead>
                    <TableHead className="px-3 py-2 text-left font-medium">OC</TableHead>
                    <TableHead className="px-3 py-2 text-left font-medium">Proveedor</TableHead>
                    <TableHead className="px-3 py-2 text-right font-medium">Cantidad</TableHead>
                    <TableHead className="px-3 py-2 text-left font-medium">UM</TableHead>
                    <TableHead className="px-3 py-2 text-right font-medium">Precio</TableHead>
                    <TableHead className="px-3 py-2 text-left font-medium">Mon.</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody className="[&_tr]:h-12">
                  {query.data.items.slice(offset, offset + 10).map((c, i) => (
                    <TableRow
                      key={c.lineaId}
                      className={cn(i % 2 === 1 ? 'bg-surface-muted/20' : undefined)}
                      data-linea={c.lineaId}
                    >
                      <TableCell className="px-3 py-2 whitespace-nowrap">
                        <DateTimeDisplay value={c.fechaDocumento} />
                      </TableCell>
                      <TableCell className="px-3 py-2 font-mono text-xs">
                        <Link
                          to="/compras/ordenes/$id"
                          params={{ id: c.ordenCompraId }}
                          className="hover:underline"
                          data-link={c.ordenCompraId}
                        >
                          {c.folioOc}
                        </Link>
                      </TableCell>
                      <TableCell className="px-3 py-2">
                        {resolverProveedor(c.proveedorId)}
                      </TableCell>
                      <TableCell className="px-3 py-2 text-right tabular-nums">
                        {formatCantidad(c.cantidad, decimalesUnidad.porCodigo(c.unidadMedida))}
                      </TableCell>
                      <TableCell className="px-3 py-2">{c.unidadMedida}</TableCell>
                      <TableCell className="px-3 py-2 text-right tabular-nums font-medium">
                        {formatCantidad(c.precioUnitario, 4)}
                      </TableCell>
                      <TableCell className="px-3 py-2 text-ink-muted">{c.moneda}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
              <p className="border-t bg-surface-muted/30 px-3 py-1.5 text-xs text-ink-muted">
                {query.data.items.length} partidas mostradas (cap 100).
              </p>
            </div>
          ))}
      </ListadoModulo>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1">
      <label className="text-xs font-medium text-ink-muted">{label}</label>
      {children}
    </div>
  );
}
