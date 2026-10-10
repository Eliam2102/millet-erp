import * as DialogPrimitive from '@radix-ui/react-dialog';
import { X } from 'lucide-react';
import { SidebarNav } from '@/components/layout/SidebarNav';
import { cn } from '@/lib/utils';

/** Drawer móvil con todos los accesos visibles. Navega y cierra directamente. */
export interface MobileSidebarProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  contadores?: Readonly<Record<string, number>>;
}

export function MobileSidebar({ open, onOpenChange, contadores }: MobileSidebarProps) {
  return (
    <DialogPrimitive.Root open={open} onOpenChange={onOpenChange}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay
          className={cn(
            'fixed inset-0 z-50 bg-[var(--mt-scrim-sheet)]',
            'data-[state=open]:animate-in data-[state=closed]:animate-out',
            'data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0',
          )}
        />
        <DialogPrimitive.Content
          // sr-only title required by Radix for a11y; oculto visualmente.
          className={cn(
            'fixed inset-y-0 left-0 z-50 flex w-menu flex-col',
            'data-[state=open]:animate-in data-[state=closed]:animate-out',
            'data-[state=closed]:slide-out-to-left data-[state=open]:slide-in-from-left',
            'duration-200',
          )}
          data-print="hidden"
          aria-describedby={undefined}
        >
          <DialogPrimitive.Title className="sr-only">Navegación principal</DialogPrimitive.Title>

          <SidebarNav contadores={contadores} onLinkSelect={() => onOpenChange(false)} />

          <DialogPrimitive.Close
            className="absolute right-2 top-2 flex size-11 items-center justify-center rounded-sm text-sidebar-foreground hover:bg-sidebar-active/50 hover:text-white focus-visible:outline-2 focus-visible:outline-brand"
            aria-label="Cerrar navegación"
          >
            <X className="h-4 w-4" />
          </DialogPrimitive.Close>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}
