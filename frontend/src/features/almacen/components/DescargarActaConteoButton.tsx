import { useState } from 'react';
import { FileDown } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { descargarActaConteo } from '@/features/almacen/api/descargarComprobanteSalida';
import { errorUsuario } from '@/lib/error-usuario';

/** M-224: baja el acta en PDF de un conteo ya aplicado. */
export function DescargarActaConteoButton({ conteoId }: { conteoId: string }) {
  const [generando, setGenerando] = useState(false);

  async function descargar() {
    setGenerando(true);
    try {
      await descargarActaConteo(conteoId);
    } catch (e) {
      toast.error('No se pudo generar el acta del conteo.', { description: errorUsuario(e, 'Reintenta en unos segundos.') });
    } finally {
      setGenerando(false);
    }
  }

  return (
    <Button type="button" variant="outline" onClick={descargar} disabled={generando}>
      <FileDown className="mr-2 size-3.5" />
      {generando ? 'Generando PDF…' : 'Descargar acta'}
    </Button>
  );
}
