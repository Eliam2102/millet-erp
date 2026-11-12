import { useEffect, useMemo, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';
import { crearSonidoAvisos } from '../audio/sonidoAvisos';
import type { CentroAvisosRespuesta, FiltrosAvisos } from './types';

const base = '/api/v1/notificaciones';
export function useCentroAvisos(filtros: FiltrosAvisos) {
  const usuario = useAuthStore(s => s.user?.id);
  const empresa = useAuthStore(s => s.currentEmpresaId);
  const sucursal = useAuthStore(s => s.currentSucursalId);
  const permisos = useAuthStore(s => s.permisos);
  const cambiando = useAuthStore(s => s.isSwitchingEmpresa);
  const scope = JSON.stringify([usuario, empresa, sucursal, [...permisos].sort()]);
  const cursorRef = useRef<{ scope: string; valor?: string }>({ scope: '' });
  const sesion = useMemo(() => ({ scope, sonido: crearSonidoAvisos(JSON.stringify([usuario, empresa, sucursal])) }), [scope, usuario, empresa, sucursal]);
  const cliente = useQueryClient();
  const [errorAudio, setErrorAudio] = useState<{ scope: string; mensaje: string }>();
  useEffect(() => {
    sesion.sonido.abrir();
    const recuperar = () => sesion.sonido.recuperar();
    const visibilidad = () => { if (document.visibilityState === 'hidden') recuperar(); };
    window.addEventListener('offline', recuperar);
    window.addEventListener('online', recuperar);
    document.addEventListener('visibilitychange', visibilidad);
    return () => {
      window.removeEventListener('offline', recuperar);
      window.removeEventListener('online', recuperar);
      document.removeEventListener('visibilitychange', visibilidad);
      sesion.sonido.cerrar();
    };
  }, [sesion]);
  const habilitada = Boolean(usuario && empresa && !cambiando);
  const preferencia = useQuery({
    queryKey: ['avisos-sonido', usuario], enabled: habilitada, retry: false, gcTime: 0,
    queryFn: async ({ signal }) => (await apiRequest<{ habilitado: boolean }>(`${base}/preferencia-sonido`, { signal })).data,
  });
  useEffect(() => { sesion.sonido.establecerHabilitado(preferencia.data?.habilitado === true && !preferencia.isError); }, [sesion, preferencia.data, preferencia.isError]);
  const query = useQuery({
    queryKey: ['centro-avisos', scope, filtros], enabled: habilitada, retry: false, gcTime: 0,
    refetchInterval: () => document.visibilityState === 'visible' ? 15000 : false,
    refetchOnWindowFocus: 'always', refetchOnReconnect: 'always',
    queryFn: async ({ signal }) => {
      try {
        if (cursorRef.current.scope !== scope) cursorRef.current = { scope };
        const data = (await apiRequest<CentroAvisosRespuesta>(`${base}/sincronizar`, {
          method: 'POST', signal, body: { ...filtros, sucursalId: sucursal, cursorDeteccion: cursorRef.current.valor, pageSize: 20 },
        })).data;
        const actual = useAuthStore.getState();
        if (signal.aborted || actual.user?.id !== usuario || actual.currentEmpresaId !== empresa || actual.currentSucursalId !== sucursal || JSON.stringify([...actual.permisos].sort()) !== JSON.stringify([...permisos].sort())) throw new DOMException('Contexto cambiado', 'AbortError');
        if (data.contexto.usuarioId !== usuario || data.contexto.empresaId !== empresa || data.contexto.sucursalId !== sucursal) throw new Error('La consulta respondió para otro contexto.');
        const cursor = data.novedades.hastaSecSeguro;
        if (!/^\d+$/.test(cursor)) throw new Error('La consulta devolvió una secuencia inválida.');
        if (!cursorRef.current.valor || BigInt(cursor) > BigInt(cursorRef.current.valor)) cursorRef.current.valor = cursor;
        void sesion.sonido.sincronizar(cursor, data.novedades.hayNovedades, data.fuentes).catch(() => setErrorAudio({ scope, mensaje: 'El sonido no pudo reproducirse. Activa o prueba el sonido; los avisos visuales continúan.' }));
        return data;
      } catch (error) { sesion.sonido.recuperar(); throw error; }
    },
  });
  const leer = useMutation({
    mutationFn: async (clave: string) => apiRequest(`${base}/${encodeURIComponent(clave)}/leer`, { method: 'POST', body: { sucursalId: sucursal } }),
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['centro-avisos', scope] }),
  });
  const cambiarSonido = useMutation({
    mutationFn: async (habilitado: boolean) => {
      if (habilitado) await sesion.sonido.probar();
      await apiRequest(`${base}/preferencia-sonido`, { method: 'PUT', body: { habilitado } });
      return habilitado;
    },
    onSuccess: habilitado => { cliente.setQueryData(['avisos-sonido', usuario], { habilitado }); setErrorAudio(undefined); },
  });
  return {
    ...query, data: habilitada && !query.isError ? query.data : undefined, leer, cambiarSonido,
    sonidoHabilitado: preferencia.data?.habilitado === true && !preferencia.isError,
    errorPreferencia: preferencia.isError,
    errorAudio: errorAudio?.scope === scope ? errorAudio.mensaje : preferencia.data?.habilitado && !sesion.sonido.soportado() ? 'Este navegador no permite alertas sonoras automáticas. Los avisos visuales continúan.' : undefined,
    async probarSonido() {
      try { await sesion.sonido.probar(); setErrorAudio(undefined); }
      catch { setErrorAudio({ scope, mensaje: 'No se pudo habilitar el sonido en este navegador. Los avisos visuales continúan.' }); }
    },
    scope,
  };
}
