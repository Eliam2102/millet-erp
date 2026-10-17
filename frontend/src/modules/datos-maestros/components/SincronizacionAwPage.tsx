import { useState } from 'react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { esApiError } from '@/lib/api';
import {
  useConflictosAw,
  useMatrizAw,
  useResolverConflictoAw,
  TIPO_CAMBIO_AW,
  type CambioMaestroAw,
} from '@/modules/datos-maestros/api/sincronizacion-aw';

const POR_PAGINA = 10;

function mensaje(error: unknown) {
  return esApiError(error) ? (error.problem.detail ?? error.problem.title) : 'No se pudo resolver el conflicto. Reintenta.';
}

/** Pantalla «Sincronización con A+W» (ADM-06): conflictos de clientes por resolver y matriz campo → dueño. */
export function SincronizacionAwPage() {
  const [estado, setEstado] = useState<'Pendiente' | 'Resuelto'>('Pendiente');
  const [pagina, setPagina] = useState(0);
  const [confirmar, setConfirmar] = useState<CambioMaestroAw | null>(null);
  const pendientes = useConflictosAw('clientes', 'Pendiente', true);
  const resueltos = useConflictosAw('clientes', 'Resuelto', true);
  const matriz = useMatrizAw();
  const resolver = useResolverConflictoAw();
  const lista = estado === 'Pendiente' ? pendientes : resueltos;
  const filas = lista.data ?? [];
  const visibles = filas.slice(pagina * POR_PAGINA, (pagina + 1) * POR_PAGINA);

  function mantenerSeparados(c: CambioMaestroAw) {
    resolver.mutate(
      { entidad: 'clientes', id: c.id, resolucion: 'MantenerSeparados', version: c.version },
      {
        onSuccess: () => { toast.success('Conflicto resuelto'); setConfirmar(null); },
        onError: (e) => toast.error(mensaje(e)),
      },
    );
  }

  return (
    <div className="space-y-5 p-6 text-sm text-ink">
      <nav aria-label="Migas" className="text-xs text-ink-muted">Administración / Datos maestros / Sincronización con A+W</nav>
      <h1 className="text-lg font-semibold">Sincronización con A+W</h1>

      <section className="rounded-lg bg-surface-card p-4 shadow-card-flat" aria-label="Conflictos por resolver">
        <h2 className="text-lg font-semibold">Conflictos por resolver</h2>
        <Tabs value={estado} onValueChange={(v) => { setEstado(v as 'Pendiente' | 'Resuelto'); setPagina(0); setConfirmar(null); }}>
          <TabsList>
            <TabsTrigger value="Pendiente">Pendientes <span className="ml-1 text-2xs tabular-nums">{pendientes.data?.length ?? 0}</span></TabsTrigger>
            <TabsTrigger value="Resuelto">Resueltos <span className="ml-1 text-2xs tabular-nums">{resueltos.data?.length ?? 0}</span></TabsTrigger>
          </TabsList>
        </Tabs>
        {lista.isError && (
          <p role="alert" className="py-3 text-danger-fg">
            No se pudieron cargar los conflictos.{' '}
            <Button variant="secondary" size="sm" onClick={() => { void lista.refetch(); }}>Reintentar</Button>
          </p>
        )}
        {!lista.isError && !lista.isLoading && filas.length === 0 && (
          <p className="py-3 text-ink-muted">{estado === 'Pendiente' ? 'No hay conflictos por resolver.' : 'No hay conflictos resueltos.'}</p>
        )}
        {filas.length > 0 && (
          <>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Registro</TableHead><TableHead>Tipo</TableHead><TableHead>Causa</TableHead>
                  <TableHead>Dueño del campo</TableHead><TableHead>Acción</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {visibles.map((c) => (
                  <TableRow key={c.id}>
                    <TableCell className="font-mono">{c.referenciaExterna}</TableCell>
                    <TableCell><Badge variant={TIPO_CAMBIO_AW[c.tipo].variante}>{TIPO_CAMBIO_AW[c.tipo].texto}</Badge></TableCell>
                    <TableCell>{c.causa}</TableCell>
                    <TableCell>{c.dueno === 'AW' ? 'A+W' : 'ERP'}</TableCell>
                    <TableCell>
                      {c.estado === 'Pendiente' && c.tipo === 'RfcCompartido' && (
                        <Button size="sm" variant="secondary" disabled={resolver.isPending} onClick={() => setConfirmar(c)}>Mantener separados</Button>
                      )}
                      {c.estado === 'Resuelto' && <span className="text-ink-muted">Mantenidos separados</span>}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            <div className="flex items-center gap-2 py-3">
              <Button size="sm" variant="secondary" disabled={pagina === 0} onClick={() => setPagina((p) => p - 1)}>Anterior</Button>
              <span className="text-xs text-ink-muted">Página {pagina + 1} · {filas.length} conflictos</span>
              <Button size="sm" variant="secondary" disabled={(pagina + 1) * POR_PAGINA >= filas.length} onClick={() => setPagina((p) => p + 1)}>Siguiente</Button>
            </div>
          </>
        )}
        {confirmar && (
          <div role="note" className="mt-3 space-y-3 rounded-md bg-warning-note-bg p-3 text-warning-note-fg">
            <p>Los dos clientes quedan separados. Esto no fusiona ni liga identidades.</p>
            <Button disabled={resolver.isPending} onClick={() => mantenerSeparados(confirmar)}>Confirmar</Button>
            <Button variant="ghost" onClick={() => setConfirmar(null)}>Cancelar</Button>
          </div>
        )}
      </section>

      <section className="rounded-lg bg-surface-card p-4 shadow-card-flat" aria-label="Matriz campo a dueño">
        <h2 className="text-lg font-semibold">Matriz campo → dueño</h2>
        {matriz.isError && <p role="alert" className="text-danger-fg">No se pudo cargar la matriz.</p>}
        {matriz.data && (
          <>
            <p className="text-ink-muted">Versión {matriz.data.version} · propuesta, pendiente de acordar con TI</p>
            <Table>
              <TableHeader>
                <TableRow><TableHead>Entidad</TableHead><TableHead>Campo</TableHead><TableHead>Dueño</TableHead><TableHead>Si difiere</TableHead></TableRow>
              </TableHeader>
              <TableBody>
                {matriz.data.campos.map((m) => (
                  <TableRow key={`${m.entidad}-${m.campo}`}>
                    <TableCell>{m.entidad}</TableCell>
                    <TableCell>{m.campo}</TableCell>
                    <TableCell><Badge variant={m.dueno === 'AW' ? 'info' : 'neutral'}>{m.dueno === 'AW' ? 'A+W' : 'ERP'}</Badge></TableCell>
                    <TableCell>{m.siDifiere}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </>
        )}
      </section>
    </div>
  );
}
