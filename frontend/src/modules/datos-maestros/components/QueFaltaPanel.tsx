import { TriangleAlert } from 'lucide-react';
import type { ReactNode } from 'react';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { irACampo } from '../lib/maestro-ui';

export interface ItemFalta {
  etiqueta: string;
  /** Nombre del campo (<c>FormRow campo="…"</c>): el botón lleva el foco a ese campo. */
  campo?: string;
  /** Ancla a otra sección de la página (<c>#expediente-proveedor</c>). */
  href?: string;
}

const enlace =
  'rounded-sm underline underline-offset-2 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand';

/**
 * Bloque «Qué falta para completar» de los detalles de Datos Maestros: lista lo que
 * falta del registro, con enlace al campo o a la sección. No pinta nada si no falta nada.
 */
export function QueFaltaPanel({
  items,
  titulo = 'Qué falta para completar',
  nota,
}: {
  items: readonly ItemFalta[];
  titulo?: string;
  nota?: ReactNode;
}) {
  if (items.length === 0) return null;
  return (
    <Alert variant="warning" role="region" aria-label={titulo}>
      <TriangleAlert className="size-4" aria-hidden="true" />
      <AlertTitle className="text-sm font-semibold">
        {titulo} ({items.length})
      </AlertTitle>
      <AlertDescription>
        <ul className="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-sm">
          {items.map((i) => (
            <li key={i.etiqueta}>
              {i.campo != null ? (
                <button type="button" className={enlace} onClick={() => irACampo(i.campo!)}>
                  {i.etiqueta}
                </button>
              ) : i.href != null ? (
                <a href={i.href} className={enlace}>
                  {i.etiqueta}
                </a>
              ) : (
                i.etiqueta
              )}
            </li>
          ))}
        </ul>
        {nota != null && <p className="mt-2 text-xs">{nota}</p>}
      </AlertDescription>
    </Alert>
  );
}
