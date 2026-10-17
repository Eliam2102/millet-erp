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

type TabsVariant = 'linea' | 'pill';
const TabsVariantContext = React.createContext<TabsVariant>('linea');

/**
 * `variant="linea"` (default): subrayado DS 4.4.1 (`shadow-tab-active`).
 * `variant="pill"`: estilo anterior de píldora, para pantallas que lo necesiten.
 */
const TabsList = React.forwardRef<
  React.ElementRef<typeof TabsPrimitive.List>,
  React.ComponentPropsWithoutRef<typeof TabsPrimitive.List> & { variant?: TabsVariant }
>(({ className, variant = 'linea', ...props }, ref) => (
  <TabsVariantContext.Provider value={variant}>
    <TabsPrimitive.List
      ref={ref}
      className={cn(
        variant === 'pill'
          ? 'inline-flex h-9 items-center justify-center rounded-lg bg-muted p-1 text-muted-foreground'
          : 'flex flex-wrap gap-1 border-b border-line',
        className,
      )}
      {...props}
    />
  </TabsVariantContext.Provider>
));
TabsList.displayName = TabsPrimitive.List.displayName;

const TabsTrigger = React.forwardRef<
  React.ElementRef<typeof TabsPrimitive.Trigger>,
  React.ComponentPropsWithoutRef<typeof TabsPrimitive.Trigger> & { count?: number }
>(({ className, count, children, ...props }, ref) => {
  const variant = React.useContext(TabsVariantContext);
  return (
    <TabsPrimitive.Trigger
      ref={ref}
      className={cn(
        'inline-flex items-center justify-center gap-1.5 whitespace-nowrap text-sm font-medium transition-colors',
        'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2',
        'disabled:pointer-events-none disabled:opacity-50',
        variant === 'pill'
          ? 'rounded-md px-3 py-1 ring-offset-background data-[state=active]:bg-background data-[state=active]:text-foreground data-[state=active]:shadow'
          : 'px-3 py-2 text-ink-muted hover:text-ink data-[state=active]:text-ink data-[state=active]:shadow-tab-active',
        className,
      )}
      {...props}
    >
      {children}
      {count !== undefined && (
        <span className="text-2xs tabular-nums text-ink-muted">{count}</span>
      )}
    </TabsPrimitive.Trigger>
  );
});
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
