import { cn } from '@/lib/utils';

/** Clases del enlace de una fila de lista de Datos Maestros: hover, activa y foco visible. */
export function filaMaestroClase(activo: boolean) {
  return cn(
    'block px-3 py-1.5 text-sm transition-colors focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-brand',
    activo ? 'bg-brand/10 hover:bg-brand/15' : 'hover:bg-surface-muted/40',
  );
}

/** Enfoca el primer control del campo <c>campo-{nombre}</c>; respeta prefers-reduced-motion. */
export function irACampo(campo: string) {
  const bloque = document.getElementById(`campo-${campo}`);
  if (!bloque) return;
  const reducido = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
  bloque.scrollIntoView({ block: 'center', behavior: reducido ? 'auto' : 'smooth' });
  bloque.querySelector<HTMLElement>('input, select, textarea, button, [role="combobox"]')?.focus({
    preventScroll: true,
  });
}
