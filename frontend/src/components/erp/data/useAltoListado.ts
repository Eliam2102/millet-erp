import { useLayoutEffect, useRef } from 'react';

/** El límite pertenece al listado, no al shell ni a los formularios de la pantalla. */
export function useAltoListado(reserva = 0) {
  const ref = useRef<HTMLDivElement>(null);
  useLayoutEffect(() => {
    const nodo = ref.current;
    const limite = nodo?.closest('main, [role="dialog"]');
    if (!nodo || !limite || nodo.closest('form')) return;
    const medir = () => {
      if (nodo.parentElement?.closest('[data-table-viewport]')) {
        nodo.style.setProperty('--listado-alto', '100%');
        return;
      }
      const fondo = limite.matches('main') ? window.innerHeight : limite.getBoundingClientRect().bottom;
      nodo.style.setProperty('--listado-alto', `${Math.max(240, Math.floor(fondo - nodo.getBoundingClientRect().top - 16 - reserva))}px`);
    };
    medir();
    window.addEventListener('resize', medir);
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(medir);
    if (nodo.parentElement) observer?.observe(nodo.parentElement);
    return () => { window.removeEventListener('resize', medir); observer?.disconnect(); };
  }, [reserva]);
  return ref;
}
