import { useQueries } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';
import { piezasPermitidas, type PiezaInicio } from '../config';

export interface ResumenPieza {
  valor: number;
  total: number;
  contexto: string;
  urgentes: number;
  valorTexto?: string | null;
  etapas?: { etiqueta: string; total: number }[] | null;
  serie?: { dia: string; cobros: number; pagos: number }[] | null;
}
export interface PiezaResuelta {
  pieza: PiezaInicio;
  data?: ResumenPieza;
  estado: 'cargando' | 'error' | 'listo';
}
export function componerPiezas(items: PiezaResuelta[]) {
  const indicadores = items.filter(i => i.pieza.tipo === 'indicador').sort((a, b) =>
    (b.data?.urgentes ?? 0) - (a.data?.urgentes ?? 0) || b.pieza.prioridad - a.pieza.prioridad);
  return { indicadores: indicadores.slice(0, 5), visuales: items.filter(i => i.pieza.tipo === 'visual').slice(0, 2) };
}
export type PeriodoInicio = 'Hoy' | 'Semana' | 'Mes';
export function usePiezasInicio(periodo: PeriodoInicio = 'Mes') {
  const permisos = useAuthStore(s => s.permisos);
  const usuario = useAuthStore(s => s.user?.id);
  const empresa = useAuthStore(s => s.currentEmpresaId);
  const sucursal = useAuthStore(s => s.currentSucursalId);
  const permitidas = piezasPermitidas(permisos);
  const visuales = permitidas.filter(p => p.tipo === 'visual').sort((a, b) => b.prioridad - a.prioridad).slice(0, 2);
  const elegibles = [...permitidas.filter(p => p.tipo === 'indicador'), ...visuales];
  const fuentes = [...new Set(elegibles.flatMap(p => p.fuentes.map(f => f.endpoint)))];
  const queries = useQueries({ queries: fuentes.map(endpoint => ({
    queryKey: ['inicio', usuario, empresa, sucursal, endpoint.endsWith('/circuito') ? periodo : null, [...permisos].sort().join(','), endpoint],
    // Conserva la promesa en cache durante el montaje doble de StrictMode.
    // La clave incluye usuario, empresa, sucursal y permisos; una respuesta tardía no se muestra en otro alcance.
    queryFn: async () => {
      const params = new URLSearchParams();
      if (sucursal) params.set('sucursalId', sucursal);
      if (endpoint.endsWith('/circuito')) params.set('periodo', periodo);
      return (await apiRequest<ResumenPieza>(`${endpoint}${params.size ? `?${params}` : ''}`)).data;
    },
    staleTime: 60_000,
    gcTime: 60_000,
    retry: false,
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
  })) });
  const items: PiezaResuelta[] = elegibles.map(pieza => {
    const respuestas = pieza.fuentes.map(f => queries[fuentes.indexOf(f.endpoint)]);
    const estado = respuestas.some(q => q.isError) ? 'error' : respuestas.some(q => q.isPending) ? 'cargando' : 'listo';
    const datos = respuestas.flatMap(q => q.data ? [q.data] : []);
    const data = estado === 'listo' ? {
      valor: datos.reduce((v, d) => v + d.valor, 0), total: datos.reduce((v, d) => v + d.total, 0),
      urgentes: datos.reduce((v, d) => v + d.urgentes, 0), contexto: datos.map(d => d.contexto).join(' · '),
      valorTexto: datos[0]?.valorTexto, etapas: datos.flatMap(d => d.etapas ?? []), serie: datos[0]?.serie,
    } : undefined;
    return { pieza, estado, data };
  });
  return componerPiezas(items);
}
export const formatoMontoInicio = (valor: number) => new Intl.NumberFormat('es-MX', {
  style: 'currency', currency: 'MXN', maximumFractionDigits: 0, minimumFractionDigits: 0,
}).format(valor);
