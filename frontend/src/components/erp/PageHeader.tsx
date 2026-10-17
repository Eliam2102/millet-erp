import type { ReactNode } from 'react';

export interface PageHeaderProps {
  titulo: ReactNode;
  /** Una sola oración (DS §6). */
  descripcion?: ReactNode;
  /** Botones/acciones alineados a la derecha. */
  acciones?: ReactNode;
  /** Para folios: tipografía mono en el H1. */
  mono?: boolean;
}

/**
 * Encabezado estándar de página de los módulos (UX-F1-08). Los márgenes
 * los pone el `main` del AppShell: las páginas no declaran `p-*`,
 * `max-w-*` ni `mx-auto` propios.
 */
export function PageHeader({ titulo, descripcion, acciones, mono }: PageHeaderProps) {
  return (
    <header className="flex flex-wrap items-start justify-between gap-3">
      <div className="min-w-0 space-y-1">
        <h1
          className={`text-3xl font-semibold tracking-tight text-ink${mono ? ' font-mono' : ''}`}
        >
          {titulo}
        </h1>
        {descripcion != null && <p className="text-sm text-ink-muted">{descripcion}</p>}
      </div>
      {acciones != null && <div className="flex flex-wrap items-center gap-2">{acciones}</div>}
    </header>
  );
}
