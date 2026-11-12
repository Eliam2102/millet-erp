import { clsx, type ClassValue } from 'clsx';
import { extendTailwindMerge } from 'tailwind-merge';

// Los tamaños de Millet también deben ceder ante el ajuste explícito del consumidor.
const twMerge = extendTailwindMerge({
  extend: {
    theme: {
      spacing: [
        'rail', 'rail-item', 'module-panel', 'topbar', 'master-list', 'sheet', 'dialog',
        'ctl-xs', 'ctl-sm', 'ctl-md', 'ctl-lg', 'ctl-xl', 'touch',
      ],
    },
  },
});

/**
 * Combina clases de Tailwind resolviendo conflictos (la última gana).
 * Usado por shadcn/ui y por componentes propios. Estándar del ecosistema
 * Tailwind + clsx.
 */
export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs));
}
