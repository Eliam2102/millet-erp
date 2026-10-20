import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import type { Poliza } from '../../api/polizas-types';
import { PolizaForm, type ReclasificaA } from './PolizaForm';

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Borrador a editar; sin él es «Nueva póliza». */
  poliza?: Poliza;
  onGuardada: (p: Poliza) => void;
  /** K10.4: «Nueva póliza» que reclasifica a una autorizada (queda ligada). */
  reclasifica?: ReclasificaA;
}

/** Sheet «Nueva póliza» / «Editar póliza» (patrón P4): confirma al cerrar con cambios; al guardar cierra sin confirmar. */
export function PolizaSheet({ open, onOpenChange, poliza, onGuardada, reclasifica }: Props) {
  const {
    setDirty: onDirtyChange,
    solicitarCierre: cerrar,
    dialogo,
  } = useConfirmarDescarte(() => onOpenChange(false));

  return (
    <>
      <Sheet open={open} onOpenChange={(o) => (o ? onOpenChange(true) : cerrar())}>
        <SheetContent size="lg" side="right" data-print="hidden">
          <SheetHeader>
            <SheetTitle className="text-xl">{poliza ? 'Editar póliza' : 'Nueva póliza'}</SheetTitle>
            <SheetDescription className="text-xs text-ink-muted">
              Se guarda como borrador sin folio. El folio (tipo + consecutivo, p. ej. TT-000123) se
              asigna al autorizar.
            </SheetDescription>
          </SheetHeader>

          <div className="flex min-h-0 flex-1 flex-col">
            {open && (
              <PolizaForm
                poliza={poliza}
                reclasifica={reclasifica}
                onGuardada={(p) => {
                  cerrar(true);
                  onGuardada(p);
                }}
                onCancelar={() => cerrar()}
                onDirtyChange={onDirtyChange}
              />
            )}
          </div>
        </SheetContent>
      </Sheet>
      {dialogo}
    </>
  );
}
