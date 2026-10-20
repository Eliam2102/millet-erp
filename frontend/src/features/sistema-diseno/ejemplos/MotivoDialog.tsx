import { MotivoDialog } from '@/components/erp/feedback/MotivoDialog';
import { Button } from '@/components/ui/button';
import { useState } from 'react';

export default function Ejemplo() {
  const [open, setOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);
  return (
    <>
      <Button variant="secondary" onClick={() => setOpen(true)}>
        Probar motivo
      </Button>
      {open && (
        <MotivoDialog
          titulo="Rechazar ejemplo"
          descripcion="Escribe al menos 5 caracteres. Escribe error para simular un fallo."
          etiqueta="Motivo *"
          min={5}
          max={100}
          verbo="Rechazar ejemplo"
          error={error}
          onCerrar={() => {
            setOpen(false);
            setError(null);
          }}
          onConfirmar={async (motivo) => {
            if (motivo === 'error') {
              setError('El ejemplo simula un fallo. Cambia el motivo para continuar.');
              return;
            }
            setOpen(false);
            setError(null);
          }}
        />
      )}
    </>
  );
}
