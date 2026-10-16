import { useCallback, useRef } from 'react';
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet';
import type { Poliza } from '../../api/polizas-types';
import { PolizaForm } from './PolizaForm';

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Borrador a editar; sin él es «Nueva póliza». */
  poliza?: Poliza;
  onGuardada: (p: Poliza) => void;
}

/** Sheet «Nueva póliza» / «Editar póliza» (patrón P4): confirma al cerrar con cambios; al guardar cierra sin confirmar. */
export function PolizaSheet({ open, onOpenChange, poliza, onGuardada }: Props) {
  const dirty = useRef(false);
  const onDirtyChange = useCallback((d: boolean) => { dirty.current = d; }, []);

  function cerrar(force = false) {
    if (!force && dirty.current && !window.confirm('Tienes cambios sin guardar en la póliza. ¿Descartar y cerrar?')) return;
    dirty.current = false;
    onOpenChange(false);
  }

  return (
    <Sheet open={open} onOpenChange={(o) => (o ? onOpenChange(true) : cerrar())}>
      <SheetContent side="right" className="w-full overflow-y-auto sm:max-w-4xl" data-print="hidden">
        <SheetHeader>
          <SheetTitle className="text-xl">{poliza ? 'Editar póliza' : 'Nueva póliza'}</SheetTitle>
          <SheetDescription className="text-xs text-ink-muted">
            Se guarda como borrador sin folio. El folio (tipo + consecutivo, p. ej. TT-000123) se asigna al autorizar.
          </SheetDescription>
        </SheetHeader>
        <div className="px-4 pb-0">
          {open && (
            <PolizaForm
              poliza={poliza}
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
