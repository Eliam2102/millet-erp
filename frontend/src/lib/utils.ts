import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

/**
 * Combina clases de Tailwind resolviendo conflictos (la última gana).
 * Usado por shadcn/ui y por componentes propios. Estándar del ecosistema
 * Tailwind + clsx.
 */
export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs));
}
