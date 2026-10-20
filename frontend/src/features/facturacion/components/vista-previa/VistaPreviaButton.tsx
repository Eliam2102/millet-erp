import { Button } from '@/components/ui/button';
import { useState } from 'react';
import { toast } from 'sonner';
import type { BorradorCfdi } from './modelo';

export function VistaPreviaButton({
  obtenerDatos,
  motivo,
}: {
  obtenerDatos: () => BorradorCfdi;
  motivo?: string;
}) {
  const [generando, setGenerando] = useState(false);
  async function descargar() {
    const datos = obtenerDatos();
    setGenerando(true);
    try {
      const { descargarBorradorCfdi } =
        await import('@/components/erp/reportes/exporters/borrador-cfdi-exporter');
      await descargarBorradorCfdi(datos);
    } catch {
      toast.error('No se pudo generar el borrador. Vuelve a intentar la descarga.');
    } finally {
      setGenerando(false);
    }
  }
  return (
    <Button
      type="button"
      variant="secondary"
      disabled={!!motivo || generando}
      title={motivo}
      onClick={() => void descargar()}
    >
      {generando ? 'Generando…' : 'Vista previa'}
    </Button>
  );
}
