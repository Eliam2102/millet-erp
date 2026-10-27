import {
  BarraFiltros,
  EmptyState,
  ErrorState,
  PaginacionTabla,
  TableSkeleton,
  TabsEstado,
} from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { PageHeader } from '@/components/erp/PageHeader';
import { Button } from '@/components/ui/button';
import { motivosDisposicion } from '@/features/activos-fijos/api/motivos-disposicion';
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { EstadoAutorizacionActivo } from '@/features/facturacion/api/types';
import {
  useAutorizacionesActivo,
  useAutorizarVentaActivo,
} from '@/features/facturacion/api/useActivos';
import { EstadoFacturacionBadge } from '@/features/facturacion/components/EstadoFacturacionBadge';
import type { ActivosSearch } from '@/features/facturacion/lib/activos-search-schema';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';
import { estadoUi } from '../lib/estados-ui';
import { usePaginacionLocal } from '../lib/use-paginacion-local';

const FROM = '/_app/facturacion/activos/' as const;

/**
 * <c>Autorizaciones de venta de activo fijo</c> (FE-F9). El Director de Administración y Finanzas
 * autoriza la venta (calcula valor neto + utilidad/pérdida); el
 * <c>autorizacionId</c> resultante es obligatorio al emitir la factura de
 * venta de activo (la emisión lo exige; el backend rechaza sin él).
 */
export function Activos() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeAutorizar = useHasPermission(PermisosCanonicos.FacturacionActivosAutorizar);
  const [formAbierto, setFormAbierto] = useState(false);
  const query = useAutorizacionesActivo(search.estado);
  const [busqueda, setBusqueda] = useState('');
  const items = (query.data ?? []).filter((a) =>
    (a.activoRef + ' ' + a.descripcion).toLowerCase().includes(busqueda.trim().toLowerCase()),
  );

  function actualizar(parcial: Partial<ActivosSearch>) {
    navigate({ to: '/facturacion/activos', search: { ...search, ...parcial } });
  }

  const paginacion = usePaginacionLocal(items, JSON.stringify(search) + busqueda);

  return (
    <div className="space-y-5">
      <PageHeader
        titulo={<>Autorizaciones de venta de activos</>}
        descripcion={
          <>
            El Director de Administración y Finanzas autoriza la venta antes de timbrar. La emisión
            de venta de activo fijo exige una autorización al mismo precio neto MXN, sin
            vencimiento.
          </>
        }
        acciones={
          <>
            {puedeAutorizar && (
              <Button onClick={() => setFormAbierto((v) => !v)}>
                <Plus className="mr-2 h-4 w-4" />
                Autorizar venta
              </Button>
            )}
          </>
        }
      />

      {puedeAutorizar && (
        <Dialog open={formAbierto} onOpenChange={setFormAbierto}>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>Autorizar venta de activo</DialogTitle>
            </DialogHeader>
            <AutorizarForm onDone={() => setFormAbierto(false)} />
          </DialogContent>
        </Dialog>
      )}

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={Object.entries(EstadoAutorizacionActivo).map(([nombre, valor]) => ({
              valor,
              label: estadoUi('activo', nombre).texto,
            }))}
            valor={search.estado}
            onChange={(estado) => actualizar({ estado: estado as ActivosSearch['estado'] })}
            conteos={undefined}
          />
        }
        filtros={
          <BarraFiltros
            busqueda={
              <Input
                aria-label="Referencia o descripción del activo"
                placeholder="Referencia o descripción del activo"
                value={busqueda}
                onChange={(e) => setBusqueda(e.target.value)}
              />
            }
            filtros={
              <>
                {search.estado != null && (
                  <Button variant="ghost" onClick={() => actualizar({ estado: undefined })}>
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
          <div className="overflow-x-auto rounded-md border border-line">
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50">
                <TableRow>
                  <TableHead className="px-3 py-2 text-left">Activo</TableHead>
                  <TableHead className="px-3 py-2 text-left">Descripción</TableHead>
                  <TableHead className="px-3 py-2 text-right tabular-nums">Precio venta</TableHead>
                  <TableHead className="px-3 py-2 text-right tabular-nums">Valor neto</TableHead>
                  <TableHead className="px-3 py-2 text-right tabular-nums">
                    Utilidad/Pérdida
                  </TableHead>
                  <TableHead className="px-3 py-2 text-left">Motivo</TableHead>
                  <TableHead className="px-3 py-2 text-left">Solicitado por</TableHead>
                  <TableHead className="px-3 py-2 text-left">Soporte</TableHead>
                  <TableHead className="px-3 py-2 text-left">Estado</TableHead>
                  <TableHead className="px-3 py-2 text-left">ID autorización</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {paginacion.visibles.map((a) => (
                  <TableRow key={a.id} className="border-t hover:bg-surface-muted/30 border-line">
                    <TableCell className="px-3 py-2 font-mono text-xs">{a.activoRef}</TableCell>
                    <TableCell className="px-3 py-2">{a.descripcion}</TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      {a.precioVenta.toFixed(2)}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      {a.valorNetoEnLibros.toFixed(2)}
                    </TableCell>
                    <TableCell
                      className={
                        'px-3 py-2 text-right font-mono ' +
                        (a.utilidadOPerdida >= 0 ? 'text-success-fg' : 'text-danger-fg')
                      }
                    >
                      {a.utilidadOPerdida.toFixed(2)}
                    </TableCell>
                    <TableCell className="px-3 py-2">
                      {motivosDisposicion.find((m) => m.valor === a.motivo)?.etiqueta}
                    </TableCell>
                    <TableCell className="px-3 py-2">{a.solicitadoPor}</TableCell>
                    <TableCell className="px-3 py-2">
                      {a.evidencia || 'Sin soporte (registro anterior)'}
                    </TableCell>
                    <TableCell className="px-3 py-2">
                      <EstadoFacturacionBadge entidad="activo" estado={a.estado} />
                    </TableCell>
                    <TableCell className="px-3 py-2 font-mono text-xs text-ink-muted">
                      {a.id}
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

function AutorizarForm({ onDone }: { onDone: () => void }) {
  const [activoRef, setActivoRef] = useState('');
  const [precioVenta, setPrecioVenta] = useState('');
  const [motivo, setMotivo] = useState('');
  const [solicitadoPor, setSolicitadoPor] = useState('');
  const [evidencia, setEvidencia] = useState('');
  const completo =
    activoRef.trim() !== '' &&
    Number(precioVenta) > 0 &&
    motivo !== '' &&
    solicitadoPor.trim() !== '' &&
    solicitadoPor.length <= 200 &&
    evidencia.trim() !== '';
  const idempotencyKey = useFormIdempotencyKey();
  const autorizar = useAutorizarVentaActivo();

  function enviar() {
    const precio = Number(precioVenta);
    if (!completo) {
      toast.error('Capture activo, precio, motivo, área solicitante y referencia del soporte.');
      return;
    }
    autorizar.mutate(
      {
        command: {
          activoRef: activoRef.trim(),
          precioVenta: precio,
          motivo: Number(motivo),
          solicitadoPor: solicitadoPor.trim(),
          evidencia: evidencia.trim(),
        },
        idempotencyKey,
      },
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
          toast.error(esApiError(error) ? error.problem.title : 'No se pudo autorizar.'),
      },
    );
  }

  return (
    <div className="grid grid-cols-1 gap-2 rounded-md border border-dashed p-3 sm:grid-cols-4 border-line">
      <p className="sm:col-span-4 text-sm text-ink-muted">
        Autoriza el Director de Administración y Finanzas, incluido el valor de venta. La
        autorización no vence.
      </p>
      <label className="sm:col-span-4">
        Motivo *
        <select
          className="block h-9 w-full rounded-md border border-line-control bg-surface-card px-3"
          value={motivo}
          onChange={(e) => setMotivo(e.target.value)}
          required
        >
          <option value="">Seleccione motivo</option>
          {motivosDisposicion.map((m) => (
            <option key={m.valor} value={m.valor}>
              {m.etiqueta}
            </option>
          ))}
        </select>
      </label>
      <label className="sm:col-span-4">
        Solicitado por (área usuaria) *
        <Input
          required
          maxLength={200}
          value={solicitadoPor}
          onChange={(e) => setSolicitadoPor(e.target.value)}
        />
      </label>
      <label className="sm:col-span-4">
        Referencia del soporte *
        <Input
          required
          value={evidencia}
          onChange={(e) => setEvidencia(e.target.value)}
          placeholder="Factura de venta, reporte o dictamen de siniestro, otro"
        />
      </label>
      <div className="sm:col-span-2">
        <Label htmlFor="venta-activo-ref" className="text-xs">
          Referencia del activo *
        </Label>
        <Input
          id="venta-activo-ref"
          placeholder="AF-000123"
          required
          maxLength={60}
          value={activoRef}
          onChange={(e) => setActivoRef(e.target.value)}
        />
      </div>
      <div>
        <Label htmlFor="venta-activo-precio" className="text-xs">
          Precio de venta *
        </Label>
        <Input
          id="venta-activo-precio"
          type="number"
          required
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
        <Button
          size="sm"
          onClick={enviar}
          disabled={autorizar.isPending || !completo}
          title={!completo ? 'Complete los campos obligatorios' : undefined}
        >
          {autorizar.isPending ? 'Autorizando…' : 'Autorizar'}
        </Button>
      </div>
    </div>
  );
}
