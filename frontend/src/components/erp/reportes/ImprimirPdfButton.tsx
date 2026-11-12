import { useRef, useState, type ComponentProps } from 'react';
import { Loader2, Printer } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { abrirPdfParaImprimir } from './imprimir-pdf';
import type { ReporteParaExportar } from './exporters/ReporteDocument';

type Props = Omit<ComponentProps<typeof Button>, 'onClick' | 'asChild'> & {
  generarPdf: () => Promise<Blob>;
};

export function ImprimirPdfButton({
  generarPdf,
  children = 'Imprimir',
  disabled,
  ...props
}: Props) {
  const [preparando, setPreparando] = useState(false);
  const enCurso = useRef(false);
  async function imprimir() {
    if (enCurso.current) return;
    enCurso.current = true;
    setPreparando(true);
    try {
      await abrirPdfParaImprimir(generarPdf);
    } catch (error) {
      toast.error('No se pudo preparar la impresión', {
        description: error instanceof Error ? error.message : String(error),
      });
    } finally {
      enCurso.current = false;
      setPreparando(false);
    }
  }
  return (
    <Button
      type="button"
      variant="outline"
      size="sm"
      {...props}
      data-print="hidden"
      disabled={disabled || preparando}
      title={
        preparando
          ? 'Preparando el documento PDF.'
          : (props.title ?? 'Abrir PDF para ajustar e imprimir.')
      }
      onClick={() => void imprimir()}
    >
      {preparando ? (
        <Loader2 className="mr-1.5 size-4 animate-spin" aria-hidden="true" />
      ) : (
        <Printer className="mr-1.5 size-4" aria-hidden="true" />
      )}
      {preparando ? 'Preparando…' : children}
    </Button>
  );
}

export function ImprimirReporteButton<TFila>({
  reporte,
  obtenerCelda,
  ...props
}: Omit<Props, 'generarPdf'> & {
  reporte: ReporteParaExportar<TFila>;
  obtenerCelda?: (fila: TFila, clave: string) => unknown;
}) {
  return (
    <ImprimirPdfButton
      {...props}
      generarPdf={async () => {
        const { generarReportePdfBlob } = await import('./exporters/reporte-pdf-exporter');
        return generarReportePdfBlob(reporte, obtenerCelda);
      }}
    />
  );
}
