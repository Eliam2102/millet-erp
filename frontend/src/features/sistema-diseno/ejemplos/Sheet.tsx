import {
  Sheet,
  SheetTrigger,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetDescription,
  SheetBody,
  SheetFooter,
  SheetClose,
} from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';

export default function Ejemplo() {
  return (
    <div className="flex flex-wrap gap-3">
      {(['form', 'md', 'lg', 'wide', 'legacy'] as const).map((size) => (
        <Sheet key={size}>
          <SheetTrigger asChild>
            <Button variant="secondary">Abrir {size}</Button>
          </SheetTrigger>
          <SheetContent size={size}>
            <SheetHeader>
              <SheetTitle>Ejemplo {size}</SheetTitle>
              <SheetDescription>Datos ficticios; encabezado y pie fijos.</SheetDescription>
            </SheetHeader>
            <SheetBody>
              {Array.from({ length: 20 }, (_, i) => (
                <p key={i} className="py-3">
                  Partida ficticia {i + 1}
                </p>
              ))}
            </SheetBody>
            <SheetFooter>
              <SheetClose asChild>
                <Button variant="secondary">Cerrar ejemplo</Button>
              </SheetClose>
            </SheetFooter>
          </SheetContent>
        </Sheet>
      ))}
    </div>
  );
}
