import {
  CircleCheck,
  Info,
  LoaderCircle,
  OctagonX,
  TriangleAlert,
} from 'lucide-react';
import { Toaster as Sonner } from 'sonner';

type ToasterProps = React.ComponentProps<typeof Sonner>;

/**
 * Wrapper de <c>sonner</c> con los iconos y clases de shadcn. El proyecto
 * usa el tema claro aprobado por DESIGN.md §2; no hay paleta oscura.
 * Por ello se fija <c>theme="light"</c>.
 */
const Toaster = ({ ...props }: ToasterProps) => {
  return (
    <Sonner
      theme="light"
      className="toaster group"
      icons={{
        success: <CircleCheck className="h-4 w-4" />,
        info: <Info className="h-4 w-4" />,
        warning: <TriangleAlert className="h-4 w-4" />,
        error: <OctagonX className="h-4 w-4" />,
        loading: <LoaderCircle className="h-4 w-4 animate-spin" />,
      }}
      toastOptions={{
        classNames: {
          toast:
            'group toast group-[.toaster]:bg-background group-[.toaster]:text-foreground group-[.toaster]:border-border group-[.toaster]:shadow-lg',
          error: 'group-[.toaster]:bg-danger-bg! group-[.toaster]:text-danger-fg!',
          success: 'group-[.toaster]:bg-success-bg! group-[.toaster]:text-success-fg!',
          warning: 'group-[.toaster]:bg-warning-bg! group-[.toaster]:text-warning-fg!',
          description: 'group-[.toast]:text-muted-foreground',
          actionButton:
            'group-[.toast]:bg-primary group-[.toast]:text-primary-foreground',
          cancelButton:
            'group-[.toast]:bg-muted group-[.toast]:text-muted-foreground',
        },
      }}
      {...props}
    />
  );
};

export { Toaster };
