import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Download, FileText } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { useFactura } from '@/features/cxp/api/useFacturas';
import { descargarPdfCfdi, descargarXmlCfdi } from '@/features/cxp/lib/cfdi-archivos';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { DocumentoOrigen } from '../../api/polizas-types';
import { mensajePoliza } from '../../lib/polizas';

const TEXTO_MODULO: Record<string, string> = {
  cuentas_por_pagar: 'Cuentas por Pagar',
  cuentas_por_cobrar: 'Cuentas por Cobrar',
  facturacion: 'Facturación',
  almacen: 'Almacén',
  compras: 'Compras',
  tesoreria: 'Tesorería',
};

const TEXTO_TIPO: Record<string, string> = {
  factura_proveedor: 'Factura de proveedor',
};

/**
 * Documento que originó una póliza automática (C1.4). La póliza solo guarda la referencia: el CFDI (XML/PDF) vive en
 * el módulo de origen y se descarga desde ahí; no se copia a la póliza.
 */
export function DocumentoOrigenCard({ documento: d }: { documento: DocumentoOrigen }) {
  const esFacturaCxp = d.modulo === 'cuentas_por_pagar' && d.tipo === 'factura_proveedor';
  const puedeFacturas = useHasPermission(PermisosCanonicos.CuentasPorPagarFacturasLeer);
  const puedeCfdis = useHasPermission(PermisosCanonicos.CuentasPorPagarCfdisLeer);
  const factura = useFactura(esFacturaCxp && puedeFacturas ? d.id : undefined);
  const cfdiId = factura.data?.cfdiRecibidoId ?? null;
  const uuid = d.uuid ?? factura.data?.uuidCfdi ?? null;
  const [descargando, setDescargando] = useState<'xml' | 'pdf' | null>(null);

  async function descargar(tipo: 'xml' | 'pdf') {
    if (!cfdiId) return;
    setDescargando(tipo);
    try {
      const nombre = uuid ?? cfdiId;
      await (tipo === 'xml' ? descargarXmlCfdi(cfdiId, nombre) : descargarPdfCfdi(cfdiId, nombre));
    } catch (e) {
      toast.error(`No se pudo descargar el ${tipo.toUpperCase()}. ${mensajePoliza(e)}`);
    } finally {
      setDescargando(null);
    }
  }

  const conArchivos = esFacturaCxp && puedeCfdis && !!cfdiId;

  return (
    <section aria-label="Documento origen" className="rounded-lg bg-surface-card p-4 shadow-card-flat">
      <h2 className="mb-3 flex items-center gap-2 text-md font-semibold">
        <FileText className="size-4 text-ink-muted" strokeWidth={1.6} aria-hidden="true" />
        Documento origen
      </h2>
      <dl className="grid grid-cols-1 gap-3 text-sm md:grid-cols-3">
        <div>
          <dt className="text-xs text-ink-muted">Módulo y tipo</dt>
          <dd>
            {TEXTO_MODULO[d.modulo] ?? d.modulo} · {TEXTO_TIPO[d.tipo] ?? d.tipo}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-ink-muted">Folio</dt>
          <dd className="font-mono text-xs">{d.folio ?? factura.data?.folioProveedor ?? 'Sin folio'}</dd>
        </div>
        <div className="min-w-0">
          <dt className="text-xs text-ink-muted">UUID</dt>
          <dd className="break-all font-mono text-xs">{uuid ?? 'Sin UUID'}</dd>
        </div>
      </dl>

      {esFacturaCxp && (
        <div className="mt-3 flex flex-wrap items-center gap-2 border-t border-line-divider pt-3" data-print="hidden">
          {puedeFacturas && (
            <Link
              to="/cxp/facturas/$id"
              params={{ id: d.id }}
              search={{}}
              className="text-sm font-medium text-brand hover:underline"
            >
              Ver factura en CxP
            </Link>
          )}
          {conArchivos ? (
            <div className="ml-auto flex gap-2">
              <Button variant="secondary" size="sm" disabled={descargando !== null} onClick={() => void descargar('xml')}>
                <Download aria-hidden="true" />
                {descargando === 'xml' ? 'Descargando…' : 'XML'}
              </Button>
              <Button variant="secondary" size="sm" disabled={descargando !== null} onClick={() => void descargar('pdf')}>
                <Download aria-hidden="true" />
                {descargando === 'pdf' ? 'Descargando…' : 'PDF'}
              </Button>
            </div>
          ) : (
            !(puedeFacturas && factura.isLoading) && (
              <span className="text-xs text-ink-muted">El CFDI se consulta en Cuentas por Pagar.</span>
            )
          )}
        </div>
      )}
    </section>
  );
}
