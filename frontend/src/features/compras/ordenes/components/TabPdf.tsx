import { useEffect } from 'react';
import { Download, FileText, Lock } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ErrorState, EmptyState } from '@/components/erp';
import { usePdfOrdenCompra, useDescargarPdfOrdenCompra } from '@/features/compras/ordenes/api/usePdfOrdenCompra';
import {
  EstadoOrdenCompra,
  type OrdenCompraDetalleResponse,
} from '@/features/compras/ordenes/api/types';
import { toast } from 'sonner';
import { DateTimeDisplay } from '@/components/erp';
import { esApiError } from '@/lib/api';

/**
 * <c>&lt;TabPdf/&gt;</c> — preview del PDF de la OC autorizada
 * (UF6-PR1, FOC5). Embed nativo sin pdf.js (decisión cerrada FOC5):
 * el browser maneja el render, sin dependencia extra.
 *
 * <para><b>Estados</b>:</para>
 * <list>
 *   <item>OC NO autorizada → banner "PDF disponible después de la
 *   autorización Nivel 2" — no se hace fetch del backend.</item>
 *   <item>OC autorizada + PDF cargando → loading.</item>
 *   <item>OC autorizada + PDF listo → \&lt;embed\&gt; + botón
 *   "Descargar PDF".</item>
 *   <item>OC autorizada + 404 → mensaje informativo (puede pasar si
 *   el job de PDF falló o aún no corrió).</item>
 * </list>
 *
 * <para>El object URL del blob se revoca en cleanup para evitar memory
 * leak; la consulta elimina del caché la URL al quedar sin observadores.</para>
 */
export interface TabPdfProps {
  oc: OrdenCompraDetalleResponse;
}

export function TabPdf({ oc }: TabPdfProps) {
  const isAutorizada =
    oc.estado === EstadoOrdenCompra.Autorizada || oc.estado === EstadoOrdenCompra.Cerrada;

  const query = usePdfOrdenCompra(oc.id, { enabled: isAutorizada, cicloAutorizacion: oc.cicloAutorizacion });
  const descargar = useDescargarPdfOrdenCompra();

  // Cleanup del object URL al desmontar o al cambiar el blob.
  useEffect(() => {
    const url = query.data;
    if (url == null) return;
    return () => {
      URL.revokeObjectURL(url);
    };
  }, [query.data]);

  if (!isAutorizada) {
    return (
      <EmptyState
        icon={<Lock className="h-10 w-10" />}
        title="PDF disponible después de la autorización Nivel 2"
        description="El PDF se genera automáticamente al autorizar la OC en Nivel 2. Mientras la OC esté en Borrador, En autorización o Rechazada, este tab muestra este aviso."
      />
    );
  }

  if (query.isLoading) {
    return (
      <div
        className="flex h-96 items-center justify-center rounded-md border bg-surface-muted/30 text-sm text-ink-muted"
        data-component="tab-pdf-loading"
      >
        Cargando PDF…
      </div>
    );
  }

  if (query.isError) {
    const apiErr = esApiError(query.error) ? query.error : null;
    if (apiErr?.status === 404) {
      return (
        <EmptyState
          icon={<FileText className="h-10 w-10" />}
          title="PDF aún no disponible"
          description="La OC está autorizada pero el PDF no se ha generado todavía. Reintenta en unos segundos."
          action={
            <Button onClick={() => query.refetch()} size="sm" variant="outline">
              Reintentar
            </Button>
          }
        />
      );
    }
    return (
      <ErrorState
        title="No se pudo cargar el PDF"
        problem={apiErr?.problem}
        onRetry={() => {
          void query.refetch();
        }}
      />
    );
  }

  const blobUrl = query.data;
  if (!blobUrl) {
    return null;
  }

  return (
    <div className="space-y-3" data-component="tab-pdf">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold tracking-tight">PDF de la orden {oc.folio}</h3>
        <div className="flex items-center gap-3">
          {(oc.impresiones ?? 0) > 0 && <p className="text-xs text-ink-muted">
            Impreso {oc.impresiones} veces · última: <DateTimeDisplay value={oc.ultimaImpresionEn} /> · {oc.ultimaImpresionPorNombre ?? 'Usuario sin nombre'}
          </p>}
          <Button size="sm" variant="outline" disabled={descargar.isPending} onClick={() => descargar.mutate({ id: oc.id, folio: oc.folio }, {
            onError: err => toast.error(esApiError(err) ? err.problem.detail ?? err.problem.title : 'No se pudo descargar el PDF. Vuelve a intentar la descarga.'),
          })} data-action="descargar-pdf">
            <Download className="mr-1 h-3.5 w-3.5" />
            {descargar.isPending ? 'Descargando…' : 'Descargar PDF'}
          </Button>
        </div>
      </div>
      <div
        className="overflow-hidden rounded-md border bg-surface-muted"
        style={{ height: 'min(80vh, 900px)' }}
      >
        <embed
          src={`${blobUrl}#toolbar=0&navpanes=0`}
          type="application/pdf"
          width="100%"
          height="100%"
          aria-label={`PDF de la OC ${oc.folio}`}
        />
      </div>
    </div>
  );
}
