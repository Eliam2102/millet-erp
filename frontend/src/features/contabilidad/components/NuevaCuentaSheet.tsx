import { useRef } from 'react';
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet';
import { CuentaForm } from './CuentaForm';

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/** Sheet "Nueva cuenta" (patrón P4): confirm al cerrar con cambios sin guardar; al guardar cierra sin confirm. */
export function NuevaCuentaSheet({ open, onOpenChange }: Props) {
  const dirty = useRef(false);

  function cerrar(force = false) {
    if (!force && dirty.current && !window.confirm('Tienes una cuenta sin guardar. ¿Descartar y cerrar?')) return;
    dirty.current = false;
    onOpenChange(false);
  }

  return (
    <Sheet open={open} onOpenChange={(o) => (o ? onOpenChange(true) : cerrar())}>
      <SheetContent side="right" className="w-full overflow-y-auto sm:max-w-sheet" data-print="hidden">
        <SheetHeader>
          <SheetTitle>Nueva cuenta</SheetTitle>
          <SheetDescription>
            Naturaleza y tipo pueden quedar «Pendiente de validación»: la cuenta no recibirá movimientos hasta que se confirmen.
          </SheetDescription>
        </SheetHeader>
        <div className="px-4 pb-6">
          {open && (
            <CuentaForm
              onGuardada={() => cerrar(true)}
              onCancelar={() => cerrar()}
              onDirtyChange={(d) => {
                dirty.current = d;
              }}
            />
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
