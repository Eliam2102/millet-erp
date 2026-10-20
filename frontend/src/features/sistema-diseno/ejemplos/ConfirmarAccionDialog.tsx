import { ConfirmarAccionDialog } from '@/components/erp/feedback/ConfirmarAccionDialog';
import { Button } from '@/components/ui/button';
import { useState } from 'react';

export default function Ejemplo() {
  const [open, setOpen] = useState(false);
  return (
    <>
      <Button variant="secondary" onClick={() => setOpen(true)}>
        Probar confirmación
      </Button>
      <ConfirmarAccionDialog
        open={open}
        onOpenChange={setOpen}
        titulo="Eliminar ejemplo"
        resumen="Sólo se cierra esta demostración; no hay registros reales."
        verbo="Eliminar ejemplo"
        tono="peligro"
        onConfirmar={() => setOpen(false)}
      />
    </>
  );
}
