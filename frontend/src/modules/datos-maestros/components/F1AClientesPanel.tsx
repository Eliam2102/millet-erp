import { useRef, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { Table, TableHeader, TableRow, TableHead, TableBody, TableCell } from '@/components/ui/table';
import { CLASIFICACIONES, LIGADOS, useF1AQuery, useF1AMutation, mensajeF1A, type ClasificacionCliente, type LigadoCliente } from '../api/f1a';

export function F1AClientesPanel() {
  const clasificaciones = useF1AQuery<ClasificacionCliente[]>(CLASIFICACIONES);
  const ligados = useF1AQuery<LigadoCliente[]>(LIGADOS);
  const guardar = useF1AMutation();
  const [editar, setEditar] = useState<ClasificacionCliente | null>(null);
  const [nombre, setNombre] = useState('');
  const [descripcion, setDescripcion] = useState('');
  const [pagina, setPagina] = useState(0);
  const [revision, setRevision] = useState<{ conflicto: LigadoCliente; clienteId: string } | null>(null);
  // Bandera en ref (no en estado): el doble clic llega antes de que React re-renderice el botón deshabilitado.
  const enVuelo = useRef(false);
  function ejecutar(path: string, body: unknown, method: 'POST' | 'PUT' = 'POST') {
    if (enVuelo.current) return;
    enVuelo.current = true;
    guardar.mutate({ path, body, method }, { onSuccess: () => { toast.success('Cambio guardado'); setEditar(null); setNombre(''); setDescripcion(''); setRevision(null); }, onError: e => toast.error(mensajeF1A(e)), onSettled: () => { enVuelo.current = false; } });
  }
  return <div className="space-y-5 p-6 text-sm text-ink">
    <h1 className="text-lg font-semibold">Clasificación y ligados de clientes</h1>
    <p className="text-ink-muted">Administra las clasificaciones y revisa coincidencias por RFC antes de ligar un cliente con A+W.</p>
    {(clasificaciones.isError || ligados.isError) && <p role="alert" className="text-danger-fg">No se pudo cargar la información. <Button variant="secondary" onClick={() => { void clasificaciones.refetch(); void ligados.refetch(); }}>Reintentar</Button></p>}
    <section className="rounded-lg bg-surface-card p-4 shadow-card-flat">
      <h2 className="text-lg font-semibold">Catálogo de clasificaciones</h2>
      {clasificaciones.data?.length === 0 && <p className="text-ink-muted">No hay clasificaciones.</p>}
      <Table><TableHeader><TableRow><TableHead>Clasificación</TableHead><TableHead>Descripción</TableHead><TableHead>Estado</TableHead><TableHead>Acciones</TableHead></TableRow></TableHeader><TableBody>
        {clasificaciones.data?.slice(pagina * 10, pagina * 10 + 10).map(c => <TableRow key={c.id}><TableCell>{c.nombre}</TableCell><TableCell>{c.descripcion}</TableCell><TableCell><Badge variant={c.activa ? 'success' : 'neutral'}>{c.activa ? 'Activa' : 'Inactiva'}</Badge></TableCell><TableCell className="space-x-2"><Button size="sm" variant="secondary" onClick={() => { setEditar(c); setNombre(c.nombre); setDescripcion(c.descripcion); }}>Editar</Button><Button size="sm" variant="secondary" disabled={guardar.isPending} onClick={() => ejecutar(`${CLASIFICACIONES}/${c.id}`, { ...c, activa: !c.activa }, 'PUT')}>{c.activa ? 'Desactivar' : 'Reactivar'}</Button></TableCell></TableRow>)}
      </TableBody></Table>
      <div className="flex items-center gap-2 py-3"><Button size="sm" variant="secondary" disabled={pagina === 0} onClick={() => setPagina(p => p - 1)}>Anterior</Button><span className="text-xs text-ink-muted">Página {pagina + 1} · {clasificaciones.data?.length ?? 0} clasificaciones</span><Button size="sm" variant="secondary" disabled={(pagina + 1) * 10 >= (clasificaciones.data?.length ?? 0)} onClick={() => setPagina(p => p + 1)}>Siguiente</Button></div>
      <form className="grid gap-3" onSubmit={e => { e.preventDefault(); ejecutar(editar ? `${CLASIFICACIONES}/${editar.id}` : CLASIFICACIONES, { id: editar?.id, nombre, descripcion, activa: editar?.activa ?? true, version: editar?.version }, editar ? 'PUT' : 'POST'); }}>
        <Label htmlFor="clasificacion-nombre">{editar ? 'Editar clasificación' : 'Nueva clasificación'}</Label><Input id="clasificacion-nombre" required maxLength={100} value={nombre} onChange={e => setNombre(e.target.value)} />
        <Label htmlFor="clasificacion-descripcion">Descripción</Label><Input id="clasificacion-descripcion" maxLength={500} value={descripcion} onChange={e => setDescripcion(e.target.value)} />
        <div className="flex gap-2"><Button disabled={guardar.isPending || !nombre.trim()} title={!nombre.trim() ? 'Indica el nombre de la clasificación' : undefined}>Guardar clasificación</Button>{editar && <Button variant="ghost" type="button" onClick={() => { setEditar(null); setNombre(''); setDescripcion(''); }}>Cancelar edición</Button>}</div>
      </form>
    </section>
    <section className="rounded-lg bg-surface-card p-4 shadow-card-flat">
      <h2 className="text-lg font-semibold">Ligados pendientes de revisión</h2>
      {ligados.data?.length === 0 && <p className="text-ink-muted">No hay coincidencias pendientes.</p>}
      {ligados.data?.map(c => <div key={c.id} className="space-y-2 border-b border-line-row py-3"><p><span className="font-mono">A+W {c.referenciaExterna} · {c.rfc}</span> · {c.razonSocialAw}</p>{c.candidatos.map(m => <div key={m.id} className="flex items-center gap-3"><Link to="/admin/datos-maestros/clientes/$id" params={{ id: m.id }} className="text-brand">{m.clave} · {m.razonSocial}</Link><Button variant="secondary" size="sm" onClick={() => setRevision({ conflicto: c, clienteId: m.id })}>Revisar ligado</Button></div>)}</div>)}
      {revision && <div role="note" className="mt-3 space-y-3 rounded-md bg-warning-note-bg p-3 text-warning-note-fg"><p>Al aprobar, la clave A+W {revision.conflicto.referenciaExterna} se ligará al cliente seleccionado. Se conservarán sus datos fiscales, clasificación y saldo.</p><Button disabled={guardar.isPending} onClick={() => ejecutar(`${LIGADOS}/${revision.conflicto.id}/revisar`, { id: revision.conflicto.id, clienteId: revision.clienteId, version: revision.conflicto.version })}>Aprobar ligado revisado</Button><Button variant="ghost" onClick={() => setRevision(null)}>Cancelar</Button></div>}
    </section>
  </div>;
}
