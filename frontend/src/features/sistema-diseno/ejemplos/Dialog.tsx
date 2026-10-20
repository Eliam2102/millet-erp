import {
  Dialog,
  DialogTrigger,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogBody,
  DialogFooter,
  DialogClose,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';

export default function Ejemplo() {
  return (
    <div className="flex flex-wrap gap-3">
      {(['sm', 'md', 'lg'] as const).map((size) => (
        <Dialog key={size}>
          <DialogTrigger asChild>
            <Button variant="secondary">Abrir {size}</Button>
          </DialogTrigger>
          <DialogContent size={size}>
            <DialogHeader>
              <DialogTitle>Ejemplo {size}</DialogTitle>
              <DialogDescription>Datos ficticios; encabezado y pie fijos.</DialogDescription>
            </DialogHeader>
            <DialogBody>
              {Array.from({ length: 20 }, (_, i) => (
                <p key={i} className="py-3">
                  Partida ficticia {i + 1}
                </p>
              ))}
            </DialogBody>
            <DialogFooter>
              <DialogClose asChild>
                <Button variant="secondary">Cerrar ejemplo</Button>
              </DialogClose>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      ))}
    </div>
  );
}
