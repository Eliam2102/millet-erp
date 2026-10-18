import { useCallback, useRef } from 'react';
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet';
import type { PlantillaRecurrente } from '../../api/recurrentes-types';
import { PlantillaForm } from './PlantillaForm';

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Plantilla en borrador a editar; sin ella es «Nueva plantilla». */
  plantilla?: PlantillaRecurrente;
  onGuardada: (p: PlantillaRecurrente) => void;
}

/** Sheet «Nueva plantilla» / «Editar plantilla» (patrón P4): confirma al cerrar con cambios; al guardar cierra sin confirmar. */
export function PlantillaSheet({ open, onOpenChange, plantilla, onGuardada }: Props) {
  const dirty = useRef(false);
  const onDirtyChange = useCallback((d: boolean) => { dirty.current = d; }, []);

  function cerrar(force = false) {
    if (!force && dirty.current && !window.confirm('Tienes cambios sin guardar en la plantilla. ¿Descartar y cerrar?')) return;
    dirty.current = false;
    onOpenChange(false);
  }

  return (
    <Sheet open={open} onOpenChange={(o) => (o ? onOpenChange(true) : cerrar())}>
      <SheetContent side="right" className="w-full overflow-y-auto sm:max-w-4xl" data-print="hidden">
        <SheetHeader>
          <SheetTitle className="text-xl">{plantilla ? 'Editar plantilla' : 'Nueva plantilla recurrente'}</SheetTitle>
          <SheetDescription className="text-xs text-ink-muted">
            Se guarda en borrador. Otro usuario con permiso la activa; desde entonces el sistema genera la póliza de cada mes en su día.
          </SheetDescription>
        </SheetHeader>
        <div className="px-4 pb-0">
          {open && (
            <PlantillaForm
              plantilla={plantilla}
              onGuardada={(p) => { cerrar(true); onGuardada(p); }}
              onCancelar={() => cerrar()}
              onDirtyChange={onDirtyChange}
            />
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
