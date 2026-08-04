import { useState } from 'react';
import { Download, FileText } from 'lucide-react';
import { toast } from 'sonner';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { esApiError } from '@/lib/api';
import {
  useCfdiDetalle,
  useCfdiParseado,
  useCfdiPdfUrl,
} from '@/features/cxp/api/useCfdis';
import {
  CanalOrigenCfdiLabels,
  TipoCfdiLabels,
  type CfdiListItem,
} from '@/features/cxp/api/types';
import { EstadoCfdiBadge } from '@/features/cxp/components/EstadoCfdiBadge';
import { descargarXmlCfdi } from '@/features/cxp/lib/cfdi-archivos';

/**
 * <c>&lt;CfdiDetalleSheet/&gt;</c> — detalle de un CFDI recibido con el
 * XML renderizado en formato legible (encabezado + conceptos, desde
 * <c>GET /cfdis/{id}/parseado</c>) y preview inline del PDF
 * (<c>GET /cfdis/{id}/pdf</c> vía object URL). Cierra la parte FE de los
 * PLATFORM-TODO(&lt;CfdiBlobDownload&gt;)/(&lt;CfdiXmlViewer&gt;) —
 * viewer legible, no XML crudo (05-frontend-diseno §7.2); el XML crudo
 * queda disponible con el botón de descarga.
 */
export interface CfdiDetalleSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  cfdi: CfdiListItem | null;
}

export function CfdiDetalleSheet({
  open,
  onOpenChange,
  cfdi,
}: CfdiDetalleSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="w-full max-w-3xl overflow-y-auto sm:max-w-3xl">
        <SheetHeader>
          <SheetTitle>Detalle del CFDI</SheetTitle>
          <SheetDescription className="font-mono text-xs">
            {cfdi?.uuidCfdi ?? ''}
          </SheetDescription>
        </SheetHeader>
        {open && cfdi && <Detalle cfdiId={cfdi.id} />}
      </SheetContent>
    </Sheet>
  );
}

function Detalle({ cfdiId }: { cfdiId: string }) {
  const detalleQuery = useCfdiDetalle(cfdiId);
  const parseadoQuery = useCfdiParseado(cfdiId);
  const [mostrarPdf, setMostrarPdf] = useState(false);
  const pdfQuery = useCfdiPdfUrl(cfdiId, mostrarPdf);
  const [descargando, setDescargando] = useState(false);

  const d = detalleQuery.data;

  async function handleDescargarXml() {
    if (!d) return;
    setDescargando(true);
    try {
      await descargarXmlCfdi(d.id, d.uuidCfdi);
    } catch (error) {
      toast.error(
        esApiError(error) ? error.problem.title : 'Error al descargar el XML.',
      );
    } finally {
      setDescargando(false);
    }
  }

  if (detalleQuery.isLoading) {
    return (
      <p className="px-4 text-sm text-muted-foreground">Cargando detalle…</p>
    );
  }
  if (!d) {
    return (
      <p className="px-4 text-sm text-destructive">
        No se pudo cargar el detalle del CFDI.
      </p>
    );
  }

  return (
    <div className="space-y-4 px-4 pb-6">
      <section className="grid grid-cols-2 gap-x-6 gap-y-2 rounded-md border p-4 text-sm md:grid-cols-3">
        <Metadato label="RFC emisor" valor={d.rfcEmisor} mono />
        <Metadato label="Tipo" valor={TipoCfdiLabels[d.tipo]} />
        <Metadato
          label="Serie / Folio"
          valor={d.serie ? `${d.serie}-${d.folio ?? ''}` : (d.folio ?? '—')}
          mono
        />
        <Metadato label="Fecha CFDI" valor={d.fechaCfdi.slice(0, 10)} />
        <Metadato label="Recepción" valor={d.fechaRecepcion.slice(0, 10)} />
        <Metadato label="Canal" valor={CanalOrigenCfdiLabels[d.canalOrigen]} />
        <Metadato label="Subtotal" valor={monto(d.subtotal, d.moneda)} mono />
        <Metadato
          label="IVA trasladado"
          valor={monto(d.impuestosTrasladados, d.moneda)}
          mono
        />
        <Metadato
          label="Retenciones"
          valor={monto(d.retenciones, d.moneda)}
          mono
        />
        <Metadato label="Total" valor={monto(d.total, d.moneda)} mono />
        <Metadato
          label="Tipo de cambio"
          valor={d.tipoCambio != null ? d.tipoCambio.toFixed(4) : '—'}
        />
        <div>
          <dt className="text-xs text-muted-foreground">Estado</dt>
          <dd className="mt-0.5">
            <EstadoCfdiBadge estado={d.estado} />
          </dd>
        </div>
        {d.motivoDescarte && (
          <Metadato label="Motivo de descarte" valor={d.motivoDescarte} />
        )}
      </section>

      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={handleDescargarXml}
          disabled={!d.tieneXml || descargando}
        >
          <Download className="mr-1 h-3 w-3" />
          {descargando ? 'Descargando…' : 'Descargar XML'}
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => setMostrarPdf((v) => !v)}
          disabled={!d.tienePdf}
        >
          <FileText className="mr-1 h-3 w-3" />
          {mostrarPdf ? 'Ocultar PDF' : 'Ver PDF'}
        </Button>
        {!d.tienePdf && (
          <span className="text-xs text-muted-foreground">
            Este CFDI no trae representación impresa.
          </span>
        )}
      </div>

      {mostrarPdf &&
        (pdfQuery.isLoading ? (
          <p className="text-sm text-muted-foreground">Cargando PDF…</p>
        ) : pdfQuery.data ? (
          <embed
            src={pdfQuery.data}
            type="application/pdf"
            className="h-[28rem] w-full rounded-md border"
            aria-label="Representación impresa del CFDI"
          />
        ) : (
          <p className="text-sm text-destructive">
            No se pudo cargar el PDF.
          </p>
        ))}

      <section className="space-y-2">
        <h3 className="text-sm font-medium">
          Conceptos{' '}
          {parseadoQuery.data
            ? `(${parseadoQuery.data.lineas.length})`
            : ''}
        </h3>
        {parseadoQuery.isLoading ? (
          <p className="text-sm text-muted-foreground">Leyendo el XML…</p>
        ) : parseadoQuery.isError ? (
          <p className="rounded-md border border-dashed px-3 py-3 text-xs text-muted-foreground">
            No se pudo leer el XML del CFDI
            {esApiError(parseadoQuery.error)
              ? ` (${parseadoQuery.error.problem.title})`
              : ''}
            . Los importes del encabezado provienen de la ingesta.
          </p>
        ) : (
          <div className="overflow-x-auto rounded-md border">
            <table className="w-full text-sm">
              <thead className="bg-muted/50">
                <tr>
                  <th className="px-3 py-2 text-left">#</th>
                  <th className="px-3 py-2 text-left">ClaveProdServ</th>
                  <th className="px-3 py-2 text-left">Descripción</th>
                  <th className="px-3 py-2 text-right">Cantidad</th>
                  <th className="px-3 py-2 text-left">Unidad</th>
                  <th className="px-3 py-2 text-right">P. unitario</th>
                  <th className="px-3 py-2 text-right">Importe</th>
                </tr>
              </thead>
              <tbody>
                {parseadoQuery.data?.lineas.map((l) => (
                  <tr key={l.posicion} className="border-t">
                    <td className="px-3 py-2 text-xs text-muted-foreground">
                      {l.posicion}
                    </td>
                    <td className="px-3 py-2 font-mono text-xs">
                      {l.claveProdServ}
                    </td>
                    <td className="max-w-72 truncate px-3 py-2">
                      {l.descripcion}
                    </td>
                    <td className="px-3 py-2 text-right font-mono">
                      {l.cantidad}
                    </td>
                    <td className="px-3 py-2 text-xs">
                      {l.unidad ?? l.claveUnidad}
                    </td>
                    <td className="px-3 py-2 text-right font-mono">
                      {l.valorUnitario.toFixed(2)}
                    </td>
                    <td className="px-3 py-2 text-right font-mono">
                      {l.importe.toFixed(2)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}

function Metadato({
  label,
  valor,
  mono,
}: {
  label: string;
  valor: string;
  mono?: boolean;
}) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className={mono ? 'break-all font-mono text-xs' : ''}>{valor}</dd>
    </div>
  );
}

function monto(v: number, moneda: string): string {
  try {
    return new Intl.NumberFormat('es-MX', {
      style: 'currency',
      currency: moneda,
      minimumFractionDigits: 2,
    }).format(v);
  } catch {
    return `${v.toFixed(2)} ${moneda}`;
  }
}
