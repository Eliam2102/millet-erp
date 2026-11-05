import { useState } from 'react';
import { Printer } from 'lucide-react';
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
import { mensajeErrorAccion } from '@/lib/api/error';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useAccionHojaSalida, useCrearHojaSalida, useHojaSalida, useHojasSalida } from '../api/hooks';
import type { EstadoHojaSalida } from '../api/types';
import { CargaHojaPanel } from './CargaHojaPanel';

// Texto además del color: el estado no depende solo de la vista.
const estadoUi: Record<EstadoHojaSalida, { texto: string; tono: 'info' | 'success' | 'neutral' }> = {
  Borrador: { texto: 'En carga', tono: 'info' },
  Emitida: { texto: 'Emitida', tono: 'success' },
  Cancelada: { texto: 'Cancelada', tono: 'neutral' },
};

interface PedidoCaptura {
  numero: string;
  etiquetas: string;
}

export function HojasSalidaPage() {
  const operar = useAuthStore((s) => s.permisos.includes(P.ProductoTerminadoExcepcionesGestionar));
  const lista = useHojasSalida({ pageSize: 50 });
  const sucursales = useSucursales();
  const nombre = (id: string) => sucursales.data?.items.find((s) => s.id === id)?.nombre ?? id.slice(0, 8);
  const crear = useCrearHojaSalida();
  const accion = useAccionHojaSalida();

  const [sucursal, setSucursal] = useState<string | null>(null);
  const [destino, setDestino] = useState('');
  const [foranea, setForanea] = useState(false);
  const [vehiculo, setVehiculo] = useState<string | null>(null);
  const [operador, setOperador] = useState<string | null>(null);
  const [distancia, setDistancia] = useState('');
  const [peso, setPeso] = useState('');
  const [cpOrigen, setCpOrigen] = useState('');
  const [estadoOrigen, setEstadoOrigen] = useState('');
  const [cp, setCp] = useState('');
  const [estadoDestino, setEstadoDestino] = useState('');
  const [pedidos, setPedidos] = useState<PedidoCaptura[]>([{ numero: '', etiquetas: '' }]);
  const [abierto, setAbierto] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const detalle = useHojaSalida(abierto);

  const cargados = pedidos
    .map((p) => ({ numeroPedido: p.numero.trim(), etiquetas: p.etiquetas.split('\n').map((l) => l.trim()).filter(Boolean) }))
    .filter((p) => p.numeroPedido);
  const datosCartaPorte = !foranea || (!!vehiculo && !!operador && Number(distancia) > 0 && Number(peso) > 0 && /^\d{5}$/.test(cpOrigen) && /^[A-Za-z]{3}$/.test(estadoOrigen) && /^\d{5}$/.test(cp) && /^[A-Za-z]{3}$/.test(estadoDestino));
  const puedeCrear = !!sucursal && destino.trim() !== '' && cargados.length > 0 && datosCartaPorte && !crear.isPending;
  const falla = (e: unknown) => setError(mensajeErrorAccion(e));
  const cambiar = (i: number, parche: Partial<PedidoCaptura>) => setPedidos((ps) => ps.map((p, j) => (j === i ? { ...p, ...parche } : p)));

  const armarHoja = () => {
    setError(null);
    setAviso(null);
    crear
      .mutateAsync({
        sucursalId: sucursal!,
        destino: destino.trim(),
        foranea,
        vehiculoId: foranea ? vehiculo : null,
        operadorId: foranea ? operador : null,
        distanciaKm: foranea ? Number(distancia) : null,
        pesoKg: foranea ? Number(peso) : null,
        origenCodigoPostal: foranea ? cpOrigen : null,
        origenEstado: foranea ? estadoOrigen : null,
        destinoCodigoPostal: foranea ? cp : null,
        destinoEstado: foranea ? estadoDestino : null,
        pedidos: cargados,
      })
      .then((h) => {
        setPedidos([{ numero: '', etiquetas: '' }]);
        setAbierto(h.id);
      })
      .catch(falla);
  };
  const ejecutar = (id: string, a: 'emitir' | 'cancelar', estado: string) => {
    setError(null);
    setAviso(null);
    accion
      .mutateAsync({ id, accion: a, estado })
      .then((h) => setAviso(h.yaEmitida ? 'La hoja ya estaba emitida: no se descontó ni se registró nada otra vez.' : a === 'emitir' ? 'Hoja emitida: piezas descontadas, salida registrada en Facturación y pedidos en ruta (71).' : 'Hoja cancelada: las piezas volvieron a PT.'))
      .catch(falla);
  };

  return (
    <main className="flex h-full min-h-0 flex-col gap-5 overflow-auto text-sm">
      <div data-print="hidden" className="flex flex-col gap-5">
        <Breadcrumbs items={[{ label: 'Producto Terminado' }, { label: 'Hojas de salida' }]} />
        <PageHeader
          titulo="Hojas de salida y albaranes"
          descripcion="Lo que el chofer lleva en la ruta: un albarán por pedido, en el orden de entrega. Se arma la ruta y se carga pieza por pieza (también desde el celular). Al emitir se descuentan las piezas una sola vez, se registra la salida para facturar solo lo embarcado y los pedidos pasan a 71."
        />
        {error && (
          <AvisoEnLinea tono="error" titulo="No se pudo completar la acción">
            {error}
          </AvisoEnLinea>
        )}
        {aviso && <p role="status" className="rounded-md bg-success-bg p-3 text-success-fg">{aviso}</p>}
        {operar && abierto && detalle.data?.estado === 'Borrador' && (
          <CargaHojaPanel hoja={detalle.data} sucursal={nombre(detalle.data.sucursalId)} emitiendo={accion.isPending} onEmitir={() => ejecutar(detalle.data!.id, 'emitir', 'Borrador')} />
        )}
        {operar && (
          <section aria-label="Nueva hoja de salida" className="flex flex-col gap-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
            <div className="flex flex-wrap items-center gap-3">
              <SucursalSelector value={sucursal} onChange={setSucursal} placeholder="Sucursal de salida" triggerProps={{ 'aria-label': 'Sucursal de salida' }} />
              <Input className="w-72" aria-label="Destino de la ruta" placeholder="Destino de la ruta" value={destino} onChange={(e) => setDestino(e.target.value)} />
              <label className="flex items-center gap-2">
                <Checkbox aria-label="Entrega foránea o por carretera federal" checked={foranea} onCheckedChange={(v) => setForanea(v === true)} />
                Foránea o carretera federal (lleva Carta Porte)
              </label>
            </div>
            {foranea && (
              <div className="flex flex-wrap items-end gap-3">
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
                  <Input className="w-32" placeholder="77500" maxLength={5} value={cp} onChange={(e) => setCp(e.target.value)} />
                </label>
                <label className="flex flex-col gap-1 text-xs text-ink-muted">
                  Estado de destino (clave SAT)
                  <Input className="w-44" placeholder="ROO" maxLength={3} value={estadoDestino} onChange={(e) => setEstadoDestino(e.target.value)} />
                </label>
              </div>
            )}
            <div className="flex flex-col gap-3">
              {pedidos.map((p, i) => (
                <div key={i} className="flex flex-wrap items-start gap-3">
                  <span className="mt-2 w-24 text-ink-muted">Entrega {i + 1}</span>
                  <Input className="w-40" aria-label={`Pedido de la entrega ${i + 1}`} placeholder="Número de pedido" value={p.numero} onChange={(e) => cambiar(i, { numero: e.target.value })} />
                  <Textarea className="min-h-16 w-96" aria-label={`Etiquetas cargadas del pedido ${i + 1}`} placeholder="Etiquetas ya cargadas, una por renglón (opcional: también se cargan una por una)" value={p.etiquetas} onChange={(e) => cambiar(i, { etiquetas: e.target.value })} />
                </div>
              ))}
              <div>
                <Button variant="outline" size="sm" onClick={() => setPedidos((ps) => [...ps, { numero: '', etiquetas: '' }])}>Agregar pedido a la ruta</Button>
              </div>
            </div>
            <div>
              <Button disabled={!puedeCrear} onClick={armarHoja}>Armar hoja de salida</Button>
            </div>
          </section>
        )}
        {lista.isError ? (
          <ErrorState title="No se pudieron cargar las hojas de salida" onRetry={() => void lista.refetch()} />
        ) : lista.isLoading ? (
          <TableSkeleton />
        ) : (lista.data?.items.length ?? 0) === 0 ? (
          <EmptyState title="No hay hojas de salida." />
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Hoja</TableHead>
                <TableHead>Sucursal</TableHead>
                <TableHead>Destino</TableHead>
                <TableHead>Estado</TableHead>
                <TableHead>Pedidos</TableHead>
                <TableHead>Piezas</TableHead>
                <TableHead>Carta Porte</TableHead>
                <TableHead>Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {lista.data!.items.map((h) => (
                <TableRow key={h.id}>
                  <TableCell className="font-mono text-xs">{h.folio}</TableCell>
                  <TableCell>{nombre(h.sucursalId)}</TableCell>
                  <TableCell>{h.destino}</TableCell>
                  <TableCell><Badge variant={estadoUi[h.estado].tono}>{estadoUi[h.estado].texto}</Badge></TableCell>
                  <TableCell className="tabular-nums">{h.pedidos}</TableCell>
                  <TableCell className="tabular-nums">{h.piezas}</TableCell>
                  <TableCell>{h.cartaPorteFolio ?? (h.foranea ? 'Al emitir' : 'No aplica')}</TableCell>
                  <TableCell className="flex gap-2">
                    <Button variant="outline" size="sm" onClick={() => setAbierto(abierto === h.id ? null : h.id)}>
                      {abierto === h.id ? 'Ocultar' : h.estado === 'Borrador' && operar ? 'Cargar' : 'Ver albaranes'}
                    </Button>
                    {operar && h.estado === 'Borrador' && (
                      <>
                        <Button size="sm" disabled={accion.isPending} onClick={() => ejecutar(h.id, 'emitir', h.estado)}>Emitir</Button>
                        <Button variant="outline" size="sm" disabled={accion.isPending} onClick={() => ejecutar(h.id, 'cancelar', h.estado)}>Cancelar</Button>
                      </>
                    )}
                    {operar && h.estado === 'Emitida' && (
                      <Button variant="outline" size="sm" disabled={accion.isPending} onClick={() => ejecutar(h.id, 'emitir', h.estado)}>Emitir de nuevo</Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </div>
      {abierto && detalle.data && !(operar && detalle.data.estado === 'Borrador') && (
        <section aria-label="Hoja de salida" className="flex flex-col gap-3 rounded-lg bg-surface-card p-4 shadow-card-flat print:shadow-none">
          <div className="flex items-start justify-between gap-3">
            <div>
              <h2 className="text-base font-medium">Hoja de salida {detalle.data.folio}</h2>
              <p className="text-ink-muted">
                {nombre(detalle.data.sucursalId)} a {detalle.data.destino} · <DateTimeDisplay value={detalle.data.emitidaAt ?? detalle.data.creadaAt} variant="datetime" />
                {detalle.data.cartaPorteFolio ? ` · Carta Porte ${detalle.data.cartaPorteFolio}` : ''}
              </p>
            </div>
            <Button data-print="hidden" variant="outline" size="sm" onClick={() => window.print()}>
              <Printer className="size-4" aria-hidden="true" /> Imprimir hoja de salida
            </Button>
          </div>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Secuencia</TableHead>
                <TableHead>Albarán</TableHead>
                <TableHead>Pedido</TableHead>
                <TableHead>Cliente</TableHead>
                <TableHead>Obra o destino</TableHead>
                <TableHead>Lo cargado</TableHead>
                <TableHead>Entrega</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {detalle.data.albaranes.map((a) => (
                <TableRow key={a.id}>
                  <TableCell className="tabular-nums">{a.secuencia}</TableCell>
                  <TableCell className="font-mono text-xs">{a.folio}</TableCell>
                  <TableCell>{a.numeroPedido}</TableCell>
                  <TableCell>{a.cliente}</TableCell>
                  <TableCell>{a.destino ?? 'Sin obra'}</TableCell>
                  <TableCell>
                    <ul>
                      {a.lineas.map((l) => (
                        <li key={l.posicion}>{l.piezas} pzas · posición {l.posicion}{l.descripcion ? ` · ${l.descripcion}` : ''}</li>
                      ))}
                    </ul>
                    <span className="text-ink-muted">{a.etiquetas.join(', ')}</span>
                    {(a.rotas?.length ?? 0) > 0 && <p className="text-warning-fg">Pendiente de reposición (rota al cargar): {a.rotas!.join(', ')}</p>}
                  </TableCell>
                  <TableCell>
                    {detalle.data.estado !== 'Emitida' ? '—' : a.entrega ? `${a.entrega.resultado === 'Entregada' ? 'Entregada' : a.entrega.resultado === 'Parcial' ? 'Entrega parcial' : 'Rechazada en sitio'} · recibió ${a.entrega.receptor}` : 'Por entregar (en Entregas)'}
                    {a.estadoPedido === '115' && <p className="text-success-fg">Pedido en 115</p>}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </section>
      )}
    </main>
  );
}
