import * as React from 'react';
import * as DialogPrimitive from '@radix-ui/react-dialog';
import { X } from 'lucide-react';
import { cn } from '@/lib/utils';

/**
 * Primitive <c>Sheet</c> — Radix Dialog estilizado como slide-from-side
 * panel. Para forms grandes (Nueva requisición) que se benefician del
 * espacio vertical sin perder el contexto de la bandeja detrás. Se
 * crea manualmente porque no está en el set de shadcn instalado.
 *
 * <para>Uso:</para>
 * <code>
 * &lt;Sheet open={open} onOpenChange={setOpen}&gt;
 *   &lt;SheetContent side="right"&gt;
 *     &lt;SheetHeader&gt;
 *       &lt;SheetTitle&gt;Título&lt;/SheetTitle&gt;
 *     &lt;/SheetHeader&gt;
 *     ...contenido...
 *   &lt;/SheetContent&gt;
 * &lt;/Sheet&gt;
 * </code>
 */

const Sheet = DialogPrimitive.Root;

const SheetTrigger = DialogPrimitive.Trigger;

const SheetClose = DialogPrimitive.Close;

const SheetPortal = DialogPrimitive.Portal;

const SheetOverlay = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Overlay>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Overlay>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Overlay
    ref={ref}
    className={cn(
      'fixed inset-0 z-50 bg-black/60 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0',
      className,
    )}
    {...props}
  />
));
SheetOverlay.displayName = DialogPrimitive.Overlay.displayName;

const sideClasses = {
  right:
    'inset-y-0 right-0 h-full border-l data-[state=closed]:slide-out-to-right data-[state=open]:slide-in-from-right',
  left:
    'inset-y-0 left-0 h-full border-r data-[state=closed]:slide-out-to-left data-[state=open]:slide-in-from-left',
  top:
    'inset-x-0 top-0 border-b data-[state=closed]:slide-out-to-top data-[state=open]:slide-in-from-top',
  bottom:
    'inset-x-0 bottom-0 border-t data-[state=closed]:slide-out-to-bottom data-[state=open]:slide-in-from-bottom',
} as const;

interface SheetContentProps
  extends React.ComponentPropsWithoutRef<typeof DialogPrimitive.Content> {
  /** De qué lado entra el panel. Default: <c>right</c>. */
  side?: keyof typeof sideClasses;
}

const SheetContent = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Content>,
  SheetContentProps
>(({ side = 'right', className, children, ...props }, ref) => (
  <SheetPortal>
    <SheetOverlay />
    <DialogPrimitive.Content
      ref={ref}
      className={cn(
        'fixed z-50 flex flex-col gap-4 bg-background shadow-lg transition-transform duration-300',
        'data-[state=open]:animate-in data-[state=closed]:animate-out',
        sideClasses[side],
        // Tamaño por default — el caller puede overridarlo con className.
        side === 'right' || side === 'left'
          ? 'w-full sm:max-w-3xl'
          : 'h-auto',
        className,
      )}
      {...props}
    >
      {children}
      <DialogPrimitive.Close
        className="absolute right-4 top-4 rounded-sm opacity-70 ring-offset-background transition-opacity hover:opacity-100 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
        aria-label="Cerrar"
      >
        <X className="h-4 w-4" />
      </DialogPrimitive.Close>
    </DialogPrimitive.Content>
  </SheetPortal>
));
SheetContent.displayName = DialogPrimitive.Content.displayName;

function SheetHeader({
  className,
  ...props
}: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn(
        'flex flex-col gap-1.5 border-b px-6 py-4 text-left',
        className,
      )}
      {...props}
    />
  );
}

function SheetFooter({
  className,
  ...props
}: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn(
        'flex flex-col-reverse gap-2 border-t px-6 py-4 sm:flex-row sm:justify-end',
        className,
      )}
      {...props}
    />
  );
}

const SheetTitle = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Title>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Title>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Title
    ref={ref}
    className={cn('text-lg font-semibold text-foreground', className)}
    {...props}
  />
));
SheetTitle.displayName = DialogPrimitive.Title.displayName;

const SheetDescription = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Description>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Description>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Description
    ref={ref}
    className={cn('text-sm text-muted-foreground', className)}
    {...props}
  />
));
SheetDescription.displayName = DialogPrimitive.Description.displayName;

export {
  Sheet,
  SheetClose,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
};
