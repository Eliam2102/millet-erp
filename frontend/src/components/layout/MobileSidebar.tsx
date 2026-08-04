import * as DialogPrimitive from '@radix-ui/react-dialog';
import { X } from 'lucide-react';
import type { NavModulo } from '@/lib/nav';
import { SidebarNav } from '@/components/layout/SidebarNav';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;MobileSidebar/&gt;</c> — drawer slide-from-left para mobile
 * (sub-<c>md</c>). Usa Radix Dialog directamente (no el wrapper de
 * shadcn que centra el modal) para tener animación lateral.
 *
 * <para>Reutiliza <c>&lt;SidebarNav/&gt;</c>; el <c>AppLauncherModal</c>
 * vive a nivel de <c>&lt;AppShell/&gt;</c> para que sobreviva al
 * cierre del drawer. El flujo en mobile: tap módulo → drawer cierra
 * (vía <c>onItemSelect</c> del wrapper) y modal de cards abre como
 * reemplazo.</para>
 *
 * <para>Accesibilidad: Radix maneja focus trap, ESC, click fuera, y
 * <c>aria-modal</c> automáticamente. El botón de cerrar interno
 * (<c>X</c>) está sr-labelled.</para>
 */
export interface MobileSidebarProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onModuloOpen: (modulo: NavModulo) => void;
}

export function MobileSidebar({
  open,
  onOpenChange,
  onModuloOpen,
}: MobileSidebarProps) {
  return (
    <DialogPrimitive.Root open={open} onOpenChange={onOpenChange}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay
          className={cn(
            'fixed inset-0 z-50 bg-black/60',
            'data-[state=open]:animate-in data-[state=closed]:animate-out',
            'data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0',
          )}
        />
        <DialogPrimitive.Content
          // sr-only title required by Radix for a11y; oculto visualmente.
          className={cn(
            'fixed inset-y-0 left-0 z-50 flex w-60 flex-col',
            'data-[state=open]:animate-in data-[state=closed]:animate-out',
            'data-[state=closed]:slide-out-to-left data-[state=open]:slide-in-from-left',
            'duration-200',
          )}
          aria-describedby={undefined}
        >
          <DialogPrimitive.Title className="sr-only">
            Navegación principal
          </DialogPrimitive.Title>

          <SidebarNav
            onLinkSelect={() => onOpenChange(false)}
            onModuloOpen={(m) => {
              onOpenChange(false);
              onModuloOpen(m);
            }}
          />

          <DialogPrimitive.Close
            className="absolute right-2 top-2 rounded p-1 text-sidebar-foreground hover:bg-sidebar-active/50 hover:text-white focus:outline-none focus:ring-2 focus:ring-ring"
            aria-label="Cerrar navegación"
          >
            <X className="h-4 w-4" />
          </DialogPrimitive.Close>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}
