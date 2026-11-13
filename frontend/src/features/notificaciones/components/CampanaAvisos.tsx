import { useId, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Bell } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useCentroAvisos } from '../api/useCentroAvisos';
import type { FiltrosAvisos } from '../api/types';
import { destinoInterno } from '../api/destinoInterno';

export function CampanaAvisos() {
  const usuario = useAuthStore(s => s.user?.id);
  const empresa = useAuthStore(s => s.currentEmpresaId);
  const sucursal = useAuthStore(s => s.currentSucursalId);
  const permisos = useAuthStore(s => s.permisos);
  return <ContenidoCampana key={JSON.stringify([usuario, empresa, sucursal, [...permisos].sort()])} />;
}

function ContenidoCampana() {
  const [open, setOpen] = useState(false);
  const [filtros, setFiltros] = useState<FiltrosAvisos>({ condicion: 'Todas' });
  const [anteriores, setAnteriores] = useState<(string | undefined)[]>([]);
  const centro = useCentroAvisos(filtros);
  const id = useId();
  const data = centro.data;
  const cantidad = data ? `${data.coberturaCompleta ? '' : 'Al menos '}${data.totalSinLeerConocido}` : undefined;
  const fallidas = data?.fuentes.filter(f => f.estado === 'Failed') ?? [];
  const cambiarFiltro = (valor: Partial<FiltrosAvisos>) => { setFiltros({ ...filtros, ...valor, continuacion: undefined }); setAnteriores([]); };
  return <Popover open={open} onOpenChange={setOpen}>
    <PopoverTrigger asChild>
      <Button variant="ghost" size="icon" className="relative" aria-label={cantidad ? `Notificaciones: ${cantidad} sin leer` : 'Notificaciones'}>
        <Bell className="h-4 w-4" />
        {data && data.totalSinLeerConocido > 0 && <span className="absolute right-0 top-0 rounded-full bg-danger-bg px-1 text-2xs text-danger-fg tabular-nums">{data.coberturaCompleta ? '' : '≥'}{data.totalSinLeerConocido}</span>}
      </Button>
    </PopoverTrigger>
    <PopoverContent align="end" className="w-96 max-w-[calc(100vw-2rem)] space-y-3 bg-surface-card shadow-card">
      <h2 className="text-sm font-semibold">Notificaciones</h2>
      {data && <p className="text-xs text-ink-muted" role="status">{cantidad} sin leer · {data.coberturaCompleta ? '' : 'Al menos '}{data.totalAccionesConocidas} por hacer</p>}
      <div className="flex gap-2">
        <div className="min-w-0 flex-1 space-y-1">
          <label htmlFor={`${id}-condicion`} className="text-xs text-ink-strong">Mostrar</label>
          <Select value={filtros.condicion} onValueChange={condicion => cambiarFiltro({ condicion: condicion as FiltrosAvisos['condicion'] })}>
            <SelectTrigger id={`${id}-condicion`}><SelectValue /></SelectTrigger>
            <SelectContent><SelectItem value="Todas">Todos</SelectItem><SelectItem value="SinLeer">Sin leer</SelectItem><SelectItem value="PorHacer">Por hacer</SelectItem></SelectContent>
          </Select>
        </div>
        <div className="min-w-0 flex-1 space-y-1">
          <label htmlFor={`${id}-modulo`} className="text-xs text-ink-strong">Módulo</label>
          <Select value={filtros.modulo ?? 'todos'} onValueChange={modulo => cambiarFiltro({ modulo: modulo === 'todos' ? undefined : modulo })}>
            <SelectTrigger id={`${id}-modulo`}><SelectValue /></SelectTrigger>
            <SelectContent><SelectItem value="todos">Todos</SelectItem>{data?.fuentes.map(f => <SelectItem key={f.id} value={f.id}>{f.id}</SelectItem>)}</SelectContent>
          </Select>
        </div>
      </div>
      {centro.isPending && <p className="text-sm text-ink-muted" role="status">Consultando tus avisos…</p>}
      {centro.isError && <div role="alert" className="text-sm text-danger-fg">No se pudieron consultar tus avisos. Vuelve a consultar para recuperar los pendientes.<Button variant="link" size="sm" onClick={() => void centro.refetch()}>Volver a consultar</Button></div>}
      {data && !data.coberturaCompleta && <div role="alert" className="text-xs text-warning-fg">Consulta incompleta. {fallidas.map(f => `No se pudo consultar ${f.id}.`).join(' ')}<Button variant="link" size="sm" onClick={() => void centro.refetch()}>Volver a consultar</Button></div>}
      {data && data.items.length === 0 && <p className="text-sm text-ink-muted">{data.coberturaCompleta ? 'No hay avisos con estos filtros.' : 'No se encontraron avisos en los orígenes consultados; faltan orígenes por recuperar.'}</p>}
      <ul className="max-h-80 space-y-2 overflow-auto" aria-label="Avisos">
        {data?.items.map(({ aviso, leido }) => <li key={aviso.claveOcurrencia} className={`space-y-1 rounded-md p-2 ${leido ? '' : 'bg-surface-selected'}`}>
          <div className="flex items-start justify-between gap-2"><p className="text-sm font-medium text-ink-strong">{aviso.titulo}</p><Badge variant={aviso.categoria === 'Accion' ? 'warning' : 'info'}>{aviso.categoria === 'Accion' ? 'Por hacer' : 'Informativo'}</Badge></div>
          {aviso.folio && <p className="font-mono text-xs">{aviso.folio}</p>}
          {aviso.motivo && <p className="text-xs text-ink-muted">{aviso.motivo}</p>}
          <p className="text-xs text-ink-muted">{aviso.modulo} · {new Date(aviso.fechaOcurrencia).toLocaleString('es-MX')} · {leido ? 'Leído' : 'Sin leer'}</p>
          <div className="flex gap-2">
            {destinoInterno(aviso.destino) && <Button asChild variant="link" size="sm"><Link to={aviso.destino as '/'} onClick={() => { if (!leido) centro.leer.mutate(aviso.claveOcurrencia); setOpen(false); }}>Abrir documento</Link></Button>}
            {!leido && <Button variant="ghost" size="sm" disabled={centro.leer.isPending} onClick={() => centro.leer.mutate(aviso.claveOcurrencia)}>Marcar leído</Button>}
          </div>
        </li>)}
      </ul>
      {centro.leer.isError && <p role="alert" className="text-xs text-danger-fg">No se pudo marcar la lectura. Consulta de nuevo y verifica que el aviso siga disponible.</p>}
      {data && <div className="flex items-center justify-between gap-2 text-xs text-ink-muted">
        <Button size="sm" variant="outline" disabled={anteriores.length === 0} title={anteriores.length === 0 ? 'Estás en la primera página' : undefined} onClick={() => { setFiltros({ ...filtros, continuacion: anteriores.at(-1) }); setAnteriores(anteriores.slice(0, -1)); }}>Anterior</Button>
        <span className="tabular-nums">{data.totalFiltradoConocido}{data.coberturaCompleta ? '' : ' o más'} avisos</span>
        <Button size="sm" variant="outline" disabled={!data.siguienteContinuacion} title={!data.siguienteContinuacion ? 'No hay otra página consultada' : undefined} onClick={() => { setAnteriores([...anteriores, filtros.continuacion]); setFiltros({ ...filtros, continuacion: data.siguienteContinuacion ?? undefined }); }}>Siguiente</Button>
      </div>}
      <div className="space-y-1 border-t border-line-divider pt-2">
        <div className="flex items-center justify-between gap-2"><span className="text-xs text-ink-muted">Sonido {centro.sonidoHabilitado ? 'activado' : 'apagado'}</span><div className="flex gap-1"><Button variant="ghost" size="sm" disabled={centro.cambiarSonido.isPending} onClick={() => centro.cambiarSonido.mutate(!centro.sonidoHabilitado)}>{centro.sonidoHabilitado ? 'Desactivar' : 'Activar sonido'}</Button><Button variant="ghost" size="sm" disabled={centro.cambiarSonido.isPending} title={centro.cambiarSonido.isPending ? 'Espera a que termine la activación del sonido' : undefined} onClick={() => void centro.probarSonido()}>Probar sonido</Button></div></div>
        <p className="text-xs text-ink-muted">Solo con el ERP abierto; al entrar, prueba el sonido para habilitarlo en este navegador. Leer un aviso no termina la acción.</p>
        {centro.estadoAudio && <p role="status" className="text-xs text-ink-muted">Último estado del sonido: {centro.estadoAudio}</p>}
        {(centro.errorAudio || centro.errorPreferencia || centro.cambiarSonido.isError) && <p role="alert" className="text-xs text-warning-fg">{centro.errorAudio ?? 'No se pudo recuperar o guardar el sonido. Vuelve a probarlo; los avisos visuales continúan.'}</p>}
      </div>
    </PopoverContent>
  </Popover>;
}
