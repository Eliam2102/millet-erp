import { PageHeader } from '@/components/erp/PageHeader';
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
    <div className="space-y-5">
      <PageHeader titulo={titulo} descripcion={<>{descripcion}</>} />

      <div
        className="rounded-md border border-dashed bg-surface-muted/40 px-6 py-10 text-center"
        data-testid="tesoreria-bandeja-placeholder"
      >
        <Icono className="mx-auto mb-3 size-6 text-ink-muted" aria-hidden="true" />
        <p className="text-sm text-ink-muted">{mensajeArranque}</p>
        {proximamente && (
          <p className="mt-3 text-xs font-medium uppercase tracking-wide text-ink-muted/70">
            Esta función estará disponible en una próxima etapa.
          </p>
        )}
      </div>
    </div>
  );
}
