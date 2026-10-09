import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription } from '@/components/ui/sheet';
import { Select, SelectTrigger, SelectValue, SelectContent, SelectItem } from '@/components/ui/select';
import { Badge } from '@/components/ui/badge';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { useConceptos, useGuardarConcepto } from '../api/useTesoreria';
import { CLASIFICACION_FLUJO_LABELS, type ClasificacionFlujo, type ConceptoResponse } from '../api/types';

export function CatalogoConceptos() {
  const [open, setOpen] = useState(false);
  const query = useConceptos(true);
  const [editando, setEditando] = useState<ConceptoResponse | null>(null);
  const [nombre, setNombre] = useState('');
  const [clasificacion, setClasificacion] = useState<ClasificacionFlujo>(1);
  const mutation = useGuardarConcepto();
  const keyFor = useBodyScopedIdempotencyKey();
  const limpiar = () => { setEditando(null); setNombre(''); setClasificacion(1); };
  function guardar(concepto?: ConceptoResponse) {
    const command = concepto
      ? { id: concepto.id, nombre: concepto.nombre, clasificacionFlujo: concepto.clasificacionFlujo, activo: !concepto.activo, version: concepto.version }
      : { id: editando?.id, nombre: nombre.trim(), clasificacionFlujo: clasificacion, activo: editando?.activo ?? true, version: editando?.version };
    mutation.mutate({ ...command, idempotencyKey: keyFor(command) }, {
      onSuccess: () => { toast.success('Catálogo actualizado'); limpiar(); },
      onError: e => toast.error(esApiError(e) ? e.problem.detail ?? e.problem.title : 'No se pudo guardar el concepto'),
    });
  }
  return <>
    <Button variant="outline" onClick={() => setOpen(true)}>Administrar conceptos</Button>
    <Sheet open={open} onOpenChange={setOpen}><SheetContent className="overflow-y-auto">
      <SheetHeader><SheetTitle>Conceptos de flujo de efectivo</SheetTitle>
        <SheetDescription>Subconceptos agrupados en operación, inversión y financiamiento. Los ejemplos requieren validación de Millet.</SheetDescription></SheetHeader>
      <div className="space-y-4 p-4">
        {query.isError && <p role="alert" className="text-danger-fg">No se pudo cargar el catálogo.</p>}
        <div className="space-y-2 rounded-lg bg-surface-subtle p-3">
          <Label htmlFor="concepto-nombre">{editando ? 'Editar subconcepto' : 'Nuevo subconcepto'}</Label>
          <Input id="concepto-nombre" value={nombre} maxLength={120} onChange={e => setNombre(e.target.value)} />
          <Label htmlFor="concepto-clasificacion">Clasificación</Label>
          <Select value={String(clasificacion)} onValueChange={v => setClasificacion(Number(v) as ClasificacionFlujo)}>
            <SelectTrigger id="concepto-clasificacion"><SelectValue /></SelectTrigger>
            <SelectContent>{([1, 2, 3] as const).map(c => <SelectItem key={c} value={String(c)}>{CLASIFICACION_FLUJO_LABELS[c]}</SelectItem>)}</SelectContent>
          </Select>
          <div className="flex gap-2"><Button disabled={!nombre.trim() || mutation.isPending} onClick={() => guardar()}>Guardar concepto</Button>
            {editando && <Button variant="ghost" onClick={limpiar}>Cancelar edición</Button>}</div>
        </div>
        <ul className="space-y-2">{query.data?.map(c => <li key={c.id} className="space-y-2 rounded-lg p-3 shadow-card-flat">
          <p className="text-sm font-medium">{c.nombre}</p>
          <p className="text-xs text-ink-muted">{CLASIFICACION_FLUJO_LABELS[c.clasificacionFlujo]}{c.esEjemplo ? ' · Ejemplo por validar con Millet' : ''}</p>
          <Badge variant={c.activo ? 'success' : 'neutral'}>{c.activo ? 'Activo' : 'Inactivo'}</Badge>
          <div className="flex gap-2"><Button size="sm" variant="outline" onClick={() => { setEditando(c); setNombre(c.nombre); setClasificacion(c.clasificacionFlujo); }}>Editar</Button>
            <Button size="sm" variant="ghost" disabled={mutation.isPending} onClick={() => guardar(c)}>{c.activo ? 'Dar de baja' : 'Reactivar'}</Button></div>
        </li>)}</ul>
      </div>
    </SheetContent></Sheet>
  </>;
}
