import { useMemo, useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { History, RotateCcw } from 'lucide-react';
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

  const [proveedorId, setProveedorId] = useState<string | null>(null);
  const [fechaDesde, setFechaDesde] = useState<string | null>(null);
  const [cantidadMinima, setCantidadMinima] = useState<string>('');

  const filtros = useMemo(
    () => ({
      proveedorId: proveedorId ?? undefined,
      fechaDesde: fechaDesde ?? undefined,
      cantidadMinima:
        cantidadMinima.trim() === '' ? undefined : Number(cantidadMinima),
    }),
    [proveedorId, fechaDesde, cantidadMinima],
  );

  const query = useUltimas100ComprasMaterial(articuloId, filtros);
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
    <div
      className="space-y-4 p-4"
      data-component="historial-compras-material"
    >
      <header>
        <h1 className="flex items-center gap-2 text-2xl font-semibold tracking-tight">
          <History className="h-6 w-6" />
          Historial de compras del material
        </h1>
        <p className="text-sm text-muted-foreground">
          Últimas 100 líneas de OC para el artículo{' '}
          {articuloQuery.data ? (
            <>
              <code className="font-mono text-xs">
                {articuloQuery.data.clave}
              </code>{' '}
              · {articuloQuery.data.nombre}
            </>
          ) : (
            <code className="font-mono text-xs" title={articuloId}>
              {articuloId.slice(0, 8)}…
            </code>
          )}
          . Útil para análisis de tendencia de precios y proveedores.
        </p>
      </header>

      {/* Filtros */}
      <div className="grid grid-cols-1 gap-3 rounded-md border bg-card p-3 sm:grid-cols-4">
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
      </div>

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
          <div className="overflow-x-auto rounded-md border bg-card">
            <table className="w-full min-w-[720px] text-sm">
              <thead className="bg-muted/50 text-xs uppercase tracking-wide text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 text-left font-medium">Fecha</th>
                  <th className="px-3 py-2 text-left font-medium">OC</th>
                  <th className="px-3 py-2 text-left font-medium">Proveedor</th>
                  <th className="px-3 py-2 text-right font-medium">Cantidad</th>
                  <th className="px-3 py-2 text-left font-medium">UM</th>
                  <th className="px-3 py-2 text-right font-medium">Precio</th>
                  <th className="px-3 py-2 text-left font-medium">Mon.</th>
                </tr>
              </thead>
              <tbody>
                {query.data.items.map((c, i) => (
                  <tr
                    key={c.lineaId}
                    className={cn(i % 2 === 1 ? 'bg-muted/20' : undefined)}
                    data-linea={c.lineaId}
                  >
                    <td className="px-3 py-2 whitespace-nowrap">
                      <DateTimeDisplay value={c.fechaDocumento} />
                    </td>
                    <td className="px-3 py-2 font-mono text-xs">
                      <Link
                        to="/compras/ordenes/$id"
                        params={{ id: c.ordenCompraId }}
                        className="hover:underline"
                        data-link={c.ordenCompraId}
                      >
                        {c.folioOc}
                      </Link>
                    </td>
                    <td className="px-3 py-2">
                      {resolverProveedor(c.proveedorId)}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums">
                      {c.cantidad.toFixed(2)}
                    </td>
                    <td className="px-3 py-2">{c.unidadMedida}</td>
                    <td className="px-3 py-2 text-right tabular-nums font-medium">
                      {c.precioUnitario.toFixed(4)}
                    </td>
                    <td className="px-3 py-2 text-muted-foreground">
                      {c.moneda}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            <p className="border-t bg-muted/30 px-3 py-1.5 text-xs text-muted-foreground">
              {query.data.items.length} líneas mostradas (cap 100).
            </p>
          </div>
        ))}
    </div>
  );
}

function Field({
  label,
  children,
}: {
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1">
      <label className="text-xs font-medium text-muted-foreground">
        {label}
      </label>
      {children}
    </div>
  );
}
