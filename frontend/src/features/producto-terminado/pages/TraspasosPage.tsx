import { useState } from 'react';
import { Breadcrumbs, EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { OperadorPicker } from '@/features/facturacion/components/OperadorPicker';
import { VehiculoPicker } from '@/features/facturacion/components/VehiculoPicker';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { useSucursales } from '@/features/catalogos/api';
import { errorUsuario as mensajeErrorAccion } from '@/lib/error-usuario';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useAutorizarTraspaso, useCrearTraspaso, useRecibirTraspaso, useTraspasos } from '../api/hooks';
import type { EstadoTraspaso } from '../api/types';
import { TraspasoDetallePanel } from './TraspasoDetallePanel';

// Texto además del color: el estado no depende solo de la vista.
const estadoUi: Record<EstadoTraspaso, { texto: string; tono: 'info' | 'warning' | 'success' | 'neutral' }> = {
  Preparado: { texto: 'Por autorizar', tono: 'neutral' },
  EnTransito: { texto: 'En tránsito', tono: 'info' },
  ConDiferencia: { texto: 'Con diferencia', tono: 'warning' },
  Recibido: { texto: 'Recibido', tono: 'success' },
  Cerrado: { texto: 'Cerrado', tono: 'success' },
};

export function TraspasosPage() {
  const operar = useAuthStore((s) => s.permisos.includes(P.ProductoTerminadoExcepcionesGestionar));
  const usuarioId = useAuthStore((s) => s.user?.id);
  const sucursales = useSucursales();
  const nombre = (id: string) => sucursales.data?.items.find((s) => s.id === id)?.nombre ?? 'Sin sucursal';
  const crear = useCrearTraspaso();
  const autorizar = useAutorizarTraspaso();
  const recibir = useRecibirTraspaso();

  const [vista, setVista] = useState<'todos' | 'porRecibir'>('todos');
  const [destinoVista, setDestinoVista] = useState<string | null>(null);
  const [origen, setOrigen] = useState<string | null>(null);
  const [destino, setDestino] = useState<string | null>(null);
  const [etiquetas, setEtiquetas] = useState('');
  const [foranea, setForanea] = useState(false);
  const [destinoTexto, setDestinoTexto] = useState('');
  const [vehiculo, setVehiculo] = useState<string | null>(null);
  const [operador, setOperador] = useState<string | null>(null);
  const [distancia, setDistancia] = useState('');
  const [peso, setPeso] = useState('');
  const [cpOrigen, setCpOrigen] = useState('');
  const [estadoOrigen, setEstadoOrigen] = useState('');
  const [cpDestino, setCpDestino] = useState('');
  const [estadoDestino, setEstadoDestino] = useState('');
  const [abierto, setAbierto] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  // «Por recibir» = lo que salió hacia la sucursal elegida y aún no se cierra.
  const lista = useTraspasos(vista === 'porRecibir' ? { estado: 'EnTransito,ConDiferencia', destinoId: destinoVista ?? undefined, pageSize: 50 } : { pageSize: 50 });

  const lineas = etiquetas.split('\n').map((l) => l.trim()).filter(Boolean);
  const datosCartaPorte = !foranea || (destinoTexto.trim() !== '' && !!vehiculo && !!operador && Number(distancia) > 0 && Number(peso) > 0 && /^\d{5}$/.test(cpOrigen) && /^[A-Za-z]{3}$/.test(estadoOrigen) && /^\d{5}$/.test(cpDestino) && /^[A-Za-z]{3}$/.test(estadoDestino));
  const puedePreparar = !!origen && !!destino && origen !== destino && lineas.length > 0 && datosCartaPorte && !crear.isPending;
  const falla = (e: unknown) => setError(mensajeErrorAccion(e));

  const prepararTraspaso = () => {
    setError(null);
    crear
      .mutateAsync({
        sucursalOrigenId: origen!,
        sucursalDestinoId: destino!,
        etiquetas: lineas,
        ...(foranea && {
          foranea: true,
          transporte: {
            destino: destinoTexto.trim(),
            vehiculoId: vehiculo,
            operadorId: operador,
            distanciaKm: Number(distancia),
            pesoKg: Number(peso),
            origenCodigoPostal: cpOrigen,
            origenEstado: estadoOrigen,
            destinoCodigoPostal: cpDestino,
            destinoEstado: estadoDestino,
          },
        }),
      })
      .then((r) => { setEtiquetas(''); setAbierto(r.data.id); })
      .catch(falla);
  };
  const confirmarAutorizacion = (id: string) => {
    setError(null);
    autorizar.mutateAsync(id).catch(falla);
  };
  const confirmarRecepcion = (id: string) => {
    setError(null);
    recibir.mutateAsync({ id }).catch(falla);
  };

  return (
    <main className="flex h-full min-h-0 flex-col gap-5 overflow-auto text-sm">
      <div data-print="hidden" className="flex flex-col gap-5">
        <Breadcrumbs items={[{ label: "Producto terminado" }, { label: 'Traspasos' }]} />
        <PageHeader
          titulo="Traspasos entre sucursales"
          descripcion="Prepara el traspaso, que lo autorice otra persona, recíbelo en destino y aclara las diferencias hasta cerrarlo."
        />
        {error && (
          <AvisoEnLinea tono="error" titulo="No se pudo completar la acción">
            {error}
          </AvisoEnLinea>
        )}
        {operar && (
          <section aria-label="Nuevo traspaso" className="flex flex-col gap-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
            <div className="flex flex-wrap items-center gap-3">
              <SucursalSelector value={origen} onChange={setOrigen} placeholder="Sucursal origen" triggerProps={{ 'aria-label': 'Sucursal origen' }} />
              <SucursalSelector value={destino} onChange={setDestino} placeholder="Sucursal destino" triggerProps={{ 'aria-label': 'Sucursal destino' }} />
              <label className="flex items-center gap-2">
                <Checkbox aria-label="Traspaso foráneo o por carretera federal" checked={foranea} onCheckedChange={(v) => setForanea(v === true)} />
                Foráneo o carretera federal (lleva Carta Porte)
              </label>
            </div>
            {foranea && (
              <div className="flex flex-wrap items-end gap-3">
                <Input className="w-72" aria-label="Destino del traslado" placeholder="Destino del traslado" value={destinoTexto} onChange={(e) => setDestinoTexto(e.target.value)} />
                <VehiculoPicker value={vehiculo} onChange={setVehiculo} />
                <OperadorPicker value={operador} onChange={setOperador} />
                <label className="flex flex-col gap-1 text-xs text-ink-muted">
                  Distancia (km)
                  <Input className="w-32" placeholder="320" inputMode="decimal" value={distancia} onChange={(e) => setDistancia(e.target.value)} />
                </label>
                <label className="flex flex-col gap-1 text-xs text-ink-muted">
                  Peso (kg)
                  <Input className="w-32" placeholder="1850" inputMode="decimal" value={peso} onChange={(e) => setPeso(e.target.value)} />
                </label>
                <label className="flex flex-col gap-1 text-xs text-ink-muted">
                  CP de origen
                  <Input className="w-32" placeholder="97302" maxLength={5} value={cpOrigen} onChange={(e) => setCpOrigen(e.target.value)} />
                </label>
                <label className="flex flex-col gap-1 text-xs text-ink-muted">
                  Estado de origen (clave SAT)
                  <Input className="w-44" placeholder="YUC" maxLength={3} value={estadoOrigen} onChange={(e) => setEstadoOrigen(e.target.value)} />
                </label>
                <label className="flex flex-col gap-1 text-xs text-ink-muted">
                  CP de destino
                  <Input className="w-32" placeholder="77500" maxLength={5} value={cpDestino} onChange={(e) => setCpDestino(e.target.value)} />
                </label>
                <label className="flex flex-col gap-1 text-xs text-ink-muted">
                  Estado de destino (clave SAT)
                  <Input className="w-44" placeholder="ROO" maxLength={3} value={estadoDestino} onChange={(e) => setEstadoDestino(e.target.value)} />
                </label>
              </div>
            )}
            <Textarea
              aria-label="Etiquetas de las piezas"
              placeholder="Una etiqueta por renglón"
              value={etiquetas}
              onChange={(e) => setEtiquetas(e.target.value)}
            />
            <div>
              <Button disabled={!puedePreparar} onClick={prepararTraspaso}>Preparar traspaso</Button>
              <p className="mt-1 text-xs text-ink-muted">Las piezas no salen hasta que otra persona autorice el traspaso.</p>
            </div>
          </section>
        )}
        <div className="flex flex-wrap items-center gap-2">
          <Button size="sm" variant={vista === 'todos' ? 'default' : 'outline'} aria-pressed={vista === 'todos'} onClick={() => setVista('todos')}>Todos</Button>
          <Button size="sm" variant={vista === 'porRecibir' ? 'default' : 'outline'} aria-pressed={vista === 'porRecibir'} onClick={() => setVista('porRecibir')}>Por recibir</Button>
          {vista === 'porRecibir' && (
            <SucursalSelector value={destinoVista} onChange={setDestinoVista} placeholder="Sucursal que recibe" triggerProps={{ 'aria-label': 'Sucursal que recibe' }} />
          )}
        </div>
        {lista.isError ? (
          <ErrorState title="No se pudieron cargar los traspasos" onRetry={() => void lista.refetch()} />
        ) : lista.isLoading ? (
          <TableSkeleton />
        ) : (lista.data?.items.length ?? 0) === 0 ? (
          <EmptyState title={vista === 'porRecibir' ? 'No hay traspasos por recibir.' : 'No hay traspasos.'} />
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Folio</TableHead>
                <TableHead>Origen</TableHead>
                <TableHead>Destino</TableHead>
                <TableHead>Estado</TableHead>
                <TableHead>Fecha</TableHead>
                <TableHead>Pendientes</TableHead>
                <TableHead>Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {lista.data!.items.map((t) => (
                <TableRow key={t.id}>
                  <TableCell className="font-mono text-xs">{t.folio}</TableCell>
                  <TableCell>{nombre(t.sucursalOrigenId)}</TableCell>
                  <TableCell>{nombre(t.sucursalDestinoId)}</TableCell>
                  <TableCell><Badge variant={estadoUi[t.estado].tono}>{estadoUi[t.estado].texto}</Badge></TableCell>
                  <TableCell><DateTimeDisplay value={t.autorizadoAt ?? t.salidaAt} variant="datetime" /></TableCell>
                  <TableCell className="tabular-nums">{t.pendientes} de {t.total}</TableCell>
                  <TableCell className="flex gap-2">
                    <Button variant="outline" size="sm" onClick={() => setAbierto(abierto === t.id ? null : t.id)}>
                      {abierto === t.id ? 'Ocultar piezas' : 'Ver piezas'}
                    </Button>
                    {operar && t.estado === 'Preparado' && t.preparadoPorId !== usuarioId && (
                      <Button size="sm" disabled={autorizar.isPending} onClick={() => confirmarAutorizacion(t.id)}>Autorizar</Button>
                    )}
                    {operar && t.estado === 'Preparado' && t.preparadoPorId === usuarioId && (
                      <span className="self-center text-xs text-ink-muted">Lo autoriza otra persona</span>
                    )}
                    {operar && (t.estado === 'EnTransito' || t.estado === 'ConDiferencia') && t.pendientes > 0 && (
                      <Button size="sm" disabled={recibir.isPending} onClick={() => confirmarRecepcion(t.id)}>Recibir todo</Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </div>
      {abierto && <TraspasoDetallePanel key={abierto} id={abierto} nombre={nombre} operar={operar} />}
    </main>
  );
}
