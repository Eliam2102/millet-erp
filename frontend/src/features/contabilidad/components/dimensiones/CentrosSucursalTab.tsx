import { useState } from 'react';
import { Search } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { useAsignarSucursales, useCentrosSucursal, useSucursalesContables } from '../../api/dimensiones';
import type { CentroSucursales } from '../../api/dimensiones-types';
import { ETIQUETA_DIMENSION, mensajeError } from '../../lib/dimensiones';
import { SELECT_CLASS } from '../../lib/estilos';

/**
 * Sucursales en las que se puede usar cada CeCo (Dim2) en movimientos contables; sus centros de Dim3 las
 * heredan. Mientras Millet no entregue la relación real, se asignan datos de prueba.
 */
export function CentrosSucursalTab({ puedeAdministrar }: { puedeAdministrar: boolean }) {
  const [busqueda, setBusqueda] = useState('');
  const [sucursalId, setSucursalId] = useState('');
  const [soloSin, setSoloSin] = useState(false);
  const [editando, setEditando] = useState<CentroSucursales | null>(null);
  const q = useDebouncedValue(busqueda.trim(), 200);
  const sucursales = useSucursalesContables();
  const centros = useCentrosSucursal(q, sucursalId, soloSin);

  return (
    <div className="flex flex-col gap-4">
      <p role="note" className="rounded-md bg-warning-note-bg px-3 py-2.5 text-sm text-warning-note-fg">
        Un centro sin sucursales no se puede usar en movimientos contables. La relación definitiva la entrega Contabilidad de Millet;
        mientras tanto, las asignaciones sirven para pruebas.
      </p>
      <div className="flex flex-wrap items-end gap-3" data-print="hidden">
        <div className="relative">
          <Search className="pointer-events-none absolute left-2.5 top-2.5 size-4 text-ink-muted" aria-hidden="true" />
          <Input aria-label="Buscar centro" placeholder="Clave o nombre del CeCo…" className="w-72 pl-8" value={busqueda} onChange={(e) => setBusqueda(e.target.value)} />
        </div>
        <select aria-label="Filtrar por sucursal" className={SELECT_CLASS} value={sucursalId} onChange={(e) => setSucursalId(e.target.value)}>
          <option value="">Todas las sucursales</option>
          {(sucursales.data ?? []).map((s) => <option key={s.id} value={s.id}>{s.clave} — {s.nombre}</option>)}
        </select>
        <div className="flex h-ctl-xl items-center gap-2">
          <Checkbox id="f-sin-sucursal" checked={soloSin} onCheckedChange={(v) => setSoloSin(v === true)} />
          <Label htmlFor="f-sin-sucursal" className="font-normal">Solo centros sin sucursal</Label>
        </div>
      </div>

      <div className="overflow-hidden rounded-lg bg-surface-card shadow-card-flat">
        {centros.isLoading ? (
          <div className="space-y-2 p-4" aria-label="Cargando centros"><Skeleton className="h-8 w-full" /><Skeleton className="h-8 w-full" /></div>
        ) : centros.isError ? (
          <div role="alert" className="flex items-center gap-2 p-4 text-sm text-danger-fg">
            No se pudieron cargar los centros.
            <Button variant="ghost" size="sm" onClick={() => void centros.refetch()}>Reintentar</Button>
          </div>
        ) : centros.data!.length === 0 ? (
          <p className="p-6 text-sm text-ink-muted">Ningún centro de costo coincide con los filtros.</p>
        ) : (
          <div className="max-h-[60vh] overflow-auto">
            <table className="w-full min-w-[720px] text-sm">
              <thead className="sticky top-0 border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
                <tr>
                  <th className="px-3 py-2">CeCo · {ETIQUETA_DIMENSION.Dim2}</th><th className="px-3 py-2">{ETIQUETA_DIMENSION.Dim1}</th><th className="px-3 py-2">Sucursales</th>
                  {puedeAdministrar && <th className="px-3 py-2"><span className="sr-only">Acciones</span></th>}
                </tr>
              </thead>
              <tbody>
                {centros.data!.map((c) => (
                  <tr key={c.dim2Id} className="border-b border-line-row">
                    <td className="px-3 py-2">
                      <span className="font-mono text-xs">{c.clave}</span> {c.nombre}
                      {!c.activo && <Badge variant="neutral" className="ml-2">Dado de baja</Badge>}
                    </td>
                    <td className="px-3 py-2 font-mono text-xs">{c.dim1Clave}</td>
                    <td className="px-3 py-2">
                      {c.sucursales.length === 0 ? (
                        <Badge variant="warning">Sin sucursal</Badge>
                      ) : (
                        <span className="flex flex-wrap gap-1">{c.sucursales.map((s) => <Badge key={s.id} variant="neutral">{s.nombre}</Badge>)}</span>
                      )}
                    </td>
                    {puedeAdministrar && (
                      <td className="px-3 py-2 text-right">
                        <Button variant="outline" size="sm" onClick={() => setEditando(c)} aria-label={`Asignar sucursales a ${c.clave}`}>Asignar sucursales</Button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      {puedeAdministrar && editando && (
        <AsignarSucursalesDialog centro={editando} sucursales={sucursales.data ?? []} onClose={() => setEditando(null)} />
      )}
    </div>
  );
}

function AsignarSucursalesDialog({
  centro, sucursales, onClose,
}: { centro: CentroSucursales; sucursales: { id: string; clave: string; nombre: string; activa: boolean }[]; onClose: () => void }) {
  const asignar = useAsignarSucursales();
  const [elegidas, setElegidas] = useState<string[]>(centro.sucursales.map((s) => s.id));
  const [error, setError] = useState('');

  function alternar(id: string, on: boolean) {
    setElegidas((x) => (on ? [...x, id] : x.filter((y) => y !== id)));
  }

  return (
    <Dialog open onOpenChange={(o) => { if (!o) onClose(); }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Sucursales de {centro.clave}</DialogTitle>
          <DialogDescription>{centro.nombre}. Sus centros de {ETIQUETA_DIMENSION.Dim3.toLowerCase()} se podrán usar en las mismas sucursales.</DialogDescription>
        </DialogHeader>
        <fieldset className="flex max-h-72 flex-col gap-2 overflow-y-auto">
          <legend className="sr-only">Sucursales</legend>
          {sucursales.map((s) => (
            <div key={s.id} className="flex items-center gap-2">
              <Checkbox id={`suc-${s.id}`} checked={elegidas.includes(s.id)} onCheckedChange={(v) => alternar(s.id, v === true)} />
              <Label htmlFor={`suc-${s.id}`} className="font-normal">
                <span className="font-mono text-xs">{s.clave}</span> {s.nombre}{!s.activa && ' (inactiva)'}
              </Label>
            </div>
          ))}
        </fieldset>
        {error && <p role="alert" className="text-sm text-danger-fg">{error}</p>}
        <DialogFooter>
          <Button variant="ghost" onClick={onClose}>Cancelar</Button>
          <Button
            disabled={asignar.isPending}
            onClick={() => asignar.mutate({ dim2Id: centro.dim2Id, sucursalIds: elegidas }, { onSuccess: onClose, onError: (e) => setError(mensajeError(e)) })}
          >
            {asignar.isPending ? 'Guardando…' : 'Guardar sucursales'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
