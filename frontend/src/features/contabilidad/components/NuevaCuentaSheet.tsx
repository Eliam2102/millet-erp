import { SheetBody } from '@/components/ui/sheet';
import { useRef } from 'react';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { CuentaForm } from './CuentaForm';

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/** Sheet "Nueva cuenta" (patrón P4): confirm al cerrar con cambios sin guardar; al guardar cierra sin confirm. */
export function NuevaCuentaSheet({ open, onOpenChange }: Props) {
  const dirty = useRef(false);

  function cerrar(force = false) {
    if (
      !force &&
      dirty.current &&
      !window.confirm('Tienes una cuenta sin guardar. ¿Descartar y cerrar?')
    )
      return;
    dirty.current = false;
    onOpenChange(false);
  }

  return (
    <Sheet open={open} onOpenChange={(o) => (o ? onOpenChange(true) : cerrar())}>
      <SheetContent size="lg" side="right" data-print="hidden">
        <SheetHeader>
          <SheetTitle>Nueva cuenta</SheetTitle>
          <SheetDescription>
            Naturaleza y tipo pueden quedar «Pendiente de validación»: la cuenta no recibirá
            movimientos hasta que se confirmen.
          </SheetDescription>
        </SheetHeader>
        <SheetBody className="space-y-4">
          <div className="min-w-0">
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
        </SheetBody>
      </SheetContent>
    </Sheet>
  );
}
