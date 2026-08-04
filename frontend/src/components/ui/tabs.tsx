import * as React from 'react';
import * as TabsPrimitive from '@radix-ui/react-tabs';

import { cn } from '@/lib/utils';

/**
 * Tabs (Radix) estilo shadcn. Primer consumidor: el form de emisión de
 * factura con pestañas Encabezado/Posiciones/Totales (FAC-UX-PR2).
 *
 * Nota para forms con react-hook-form: usa `<TabsContent forceMount>` para
 * que los inputs de pestañas inactivas permanezcan montados (Radix los
 * oculta con el atributo `hidden`); sin forceMount, los errores de zod en
 * pestañas no visibles no se renderizan y el usuario no sabe qué corregir.
 */
const Tabs = TabsPrimitive.Root;

const TabsList = React.forwardRef<
  React.ElementRef<typeof TabsPrimitive.List>,
  React.ComponentPropsWithoutRef<typeof TabsPrimitive.List>
>(({ className, ...props }, ref) => (
  <TabsPrimitive.List
    ref={ref}
    className={cn(
      'inline-flex h-9 items-center justify-center rounded-lg bg-muted p-1 text-muted-foreground',
      className,
    )}
    {...props}
  />
));
TabsList.displayName = TabsPrimitive.List.displayName;

const TabsTrigger = React.forwardRef<
  React.ElementRef<typeof TabsPrimitive.Trigger>,
  React.ComponentPropsWithoutRef<typeof TabsPrimitive.Trigger>
>(({ className, ...props }, ref) => (
  <TabsPrimitive.Trigger
    ref={ref}
    className={cn(
      'inline-flex items-center justify-center gap-1.5 whitespace-nowrap rounded-md px-3 py-1 text-sm font-medium ring-offset-background transition-all',
      'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2',
      'disabled:pointer-events-none disabled:opacity-50',
      'data-[state=active]:bg-background data-[state=active]:text-foreground data-[state=active]:shadow',
      className,
    )}
    {...props}
  />
));
TabsTrigger.displayName = TabsPrimitive.Trigger.displayName;

const TabsContent = React.forwardRef<
  React.ElementRef<typeof TabsPrimitive.Content>,
  React.ComponentPropsWithoutRef<typeof TabsPrimitive.Content>
>(({ className, ...props }, ref) => (
  <TabsPrimitive.Content
    ref={ref}
    className={cn(
      // Con `forceMount`, Radix deja el contenido montado Y visible (no
      // aplica el atributo `hidden`); el ocultamiento de pestañas
      // inactivas corre por cuenta del CSS vía data-state. Sin esta
      // clase, las tres pestañas se apilan (bug FAC-UX-PR8). Los
      // consumidores que quieran impresión completa agregan print:!block.
      'mt-4 ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 data-[state=inactive]:hidden',
      className,
    )}
    {...props}
  />
));
TabsContent.displayName = TabsPrimitive.Content.displayName;

export { Tabs, TabsList, TabsTrigger, TabsContent };
