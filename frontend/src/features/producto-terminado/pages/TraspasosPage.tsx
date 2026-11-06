import { useState } from 'react';
import { Breadcrumbs, EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Textarea } from '@/components/ui/textarea';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { useSucursales } from '@/features/catalogos/api';
import { errorUsuario as mensajeErrorAccion } from '@/lib/error-usuario';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useCrearTraspaso, useRecibirTraspaso, useTraspaso, useTraspasos } from '../api/hooks';
import type { EstadoTraspaso } from '../api/types';

// Texto además del color: el estado no depende solo de la vista.
const estadoUi: Record<EstadoTraspaso, { texto: string; tono: 'info' | 'warning' | 'success' }> = {
  EnTransito: { texto: 'En tránsito', tono: 'info' },
  ConDiferencia: { texto: 'Con diferencia', tono: 'warning' },
  Recibido: { texto: 'Recibido', tono: 'success' },
};

export function TraspasosPage() {
  const operar = useAuthStore((s) => s.permisos.includes(P.ProductoTerminadoExcepcionesGestionar));
  const lista = useTraspasos({ pageSize: 50 });
  const sucursales = useSucursales();
  const nombre = (id: string) => sucursales.data?.items.find((s) => s.id === id)?.nombre ?? 'Sin sucursal';
  const crear = useCrearTraspaso();
  const recibir = useRecibirTraspaso();

  const [origen, setOrigen] = useState<string | null>(null);
  const [destino, setDestino] = useState<string | null>(null);
  const [etiquetas, setEtiquetas] = useState('');
  const [abierto, setAbierto] = useState<string | null>(null);
  const [marcadas, setMarcadas] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const detalle = useTraspaso(abierto);

  const lineas = etiquetas.split('\n').map((l) => l.trim()).filter(Boolean);
  const puedeSalir = !!origen && !!destino && origen !== destino && lineas.length > 0 && !crear.isPending;
  const falla = (e: unknown) => setError(mensajeErrorAccion(e));

  const registrarSalida = () => {
    setError(null);
    crear
      .mutateAsync({ sucursalOrigenId: origen!, sucursalDestinoId: destino!, etiquetas: lineas })
      .then(() => setEtiquetas(''))
      .catch(falla);
  };
  const confirmarRecepcion = (id: string, piezaIds?: string[]) => {
    setError(null);
    recibir.mutateAsync({ id, piezaIds }).then(() => setMarcadas([])).catch(falla);
  };
  const pendientes = detalle.data?.piezas.filter((p) => !p.recibida) ?? [];
  const alternar = (id: string) => setMarcadas((m) => (m.includes(id) ? m.filter((x) => x !== id) : [...m, id]));

  return (
    <main className="flex h-full min-h-0 flex-col gap-5 overflow-auto text-sm">
      <Breadcrumbs items={[{ label: "Producto terminado" }, { label: 'Traspasos' }]} />
      <PageHeader
        titulo="Traspasos entre sucursales"
        descripcion="Registra la salida y recepción de piezas entre sucursales y revisa las diferencias pendientes."
      />
      {error && (
        <AvisoEnLinea tono="error" titulo="No se pudo completar la acción">
          {error}
        </AvisoEnLinea>
      )}
      {operar && (
        <section aria-label="Nueva salida" className="flex flex-col gap-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
          <div className="flex flex-wrap gap-3">
            <SucursalSelector value={origen} onChange={setOrigen} placeholder="Sucursal origen" triggerProps={{ 'aria-label': 'Sucursal origen' }} />
            <SucursalSelector value={destino} onChange={setDestino} placeholder="Sucursal destino" triggerProps={{ 'aria-label': 'Sucursal destino' }} />
          </div>
          <Textarea
            aria-label="Etiquetas de las piezas"
            placeholder="Una etiqueta por renglón"
            value={etiquetas}
            onChange={(e) => setEtiquetas(e.target.value)}
          />
          <div>
            <Button disabled={!puedeSalir} onClick={registrarSalida}>Registrar salida</Button>
          </div>
        </section>
      )}
      {lista.isError ? (
        <ErrorState title="No se pudieron cargar los traspasos" onRetry={() => void lista.refetch()} />
      ) : lista.isLoading ? (
        <TableSkeleton />
      ) : (lista.data?.items.length ?? 0) === 0 ? (
        <EmptyState title="No hay traspasos." />
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Origen</TableHead>
              <TableHead>Destino</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead>Salida</TableHead>
              <TableHead>Pendientes</TableHead>
              <TableHead>Acciones</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {lista.data!.items.map((t) => (
              <TableRow key={t.id}>
                <TableCell>{nombre(t.sucursalOrigenId)}</TableCell>
                <TableCell>{nombre(t.sucursalDestinoId)}</TableCell>
                <TableCell><Badge variant={estadoUi[t.estado].tono}>{estadoUi[t.estado].texto}</Badge></TableCell>
                <TableCell><DateTimeDisplay value={t.salidaAt} variant="datetime" /></TableCell>
                <TableCell className="tabular-nums">{t.pendientes} de {t.total}</TableCell>
                <TableCell className="flex gap-2">
                  <Button variant="outline" size="sm" onClick={() => { setAbierto(abierto === t.id ? null : t.id); setMarcadas([]); }}>
                    {abierto === t.id ? 'Ocultar piezas' : 'Ver piezas'}
                  </Button>
                  {operar && t.pendientes > 0 && (
                    <Button size="sm" disabled={recibir.isPending} onClick={() => confirmarRecepcion(t.id)}>Recibir todo</Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {abierto && detalle.data && (
        <section aria-label="Piezas del traspaso" className="flex flex-col gap-2 rounded-lg bg-surface-card p-4 shadow-card-flat">
          <h2 className="font-medium">Piezas del traspaso</h2>
          <ul className="flex flex-col gap-1">
            {detalle.data.piezas.map((p) => (
              <li key={p.piezaId} className="flex items-center gap-2">
                {operar && !p.recibida ? (
                  <Checkbox aria-label={`Recibir ${p.etiqueta}`} checked={marcadas.includes(p.piezaId)} onCheckedChange={() => alternar(p.piezaId)} />
                ) : null}
                <span>{p.etiqueta}</span>
                <span className="text-ink-muted">{p.numeroPedido}</span>
                <span>{p.recibida ? 'Recibida' : 'En tránsito'}</span>
              </li>
            ))}
          </ul>
          {operar && pendientes.length > 0 && (
            <div>
              <Button disabled={marcadas.length === 0 || recibir.isPending} onClick={() => confirmarRecepcion(detalle.data.id, marcadas)}>
                Recibir seleccionadas
              </Button>
            </div>
          )}
        </section>
      )}
    </main>
  );
}
