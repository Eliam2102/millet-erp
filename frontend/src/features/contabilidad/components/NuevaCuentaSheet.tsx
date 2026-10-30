import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
import { SheetBody } from '@/components/ui/sheet';
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
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(() => onOpenChange(false));

  return (
    <Sheet open={open} onOpenChange={(o) => (o ? onOpenChange(true) : solicitarCierre())}>
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
                onGuardada={() => solicitarCierre(true)}
                onCancelar={() => solicitarCierre()}
                onDirtyChange={setDirty}
              />
            )}
          </div>
        </SheetBody>
      </SheetContent>
      {dialogo}
    </Sheet>
  );
}
