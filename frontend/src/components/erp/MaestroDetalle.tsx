import { useId, useState, type ReactNode } from 'react';
import { PanelLeft } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

/** Mantiene lista y captura montadas al recuperar espacio para el detalle. */
export function MaestroDetalle({
  seleccionado,
  etiquetaLista,
  lista,
  children,
}: {
  seleccionado: boolean;
  etiquetaLista: string;
  lista: ReactNode;
  children: ReactNode;
}) {
  const [listaVisible, setListaVisible] = useState(false);
  const id = useId();
  return (
    <div className="min-w-0">
      {seleccionado && (
        <Button
          variant="outline"
          className="mb-3 hidden md:inline-flex"
          data-print="hidden"
          aria-expanded={listaVisible}
          aria-controls={id}
          onClick={() => setListaVisible(!listaVisible)}
        >
          <PanelLeft className="size-3.5" aria-hidden="true" />
          {listaVisible ? 'Ocultar' : 'Mostrar'} {etiquetaLista}
        </Button>
      )}
      <div
        id={id}
        className={cn(
          'flex min-h-0 min-w-0 flex-col gap-4 md:flex-row print:block print:h-auto print:[&>section]:h-auto print:[&>section]:overflow-visible [&>aside]:min-h-0 [&>aside]:min-w-0 [&>section]:min-h-0 [&>section]:min-w-0',
          seleccionado ? 'md:h-[calc(100dvh-10.5rem)]' : 'md:h-[calc(100dvh-7.5rem)]',
          seleccionado &&
            (listaVisible
              ? '[&>aside]:hidden md:[&>aside]:flex md:[&>aside]:flex-col'
              : '[&>aside]:hidden'),
        )}
      >
        {lista}
        {children}
      </div>
    </div>
  );
}
