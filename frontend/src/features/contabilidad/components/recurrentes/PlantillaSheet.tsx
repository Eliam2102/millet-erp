import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
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
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(() => onOpenChange(false));

  return (
    <Sheet open={open} onOpenChange={(o) => (o ? onOpenChange(true) : solicitarCierre())}>
      <SheetContent size="lg" side="right" data-print="hidden">
        <SheetHeader>
          <SheetTitle className="text-xl">
            {plantilla ? 'Editar plantilla' : 'Nueva plantilla recurrente'}
          </SheetTitle>
          <SheetDescription className="text-xs text-ink-muted">
            Se guarda en borrador. Otro usuario con permiso la activa; desde entonces el sistema
            genera la póliza de cada mes en su día.
          </SheetDescription>
        </SheetHeader>

        <div className="flex min-h-0 flex-1 flex-col">
          {open && (
            <PlantillaForm
              plantilla={plantilla}
              onGuardada={(p) => {
                solicitarCierre(true);
                onGuardada(p);
              }}
              onCancelar={() => solicitarCierre()}
              onDirtyChange={setDirty}
            />
          )}
        </div>
      </SheetContent>
      {dialogo}
    </Sheet>
  );
}
