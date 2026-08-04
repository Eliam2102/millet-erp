import type { LucideIcon } from 'lucide-react';

export interface BandejaPlaceholderProps {
  titulo: string;
  descripcion: string;
  icono: LucideIcon;
  /**
   * Mensaje de arranque (05-frontend-diseno §6): el módulo depende de
   * eventos de otros módulos y la bandeja vacía es un estado NORMAL —
   * explicar de dónde llegan los datos en vez de un vacío mudo.
   */
  mensajeArranque: string;
  /** Pantalla que llega en un PR FE posterior (chip informativo). */
  proximamente?: string;
}

/**
 * Layout de bandeja vacía del módulo Tesorería (TES-FE-PR1). Cada ruta
 * lo monta con su copy mientras la pantalla real llega en su PR
 * (FE-PR2..FE-PR6). Sin breadcrumbs (patrones-compras §6.7).
 */
export function BandejaPlaceholder({
  titulo,
  descripcion,
  icono: Icono,
  mensajeArranque,
  proximamente,
}: BandejaPlaceholderProps) {
  return (
    <div className="mx-auto max-w-3xl space-y-6 px-4 py-10">
      <header className="space-y-1">
        <h1 className="flex items-center gap-2 text-2xl font-semibold">
          <Icono className="h-6 w-6 text-primary" />
          {titulo}
        </h1>
        <p className="text-sm text-muted-foreground">{descripcion}</p>
      </header>

      <div
        className="rounded-md border border-dashed bg-muted/40 px-6 py-10 text-center"
        data-testid="tesoreria-bandeja-placeholder"
      >
        <p className="text-sm text-muted-foreground">{mensajeArranque}</p>
        {proximamente && (
          <p className="mt-3 text-xs font-medium uppercase tracking-wide text-muted-foreground/70">
            {proximamente}
          </p>
        )}
      </div>
    </div>
  );
}
