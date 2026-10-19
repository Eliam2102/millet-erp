import type { ReactNode } from 'react';
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip';

/**
 * Explica por qué una acción está deshabilitada. Un `<button disabled>` no
 * recibe foco ni eventos, así que el motivo se ancla a un `span tabIndex=0`
 * (alcanzable con teclado y táctil) en lugar de un `title`. Sin motivo no
 * envuelve nada.
 */
export function ConMotivo({
  motivo,
  children,
}: {
  motivo: string | null | undefined;
  children: ReactNode;
}) {
  if (!motivo) return <>{children}</>;
  return (
    <TooltipProvider>
      <Tooltip>
        <TooltipTrigger asChild>
          <span tabIndex={0} className="inline-flex rounded-md">
            {children}
          </span>
        </TooltipTrigger>
        <TooltipContent>{motivo}</TooltipContent>
      </Tooltip>
    </TooltipProvider>
  );
}
