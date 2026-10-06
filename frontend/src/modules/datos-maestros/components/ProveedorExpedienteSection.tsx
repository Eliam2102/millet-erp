import { useId, useRef, useState } from 'react';
import { format } from 'date-fns';
import { Download, Loader2, TriangleAlert, Upload } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { AdjuntosManager } from '@/components/erp/adjuntos/AdjuntosManager';
import { EstadoExpedienteBadge } from '@/components/erp/adjuntos/EstadoAdjuntoBadge';
import {
  EstadoAdjunto,
  EstadoExpediente,
  aAdjuntoItem,
  aTipoSelector,
  mensajeErrorAdjunto,
  useAdjuntos,
  useDarDeBajaAdjunto,
  useEnlaceAdjunto,
  useExpediente,
  useSubirAdjunto,
  useTiposDocumento,
  type ExpedienteDocumento,
} from '@/components/erp/adjuntos/api';
import { esApiError } from '@/lib/api';
import { parseDateOnlyLocal } from '@/lib/datetime';
import { cn } from '@/lib/utils';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Expediente documental del proveedor (F1-ADM-11 G1.2): los tipos de
 * documento con su estado (Faltante / Vigente / Por vencer / Vencido),
 * adjuntar o reemplazar por tipo, y el historial con descarga por enlace
 * temporal y baja con motivo.
 *
 * <para>Visibilidad por permiso (<c>adjuntos-ver</c> / <c>-subir</c> /
 * <c>-baja</c>); es solo UX: la API vuelve a validar cada operación.</para>
 */
const TIPO_ENTIDAD = 'proveedor';
// Mismos formatos que la política de proveedor del backend (PRUEBA, pendiente Eliam).
const MIME_PROVEEDOR = ['application/pdf', 'application/xml', 'text/xml', 'image/jpeg', 'image/png'];
const MAX_BYTES = 10 * 1024 * 1024;

export function ProveedorExpedienteSection({ proveedorId }: { proveedorId: string }) {
  const canVer = useHasPermission(PermisosCanonicos.DatosMaestrosProveedoresAdjuntosVer);
  const canSubir = useHasPermission(PermisosCanonicos.DatosMaestrosProveedoresAdjuntosSubir);
  const canBaja = useHasPermission(PermisosCanonicos.DatosMaestrosProveedoresAdjuntosBaja);
  const base = `/api/v1/datos-maestros/proveedores/${proveedorId}`;
  const [verBajas, setVerBajas] = useState(false);

  const expediente = useExpediente(base, { enabled: canVer });
  const tipos = useTiposDocumento(TIPO_ENTIDAD, { enabled: canVer });
  const adjuntos = useAdjuntos(base, { enabled: canVer, incluirBajas: canBaja && verBajas });
  const darDeBaja = useDarDeBajaAdjunto(base);
  const enlace = useEnlaceAdjunto(base);

  if (!canVer) return null;

  if (expediente.isError) {
    const problem = esApiError(expediente.error) ? expediente.error.problem : undefined;
    return (
      <div className="mt-4 max-w-3xl">
        <ErrorState problem={problem} onRetry={() => expediente.refetch()} />
      </div>
    );
  }
  if (expediente.isLoading || expediente.data == null) {
    return (
      <div className="mt-4 max-w-3xl">
        <TableSkeleton rows={5} columns={[{ width: 'w-48' }, { width: 'w-24' }, { width: 'w-32' }]} />
      </div>
    );
  }

  const exp = expediente.data;
  const nombrePorCodigo = new Map(exp.documentos.map((d) => [d.codigo, d.nombre]));
  const nombres = (codigos: string[]) => codigos.map((c) => nombrePorCodigo.get(c) ?? c).join(', ');

  return (
    <section
      aria-labelledby="expediente-titulo"
      className="mt-4 max-w-3xl rounded-lg bg-surface-card p-4 shadow-card-flat"
      data-component="proveedor-expediente"
    >
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <h3 id="expediente-titulo" className="text-md font-semibold text-ink">
          Expediente documental
        </h3>
        {exp.completo && <Badge variant="success">Expediente completo</Badge>}
      </div>

      {!exp.completo && (
        <div
          role="note"
          className="mb-3 flex items-start gap-2 rounded-md bg-warning-note-bg px-3 py-2.5 text-sm text-warning-note-fg"
        >
          <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
          <div>
            {exp.faltantes.length > 0 && <p>Falta adjuntar: {nombres(exp.faltantes)}.</p>}
            {exp.vencidos.length > 0 && <p>Vencidos, hay que reemplazarlos: {nombres(exp.vencidos)}.</p>}
          </div>
        </div>
      )}

      <ul className="divide-y divide-line-row" aria-label="Documentos del expediente">
        {exp.documentos.map((d) => (
          <FilaExpediente
            key={d.tipoDocumentoId}
            doc={d}
            base={base}
            canSubir={canSubir}
            onDescargar={(adjuntoId) => enlace.mutateAsync({ adjuntoId })}
          />
        ))}
      </ul>

      <h4 className="mb-2 mt-5 text-sm font-semibold text-ink">Historial de documentos</h4>
      <AdjuntosManager
        adjuntos={(adjuntos.data ?? []).map(aAdjuntoItem)}
        tipos={(tipos.data ?? []).map(aTipoSelector)}
        tiposLoading={tipos.isLoading}
        adjuntosLoading={adjuntos.isLoading}
        adjuntosError={adjuntos.isError ? mensajeErrorAdjunto(adjuntos.error) : null}
        onUpload={async () => {}}
        canUpload={false}
        canRemove={canBaja}
        modoBaja
        onDarDeBaja={async (adjuntoId, motivo) => {
          await darDeBaja.mutateAsync({ adjuntoId, motivo });
          toast.success('Documento dado de baja.');
        }}
        onDescargar={async (adjuntoId) => {
          await enlace.mutateAsync({ adjuntoId });
        }}
        puedeVerBajas={canBaja}
        verBajas={verBajas}
        onVerBajasChange={setVerBajas}
      />
    </section>
  );
}

function FilaExpediente({
  doc,
  base,
  canSubir,
  onDescargar,
}: {
  doc: ExpedienteDocumento;
  base: string;
  canSubir: boolean;
  onDescargar: (adjuntoId: string) => Promise<unknown>;
}) {
  const [abierto, setAbierto] = useState(false);
  const [errorDescarga, setErrorDescarga] = useState<string | null>(null);
  const actual = doc.actual;
  const vencido = doc.estado === EstadoExpediente.Vencido;

  async function descargar() {
    if (!actual) return;
    setErrorDescarga(null);
    try {
      await onDescargar(actual.id);
    } catch (err) {
      setErrorDescarga(mensajeErrorAdjunto(err));
    }
  }

  return (
    <li className="py-2.5" data-tipo={doc.codigo} data-estado={doc.estado}>
      <div className="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 sm:grid-cols-[minmax(0,1.6fr)_auto_auto]">
        <div className="min-w-0">
          <p className="truncate text-sm font-medium text-ink">
            {doc.nombre}
            {!doc.obligatorio && <span className="font-normal text-ink-muted"> (opcional)</span>}
          </p>
          <p className={cn('truncate text-xs', vencido ? 'font-medium text-danger-fg' : 'text-ink-muted')}>
            {actual
              ? `${actual.nombreArchivo}${
                  actual.vigenteHasta
                    ? ` · vigente hasta ${format(parseDateOnlyLocal(actual.vigenteHasta), 'dd/MM/yyyy')}`
                    : ''
                }`
              : 'Sin documento'}
          </p>
        </div>
        <EstadoExpedienteBadge estado={doc.estado} />
        <div className="col-span-2 flex items-center justify-end gap-1 sm:col-span-1">
          {actual && actual.estado !== EstadoAdjunto.Baja && (
            <Button
              size="sm"
              variant="ghost"
              onClick={descargar}
              aria-label={`Descargar ${doc.nombre}`}
            >
              <Download />
            </Button>
          )}
          {canSubir && (
            <Button
              size="sm"
              variant="secondary"
              onClick={() => setAbierto((v) => !v)}
              aria-expanded={abierto}
              aria-label={`${actual ? 'Reemplazar' : 'Adjuntar'} ${doc.nombre}`}
            >
              <Upload />
              {actual ? 'Reemplazar' : 'Adjuntar'}
            </Button>
          )}
        </div>
      </div>
      {errorDescarga && (
        <p role="alert" className="mt-1 text-xs text-danger-fg">
          {errorDescarga}
        </p>
      )}
      {abierto && canSubir && (
        <FormularioSubida doc={doc} base={base} onTerminado={() => setAbierto(false)} />
      )}
    </li>
  );
}

/** Formulario inline (borde punteado brand = agregar) para adjuntar o reemplazar un tipo. */
function FormularioSubida({
  doc,
  base,
  onTerminado,
}: {
  doc: ExpedienteDocumento;
  base: string;
  onTerminado: () => void;
}) {
  const archivoId = useId();
  const vigenciaId = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  const [archivo, setArchivo] = useState<File | null>(null);
  const [vigenteHasta, setVigenteHasta] = useState('');
  const [error, setError] = useState<string | null>(null);
  const { subir, isUploading, progress } = useSubirAdjunto(base);

  function elegir(file: File | null) {
    setError(null);
    if (file && file.size > MAX_BYTES) {
      setArchivo(null);
      setError('El archivo excede el máximo de 10 MB.');
      return;
    }
    if (file && !MIME_PROVEEDOR.includes(file.type)) {
      setArchivo(null);
      setError('Formato no permitido. Usa PDF, XML, JPG o PNG.');
      return;
    }
    setArchivo(file);
  }

  async function enviar() {
    if (!archivo) return;
    setError(null);
    try {
      await subir({
        archivo,
        tipoDocumentoId: doc.tipoDocumentoId,
        vigenteHasta: doc.vigenciaMeses != null ? vigenteHasta || null : null,
      });
      toast.success(`"${archivo.name}" adjuntado.`);
      onTerminado();
    } catch (err) {
      setError(mensajeErrorAdjunto(err));
    }
  }

  return (
    <div
      className="mt-2 grid gap-3 rounded-md border border-dashed border-brand p-3 sm:grid-cols-2"
      data-component="expediente-subida"
    >
      <div className="grid gap-1.5">
        <label htmlFor={archivoId} className="text-xs font-medium text-ink-strong">
          Archivo (PDF, XML, JPG o PNG, máx. 10 MB)
        </label>
        <input
          ref={inputRef}
          id={archivoId}
          type="file"
          accept=".pdf,.xml,.jpg,.jpeg,.png"
          disabled={isUploading}
          onChange={(e) => elegir(e.target.files?.[0] ?? null)}
          className="text-sm text-ink file:mr-2 file:rounded-sm file:border file:border-line-control file:bg-surface-card file:px-2 file:py-1 file:text-xs"
        />
      </div>
      {doc.vigenciaMeses != null && (
        <div className="grid gap-1.5">
          <label htmlFor={vigenciaId} className="text-xs font-medium text-ink-strong">
            Vigente hasta <span className="font-normal text-ink-muted">(opcional)</span>
          </label>
          <input
            id={vigenciaId}
            type="date"
            value={vigenteHasta}
            onChange={(e) => setVigenteHasta(e.target.value)}
            disabled={isUploading}
            aria-describedby={`${vigenciaId}-ayuda`}
            className="h-ctl-xl rounded-md border border-line-control bg-surface-card px-3 text-sm text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand"
          />
          <p id={`${vigenciaId}-ayuda`} className="text-xs text-ink-muted">
            Si la dejas vacía se calcula con {doc.vigenciaMeses} {doc.vigenciaMeses === 1 ? 'mes' : 'meses'} desde hoy.
          </p>
        </div>
      )}
      {error && (
        <p role="alert" className="text-xs text-danger-fg sm:col-span-2">
          {error}
        </p>
      )}
      <div className="flex items-center justify-end gap-2 sm:col-span-2">
        <Button variant="ghost" size="sm" onClick={onTerminado} disabled={isUploading}>
          Cancelar
        </Button>
        <Button
          size="sm"
          onClick={enviar}
          disabled={!archivo || isUploading}
          title={archivo ? undefined : 'Selecciona un archivo para subir.'}
        >
          {isUploading ? (
            <>
              <Loader2 className="animate-spin" /> Subiendo {progress}%
            </>
          ) : (
            'Subir documento'
          )}
        </Button>
      </div>
    </div>
  );
}
