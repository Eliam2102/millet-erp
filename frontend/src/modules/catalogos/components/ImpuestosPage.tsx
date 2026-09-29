import { useState, type FormEvent } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { hoyLocalISO } from '@/lib/datetime';
import {
  useActualizarImpuestoReferencia,
  useCrearImpuestoReferencia,
  useImpuestosReferencia,
} from '@/modules/catalogos/api';
import type {
  CrearImpuestoReferenciaPayload,
  ImpuestoReferenciaResponse,
} from '@/modules/catalogos/api/types';

const hoy = hoyLocalISO();
const initial: CrearImpuestoReferenciaPayload = {
  clave: '', nombre: '', tipo: 'Traslado', factor: 'Tasa', tasa: 0,
  vigenteDesde: hoy, vigenteHasta: null, activo: true, fuente: 'Propuesta VILO',
};

/** Mantenimiento de referencias. No modifica el motor de cálculo fiscal. */
export function ImpuestosPage() {
  const canManage = useHasPermission(PermisosCanonicos.CompartidoCatalogosAdministrar);
  const [fecha, setFecha] = useState(hoy);
  const [historico, setHistorico] = useState(true);
  const [editando, setEditando] = useState<ImpuestoReferenciaResponse | null>(null);
  const [nuevo, setNuevo] = useState(false);
  const [draft, setDraft] = useState<CrearImpuestoReferenciaPayload>(initial);
  const query = useImpuestosReferencia(fecha, historico);
  const crear = useCrearImpuestoReferencia();
  const actualizar = useActualizarImpuestoReferencia();

  function abrirNuevo() {
    setEditando(null);
    setDraft({ ...initial, vigenteDesde: fecha || hoy });
    setNuevo(true);
  }

  function abrirEdicion(item: ImpuestoReferenciaResponse) {
    setNuevo(false);
    setEditando(item);
    setDraft({ clave: item.clave, nombre: item.nombre, tipo: item.tipo,
      factor: item.factor, tasa: item.tasa, vigenteDesde: item.vigenteDesde,
      vigenteHasta: item.vigenteHasta, activo: item.activo, fuente: item.fuente });
  }

  async function guardar(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    try {
      if (editando) {
        await actualizar.mutateAsync({ id: editando.id, payload: {
          nombre: draft.nombre, vigenteHasta: draft.vigenteHasta,
          activo: draft.activo, fuente: draft.fuente,
        } });
        toast.success('Referencia actualizada');
      } else {
        await crear.mutateAsync(draft);
        toast.success('Referencia creada');
      }
      setEditando(null);
      setNuevo(false);
    } catch (error) {
      toast.error(error instanceof Error ? error.message : 'No se pudo guardar la referencia.');
    }
  }

  return (
    <div className="space-y-5 p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Impuestos</h1>
          <p className="text-sm text-muted-foreground">Referencias fiscales compartidas. Las reglas y tasas de Millet requieren validación antes de su uso operativo.</p>
        </div>
        {canManage && <Button size="sm" onClick={abrirNuevo}>Nueva referencia</Button>}
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <label className="space-y-1 text-sm">Fecha de consulta
          <Input type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} className="w-44" />
        </label>
        <label className="flex items-center gap-2 pb-2 text-sm">
          <input type="checkbox" checked={historico} onChange={(e) => setHistorico(e.target.checked)} />
          Incluir historial e inactivos
        </label>
      </div>

      {query.isLoading && <p role="status">Cargando referencias…</p>}
      {query.isError && <div role="alert" className="text-sm text-destructive">No se pudo cargar el catálogo. <Button variant="outline" size="sm" onClick={() => query.refetch()}>Reintentar</Button></div>}
      {query.data && query.data.length === 0 && <p className="rounded-md border p-6 text-sm text-muted-foreground">Todavía no hay referencias para esta consulta.</p>}
      {query.data && query.data.length > 0 && (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-left text-sm">
            <thead className="bg-muted/50"><tr>
              <th className="p-3">Clave</th><th className="p-3">Nombre</th><th className="p-3">Tipo</th>
              <th className="p-3">Factor / valor</th><th className="p-3">Vigencia</th>
              <th className="p-3">Fuente</th><th className="p-3">Estado</th>
              {canManage && <th className="p-3">Acción</th>}
            </tr></thead>
            <tbody>{query.data.map((item) => (
              <tr key={item.id} className="border-t">
                <td className="p-3 font-mono">{item.clave}</td><td className="p-3">{item.nombre}</td>
                <td className="p-3">{item.tipo}</td><td className="p-3">{item.factor} {item.tasa}</td>
                <td className="p-3 whitespace-nowrap">{item.vigenteDesde} → {item.vigenteHasta ?? 'Abierta'}</td>
                <td className="p-3">{item.fuente}</td><td className="p-3">{item.activo ? 'Activo' : 'Inactivo'}</td>
                {canManage && <td className="p-3"><Button variant="outline" size="sm" onClick={() => abrirEdicion(item)}>Editar</Button></td>}
              </tr>
            ))}</tbody>
          </table>
        </div>
      )}

      {canManage && (nuevo || editando) && (
        <form onSubmit={guardar} className="space-y-4 rounded-md border bg-muted/20 p-4" aria-label={editando ? 'Editar referencia' : 'Nueva referencia'}>
          <h2 className="font-medium">{editando ? `Editar ${editando.clave}` : 'Nueva referencia de impuesto'}</h2>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <label className="space-y-1 text-sm">Clave
              <Input required maxLength={20} value={draft.clave} disabled={!!editando}
                onChange={(e) => setDraft({ ...draft, clave: e.target.value })} />
            </label>
            <label className="space-y-1 text-sm">Nombre
              <Input required maxLength={120} value={draft.nombre}
                onChange={(e) => setDraft({ ...draft, nombre: e.target.value })} />
            </label>
            <label className="space-y-1 text-sm">Tipo
              <select className="h-9 w-full rounded-md border bg-background px-3" value={draft.tipo} disabled={!!editando}
                onChange={(e) => setDraft({ ...draft, tipo: e.target.value as typeof draft.tipo })}>
                <option value="Traslado">Traslado</option><option value="Retencion">Retención</option>
              </select>
            </label>
            <label className="space-y-1 text-sm">Factor
              <select className="h-9 w-full rounded-md border bg-background px-3" value={draft.factor} disabled={!!editando}
                onChange={(e) => setDraft({ ...draft, factor: e.target.value as typeof draft.factor,
                  tasa: e.target.value === 'Exento' ? 0 : draft.tasa })}>
                <option value="Tasa">Tasa</option><option value="Cuota">Cuota</option><option value="Exento">Exento</option>
              </select>
            </label>
            <label className="space-y-1 text-sm">Valor
              <Input type="number" min={0} max={draft.factor === 'Tasa' ? 1 : undefined} step="0.000001"
                value={draft.tasa} disabled={!!editando || draft.factor === 'Exento'}
                onChange={(e) => setDraft({ ...draft, tasa: Number(e.target.value) })} />
            </label>
            <label className="space-y-1 text-sm">Vigente desde
              <Input type="date" required value={draft.vigenteDesde} disabled={!!editando}
                onChange={(e) => setDraft({ ...draft, vigenteDesde: e.target.value })} />
            </label>
            <label className="space-y-1 text-sm">Vigente hasta
              <Input type="date" min={draft.vigenteDesde} value={draft.vigenteHasta ?? ''}
                onChange={(e) => setDraft({ ...draft, vigenteHasta: e.target.value || null })} />
            </label>
            <label className="space-y-1 text-sm">Fuente
              <Input required maxLength={120} value={draft.fuente}
                onChange={(e) => setDraft({ ...draft, fuente: e.target.value })} />
            </label>
          </div>
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={draft.activo} onChange={(e) => setDraft({ ...draft, activo: e.target.checked })} />
            Activo para operaciones nuevas durante su vigencia
          </label>
          <div className="flex gap-2">
            <Button type="submit" disabled={crear.isPending || actualizar.isPending}>Guardar</Button>
            <Button type="button" variant="outline" onClick={() => { setNuevo(false); setEditando(null); }}>Cancelar</Button>
          </div>
        </form>
      )}
    </div>
  );
}
