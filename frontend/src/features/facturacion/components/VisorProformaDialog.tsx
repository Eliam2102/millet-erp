import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Download } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { ErrorState } from '@/components/erp/feedback/ErrorState';
import type { ProformaPedidoResponse } from '@/features/facturacion/api/types';
import { formatDateTime } from '@/lib/datetime';

/**
 * Visor de la proforma dentro del sistema (U1.8a): muestra el mismo PDF
 * «PROFORMA – SIN VALOR FISCAL» que se descarga, generado en el navegador con
 * @react-pdf (ADR-0036) y abierto en el visor nativo del navegador. «Descargar»
 * sigue disponible desde el pie.
 *
 * <para>El PDF se genera bajo demanda por versión de la proforma y el object URL
 * se revoca al cerrar o al cambiar de proforma (mismo patrón que
 * <c>useContenidoAdjunto</c>).</para>
 */
export function VisorProformaDialog({
  proforma,
  descargando,
  onDescargar,
  onCerrar,
}: {
  proforma: ProformaPedidoResponse | null;
  descargando: boolean;
  onDescargar: (proforma: ProformaPedidoResponse) => void;
  onCerrar: () => void;
}) {
  const pdfUrl = useQuery({
    queryKey: ['proforma-pdf', proforma?.id, proforma?.version] as const,
    enabled: proforma != null,
    staleTime: Infinity,
    gcTime: 0,
    retry: false,
    queryFn: async () => {
      const { generarProformaPdfBlob } =
        await import('@/components/erp/reportes/exporters/proforma-cfdi-exporter');
      return URL.createObjectURL(await generarProformaPdfBlob(proforma as ProformaPedidoResponse));
    },
  });

  const url = pdfUrl.data;
  useEffect(() => {
    if (url == null) return;
    return () => URL.revokeObjectURL(url);
  }, [url]);

  return (
    <Dialog
      open={proforma != null}
      onOpenChange={(abierto) => {
        if (!abierto) onCerrar();
      }}
    >
      <DialogContent
        size="lg"
        closeLabel="Cerrar vista previa"
        className="rounded-2xl shadow-dialog"
      >
        {proforma != null && (
          <>
            <DialogHeader>
              <DialogTitle className="text-xl">
                Proforma <span className="font-mono">{proforma.folio}</span>
              </DialogTitle>
              <DialogDescription className="text-xs text-ink-muted">
                Sin valor fiscal · Generada {formatDateTime(proforma.generadaEn)}
                {proforma.generadaPor ? ` por ${proforma.generadaPor}` : ''}
              </DialogDescription>
            </DialogHeader>
            <DialogBody className="flex">
              {pdfUrl.isError ? (
                <ErrorState
                  title="No se pudo mostrar la proforma"
                  onRetry={() => void pdfUrl.refetch()}
                  className="flex-1"
                />
              ) : url == null ? (
                <div
                  role="status"
                  aria-label="Generando la vista previa de la proforma"
                  className="h-[70vh] flex-1 animate-pulse rounded-lg bg-surface-muted"
                />
              ) : (
                <iframe
                  title={`Vista previa de la proforma ${proforma.folio}`}
                  src={url}
                  className="h-[70vh] flex-1 rounded-lg bg-surface-card shadow-card-flat"
                />
              )}
            </DialogBody>
            <DialogFooter className="border-t border-line pt-4 sm:justify-between">
              <Button type="button" variant="ghost" onClick={onCerrar}>
                Cerrar
              </Button>
              <Button type="button" disabled={descargando} onClick={() => onDescargar(proforma)}>
                <Download className="mr-1.5 h-3.5 w-3.5" strokeWidth={1.8} />
                {descargando ? 'Generando…' : 'Descargar'}
              </Button>
            </DialogFooter>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}
