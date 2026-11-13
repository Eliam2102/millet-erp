import { useEffect, useState, useSyncExternalStore } from 'react';
import { useAuthStore } from '@/lib/auth/auth-store';

export const TAMANOS_PAGINA = [10, 20, 25, 50, 100];
export type TamanoPagina = number | 'auto';
const evento = 'millet:tamano-tabla';
const memoria = new Map<string, TamanoPagina>();

export function leerTamanoPagina(clave: string): TamanoPagina {
  if (memoria.has(clave)) return memoria.get(clave)!;
  try {
    const valor = localStorage.getItem(clave);
    const numero = Number(valor);
    return valor != null && Number.isInteger(numero) && numero >= 1 && numero <= 200
      ? numero
      : 'auto';
  } catch {
    return 'auto';
  }
}

function suscribir(listener: () => void) {
  window.addEventListener(evento, listener);
  window.addEventListener('storage', listener);
  return () => {
    window.removeEventListener(evento, listener);
    window.removeEventListener('storage', listener);
  };
}

/** Sólo guarda presentación; nunca filtros, tokens ni registros del ERP. */
export function useTamanoPagina(tabla = 'principal', maximo = 100, inicial = 10, regionId?: string) {
  const usuario = useAuthStore((s) => s.user?.id ?? 'local');
  const clave = `millet:tabla:v1:${usuario}:${window.location.pathname}:${tabla}`;
  const modo = useSyncExternalStore(
    suscribir,
    () => leerTamanoPagina(clave),
    () => 'auto' as const,
  );
  const [automatico, setAutomatico] = useState(inicial);
  const actual = modo;
  const [limiteEdicion, setLimiteEdicion] = useState<number | null>(null);

  // La región puede aparecer después de una carga o cambiar al abrir una bitácora.
  useEffect(() => {
    const region = (regionId ? document.getElementById(regionId) : null) ?? Array.from(document.querySelectorAll<HTMLElement>('[data-table-viewport]')).find((r) => !r.querySelector('[data-table-viewport]') && r.clientHeight > 0);
    if (!region) return;
    const medir = () => {
      if (region.querySelector('form, [data-edicion-activa]')) {
        setLimiteEdicion((previo) => previo ?? Math.min(maximo, actual === 'auto' ? automatico : actual));
        return;
      }
      setLimiteEdicion(null);
      if (actual !== 'auto' || region.clientHeight <= 0) return;
      const cabecera = region.querySelector('thead')?.getBoundingClientRect().height ?? (region.querySelector('table') ? 36 : 0);
      const filas = Array.from(region.querySelectorAll('tbody tr, [data-fila-registro]')).slice(0, 5);
      const altoFila = Math.max(44, ...filas.map((f) => f.getBoundingClientRect().height));
      setAutomatico(
        Math.max(5, Math.min(maximo, Math.floor((region.clientHeight - cabecera) / altoFila))),
      );
    };
    medir();
    const resize = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(medir);
    resize?.observe(region);
    const cambios = new MutationObserver(medir);
    cambios.observe(region, { childList: true, subtree: true });
    window.addEventListener('resize', medir);
    return () => {
      resize?.disconnect();
      cambios.disconnect();
      window.removeEventListener('resize', medir);
    };
  });

  const elegir = (valor: TamanoPagina) => {
    const valido = valor === 'auto' ? valor : Math.max(1, Math.min(maximo, Math.trunc(valor)));
    try {
      localStorage.setItem(clave, String(valido));
      memoria.delete(clave);
    } catch {
      memoria.set(clave, valido);
    }
    window.dispatchEvent(new Event(evento));
  };
  return { modo: actual, limit: limiteEdicion ?? Math.min(maximo, actual === 'auto' ? automatico : actual), elegir, editando: limiteEdicion !== null };
}
