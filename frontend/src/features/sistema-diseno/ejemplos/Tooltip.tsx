import { Tooltip, TooltipProvider, TooltipTrigger, TooltipContent } from '@/components/ui/tooltip';
import { Button } from '@/components/ui/button';

export default function Ejemplo() {
  return (
    <TooltipProvider>
      <Tooltip>
        <TooltipTrigger asChild>
          <Button variant="secondary">Ver ayuda de ejemplo</Button>
        </TooltipTrigger>
        <TooltipContent>Ayuda adicional al pasar el cursor o enfocar.</TooltipContent>
      </Tooltip>
    </TooltipProvider>
  );
}
