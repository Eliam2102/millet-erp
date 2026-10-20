import type { HTMLAttributes } from 'react';
import { cn } from '@/lib/utils';

/** Rejilla común para alinear campos entre secciones consecutivas. */
export function FormGrid({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('grid grid-cols-12 gap-x-4 gap-y-3.5', className)} {...props} />;
}
export interface FormCampoProps extends HTMLAttributes<HTMLDivElement> {
  span?: 3 | 4 | 6 | 12;
}
const spans = { 3: 'sm:col-span-3', 4: 'sm:col-span-4', 6: 'sm:col-span-6', 12: 'sm:col-span-12' };
/** Campo de ancho declarado; ocupa toda la rejilla en móvil. */
export function FormCampo({ span = 6, className, ...props }: FormCampoProps) {
  return <div className={cn('col-span-12 min-w-0', spans[span], className)} {...props} />;
}
