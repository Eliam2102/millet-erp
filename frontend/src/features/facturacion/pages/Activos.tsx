import { useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import {
  useAutorizacionesActivo,
  useAutorizarVentaActivo,
} from '@/features/facturacion/api/useActivos';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { EstadoAutorizacionActivo } from '@/features/facturacion/api/types';
import type { ActivosSearch } from '@/features/facturacion/lib/activos-search-schema';

const FROM = '/_app/facturacion/activos/' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>Autorizaciones de venta de activo fijo</c> (FE-F9). El Contador General
 * autoriza la venta (calcula valor neto + utilidad/pérdida); el
 * <c>autorizacionId</c> resultante es obligatorio al emitir la factura de
 * venta de activo (la emisión lo exige; el backend rechaza sin él).
 */
export function Activos() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeAutorizar = useHasPermission(
    PermisosCanonicos.FacturacionActivosAutorizar,
  );
  const [formAbierto, setFormAbierto] = useState(false);
  const query = useAutorizacionesActivo(search.estado);
  const items = query.data ?? [];

  function actualizar(parcial: Partial<ActivosSearch>) {
    navigate({ to: '/facturacion/activos', search: { ...search, ...parcial } });
  }

  return (
    <div className="space-y-4 px-4 py-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Autorizaciones de venta de activos
          </h1>
          <p className="text-sm text-muted-foreground">
            El Contador General autoriza la venta antes de timbrar. La emisión de
            venta de activo fijo exige una autorización vigente.
          </p>
        </div>
        {puedeAutorizar && (
          <Button onClick={() => setFormAbierto((v) => !v)}>
            <Plus className="mr-2 h-4 w-4" />
            Autorizar venta
          </Button>
        )}
      </div>

      {formAbierto && puedeAutorizar && (
        <AutorizarForm onDone={() => setFormAbierto(false)} />
      )}

      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">Estado</label>
          <Select
            value={search.estado != null ? String(search.estado) : SENTINEL_ALL}
            onValueChange={(v) =>
              actualizar({
                estado:
                  v === SENTINEL_ALL
                    ? undefined
                    : (Number(v) as ActivosSearch['estado']),
              })
            }
          >
            <SelectTrigger aria-label="Filtrar por estado" className="w-44">
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
              <SelectItem value={String(EstadoAutorizacionActivo.Autorizada)}>
                Autorizada
              </SelectItem>
              <SelectItem value={String(EstadoAutorizacionActivo.Usada)}>
                Usada
              </SelectItem>
              <SelectItem value={String(EstadoAutorizacionActivo.Cancelada)}>
                Cancelada
              </SelectItem>
            </SelectContent>
          </Select>
        </div>
        {search.estado != null && (
          <Button variant="ghost" onClick={() => actualizar({ estado: undefined })}>
            Limpiar
          </Button>
        )}
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las autorizaciones"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={5}
          columns={[
            { width: 'w-40' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-24' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          title="Sin autorizaciones"
          description={
            search.estado != null
              ? 'No hay autorizaciones que coincidan con el filtro.'
              : 'Aún no hay autorizaciones de venta de activo.'
          }
        />
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">Activo</th>
                <th className="px-3 py-2 text-left">Descripción</th>
                <th className="px-3 py-2 text-right">Precio venta</th>
                <th className="px-3 py-2 text-right">Valor neto</th>
                <th className="px-3 py-2 text-right">Utilidad/Pérdida</th>
                <th className="px-3 py-2 text-left">Estado</th>
                <th className="px-3 py-2 text-left">ID autorización</th>
              </tr>
            </thead>
            <tbody>
              {items.map((a) => (
                <tr key={a.id} className="border-t hover:bg-muted/30">
                  <td className="px-3 py-2 font-mono text-xs">{a.activoRef}</td>
                  <td className="px-3 py-2">{a.descripcion}</td>
                  <td className="px-3 py-2 text-right font-mono">
                    {a.precioVenta.toFixed(2)}
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    {a.valorNetoEnLibros.toFixed(2)}
                  </td>
                  <td
                    className={
                      'px-3 py-2 text-right font-mono ' +
                      (a.utilidadOPerdida >= 0
                        ? 'text-emerald-700'
                        : 'text-rose-700')
                    }
                  >
                    {a.utilidadOPerdida.toFixed(2)}
                  </td>
                  <td className="px-3 py-2">{a.estado}</td>
                  <td className="px-3 py-2 font-mono text-xs text-muted-foreground">
                    {a.id}
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

function AutorizarForm({ onDone }: { onDone: () => void }) {
  const [activoRef, setActivoRef] = useState('');
  const [precioVenta, setPrecioVenta] = useState('');
  const idempotencyKey = useFormIdempotencyKey();
  const autorizar = useAutorizarVentaActivo();

  function enviar() {
    const precio = Number(precioVenta);
    if (activoRef.trim() === '' || !(precio > 0)) {
      toast.error('Captura la referencia del activo y un precio de venta > 0.');
      return;
    }
    autorizar.mutate(
      { command: { activoRef: activoRef.trim(), precioVenta: precio }, idempotencyKey },
      {
        onSuccess: (res) => {
          toast.success(
            `Autorizado: ${res.descripcion} · valor neto ${res.valorNetoEnLibros.toFixed(2)} · ${
              res.utilidadOPerdida >= 0 ? 'utilidad' : 'pérdida'
            } ${Math.abs(res.utilidadOPerdida).toFixed(2)} · ID ${res.autorizacionId}`,
            { duration: 12000 },
          );
          onDone();
        },
        onError: (error) =>
          toast.error(
            esApiError(error) ? error.problem.title : 'No se pudo autorizar.',
          ),
      },
    );
  }

  return (
    <div className="grid grid-cols-1 gap-2 rounded-md border border-dashed p-3 sm:grid-cols-4">
      <div className="sm:col-span-4 text-sm font-medium">
        Autorizar venta de activo
      </div>
      <div className="sm:col-span-2">
        <Label className="text-xs">Referencia del activo *</Label>
        <Input
          placeholder="AF-000123"
          value={activoRef}
          onChange={(e) => setActivoRef(e.target.value)}
        />
      </div>
      <div>
        <Label className="text-xs">Precio de venta *</Label>
        <Input
          type="number"
          step="0.01"
          min="0"
          value={precioVenta}
          onChange={(e) => setPrecioVenta(e.target.value)}
        />
      </div>
      <div className="flex items-end justify-end gap-2">
        <Button variant="ghost" size="sm" onClick={onDone} disabled={autorizar.isPending}>
          Cancelar
        </Button>
        <Button size="sm" onClick={enviar} disabled={autorizar.isPending}>
          {autorizar.isPending ? 'Autorizando…' : 'Autorizar'}
        </Button>
      </div>
    </div>
  );
}
