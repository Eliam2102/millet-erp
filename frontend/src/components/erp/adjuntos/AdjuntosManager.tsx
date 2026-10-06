import {
  type ChangeEvent,
  type DragEvent,
  type ReactNode,
  useEffect,
  useId,
  useRef,
  useState,
} from 'react';
import { Ban, Download, Trash2, Upload, X, Loader2, Inbox } from 'lucide-react';
import { format } from 'date-fns';
import { Button } from '@/components/ui/button';
import { EmptyState } from '@/components/erp';
import {
  TipoDocumentoSelector,
  type TipoDocumentoSelectorItem,
} from '@/components/erp/adjuntos/TipoDocumentoSelector';
import { AdjuntoPreview } from '@/components/erp/adjuntos/AdjuntoPreview';
import { useContenidoAdjunto } from '@/components/erp/adjuntos/useContenidoAdjunto';
import { EstadoAdjuntoBadge } from '@/components/erp/adjuntos/EstadoAdjuntoBadge';
import { MotivoBajaDialog } from '@/components/erp/adjuntos/MotivoBajaDialog';
import { EstadoAdjunto } from '@/components/erp/adjuntos/api/types';
import { mensajeErrorAdjunto } from '@/components/erp/adjuntos/api/hooks';
import { formatDateTime, parseDateOnlyLocal } from '@/lib/datetime';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;AdjuntosManager/&gt;</c> — componente cross-módulo (FOC2,
 * UF3-PR2). Lista de adjuntos + drag-and-drop + file picker fallback +
 * upload con progress. **API parametrizada** (no acoplada a OC) para
 * que CxP/Activos/Recepción lo consuman cambiando solo las props.
 *
 * <para><b>Contrato cross-módulo</b>:</para>
 * <list>
 *   <item><c>adjuntos</c>: lista actual del agregado (el caller fetcha
 *   y pasa).</item>
 *   <item><c>tipos</c>: catálogo de tipos de documento del módulo.</item>
 *   <item><c>onUpload</c>: callback async que realiza el upload (el
 *   caller wirea el hook adecuado: <c>useAdjuntarDocumento</c> en OC).</item>
 *   <item><c>onRemove</c>: callback async que borra. Visible solo si
 *   <c>canRemove=true</c>.</item>
 *   <item><c>uploadProgress</c>: 0-100 mientras hay upload activo.</item>
 *   <item><c>maxBytes</c>: tope por archivo (default 20 MB §4.11).</item>
 *   <item><c>mimeWhitelist</c>: tipos MIME aceptados; default es la
 *   lista de FOC2 (PDF + imágenes + Office).</item>
 *   <item><c>obligatorios</c>: ids de tipos que el caller marca como
 *   requeridos por flujo (ej. ficha técnica si importación). El
 *   componente solo los muestra con un badge — no enforzamos en frontend
 *   (el backend lo valida al transmitir).</item>
 * </list>
 *
 * <para><b>Servicio genérico G1.2</b> (opt-in, no afecta a OC): metadatos
 * de vigencia/estado/hash, campo de vigencia por tipo
 * (<c>vigenciaMeses</c>), baja con motivo (<c>modoBaja</c> +
 * <c>onDarDeBaja</c>), toggle de dadas de baja (<c>puedeVerBajas</c>) y
 * descarga por enlace temporal (<c>onDescargar</c>).</para>
 *
 * <para><b>Permisos</b>: gateado por <c>canUpload</c>/<c>canRemove</c>
 * que el caller resuelve via la matriz §6.1
 * (<c>accionAdjuntarDocumento</c> / <c>accionRemoverAdjunto</c> en OC).</para>
 */

/** Tipo mínimo de un adjunto que el manager renderea. */
export interface AdjuntoItem {
  id: string;
  tipoDocumentoId: string;
  nombreArchivo: string;
  contentType: string;
  tamanoBytes: number;
  fechaCarga: string;
  usuarioCargaId: string;
  // ── Servicio genérico G1.2 (todos opcionales) ──
  estado?: EstadoAdjunto;
  /** yyyy-MM-dd */
  vigenteHasta?: string | null;
  hashSha256?: string;
  bajaEn?: string | null;
  bajaMotivo?: string | null;
}

const DEFAULT_MAX_BYTES = 20 * 1024 * 1024; // 20 MB
const DEFAULT_MIME_WHITELIST: readonly string[] = [
  'application/pdf',
  'image/jpeg',
  'image/png',
  'image/webp',
  'image/gif',
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', // xlsx
  'application/vnd.ms-excel', // xls
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document', // docx
  'application/msword', // doc
  'text/plain',
];

export interface AdjuntosManagerProps<
  TItem extends AdjuntoItem,
  TTipo extends TipoDocumentoSelectorItem,
> {
  adjuntos: readonly TItem[];
  tipos: readonly TTipo[];
  tiposLoading?: boolean;

  /**
   * Subir un archivo. Recibe el File + el id del tipo seleccionado.
   * Debe lanzar (rechazar) en error para que el manager muestre el
   * mensaje al usuario.
   */
  onUpload: (args: {
    archivo: File;
    tipoDocumentoId: string;
    /** yyyy-MM-dd; solo si el tipo maneja vigencia y el usuario la capturó. */
    vigenteHasta?: string;
  }) => Promise<void>;
  /** Callback de eliminación. Solo si <c>canRemove=true</c>. */
  onRemove?: (adjuntoId: string) => Promise<void>;

  /** Progreso del upload activo (0-100). */
  uploadProgress?: number;
  /** True mientras hay un upload en vuelo. */
  isUploading?: boolean;

  /** Baja con diálogo de motivo (reemplaza al borrado directo). Requiere <c>onDarDeBaja</c>. */
  modoBaja?: boolean;
  onDarDeBaja?: (adjuntoId: string, motivo: string) => Promise<void>;
  /** Descarga por enlace temporal; debe lanzar si falla. Sin él, se usa el link de contenido. */
  onDescargar?: (adjuntoId: string) => Promise<void>;
  /** Muestra el toggle "ver dadas de baja" (solo con permiso de baja). */
  puedeVerBajas?: boolean;
  verBajas?: boolean;
  onVerBajasChange?: (ver: boolean) => void;
  /** Texto de ayuda de formatos permitidos. */
  ayudaFormatos?: string;
  /** Estado de la lista del caller (carga / error ya traducido). */
  adjuntosLoading?: boolean;
  adjuntosError?: string | null;

  /** Si <c>false</c>, oculta el área de drop + botones de upload. */
  canUpload?: boolean;
  /** Si <c>false</c>, oculta el botón de eliminar de cada fila. */
  canRemove?: boolean;

  /** Tope por archivo (bytes). Default 20 MB. */
  maxBytes?: number;
  /** MIME whitelist. Default PDF + imágenes + Office. */
  mimeWhitelist?: readonly string[];
  /** Ids de tipos marcados como obligatorios por el flujo. Solo decora. */
  obligatorios?: readonly string[];

  /** Resolver opcional id → label (ej. nombre de usuario). */
  resolverNombreUsuario?: (id: string) => string | null;

  /**
   * Resolver opcional que devuelve la ruta del endpoint autenticado del
   * que se baja el CONTENIDO del adjunto (stream por backend, ADR-0024).
   * El manager lo consume vía <c>useContenidoAdjunto</c> y usa el object
   * URL resultante para el preview inline y el enlace de descarga. Si no
   * se pasa, no hay preview ni descarga (los DTO ya no traen URL de blob). El caller OC inyecta
   * <c>/api/v1/compras/ordenes/{id}/adjuntos/{adjuntoId}/contenido</c>.
   */
  resolverContenidoUrl?: (adjunto: TItem) => string | null;

  /**
   * Slot para tipo del documento adjunto en cada fila — útil si el
   * caller quiere renderear más metadata (ej. badge "obligatorio").
   * Si no se pasa, se muestra <c>tipoDocumento.descripcion</c>.
   */
  renderTipoExtra?: (tipo: TTipo) => ReactNode;
}

export function AdjuntosManager<
  TItem extends AdjuntoItem,
  TTipo extends TipoDocumentoSelectorItem,
>({
  adjuntos,
  tipos,
  tiposLoading,
  onUpload,
  onRemove,
  uploadProgress = 0,
  isUploading = false,
  canUpload = true,
  canRemove = true,
  maxBytes = DEFAULT_MAX_BYTES,
  mimeWhitelist = DEFAULT_MIME_WHITELIST,
  obligatorios,
  resolverNombreUsuario,
  resolverContenidoUrl,
  renderTipoExtra,
  modoBaja = false,
  onDarDeBaja,
  onDescargar,
  puedeVerBajas = false,
  verBajas = false,
  onVerBajasChange,
  ayudaFormatos = 'PDF, imágenes, Office',
  adjuntosLoading = false,
  adjuntosError = null,
}: AdjuntosManagerProps<TItem, TTipo>) {
  const fileInputId = useId();
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const [tipoSeleccionado, setTipoSeleccionado] = useState<string | null>(
    null,
  );
  const [stagedFile, setStagedFile] = useState<File | null>(null);
  const [validationError, setValidationError] = useState<string | null>(null);
  const [isDragOver, setIsDragOver] = useState(false);
  const [removingId, setRemovingId] = useState<string | null>(null);
  const [vigenteHasta, setVigenteHasta] = useState('');
  const [accionError, setAccionError] = useState<string | null>(null);
  const [bajaDe, setBajaDe] = useState<TItem | null>(null);
  const [bajaError, setBajaError] = useState<string | null>(null);
  const vigenciaId = useId();
  const verBajasId = useId();

  const tiposPorId = new Map(tipos.map((t) => [t.id, t]));
  const obligatoriosSet = new Set(obligatorios ?? []);
  const tipoActual = tipoSeleccionado ? tiposPorId.get(tipoSeleccionado) : undefined;
  const tipoConVigencia = tipoActual?.vigenciaMeses != null;

  function validarArchivo(file: File): string | null {
    if (file.size > maxBytes) {
      return `Archivo demasiado grande (${formatBytes(file.size)}). Máximo ${formatBytes(maxBytes)}.`;
    }
    if (mimeWhitelist.length > 0 && !mimeWhitelist.includes(file.type)) {
      return `Tipo de archivo no permitido (${file.type || 'desconocido'}).`;
    }
    return null;
  }

  function handleFileSelected(file: File) {
    const err = validarArchivo(file);
    if (err) {
      setValidationError(err);
      setStagedFile(null);
      return;
    }
    setValidationError(null);
    setStagedFile(file);
  }

  function handleInputChange(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (file) handleFileSelected(file);
  }

  function handleDragOver(e: DragEvent<HTMLDivElement>) {
    e.preventDefault();
    setIsDragOver(true);
  }
  function handleDragLeave() {
    setIsDragOver(false);
  }
  function handleDrop(e: DragEvent<HTMLDivElement>) {
    e.preventDefault();
    setIsDragOver(false);
    const file = e.dataTransfer.files?.[0];
    if (file) handleFileSelected(file);
  }

  async function confirmarUpload() {
    if (stagedFile == null || tipoSeleccionado == null || isUploading) return;
    try {
      await onUpload({
        archivo: stagedFile,
        tipoDocumentoId: tipoSeleccionado,
        ...(tipoConVigencia && vigenteHasta ? { vigenteHasta } : {}),
      });
      // Limpia staging tras éxito
      setStagedFile(null);
      setTipoSeleccionado(null);
      setVigenteHasta('');
      setValidationError(null);
      if (fileInputRef.current) fileInputRef.current.value = '';
    } catch (err) {
      const msg =
        err instanceof Error
          ? err.message
          : 'No se pudo subir el archivo. Reintenta.';
      setValidationError(msg);
    }
  }

  function cancelarStaging() {
    setStagedFile(null);
    setValidationError(null);
    if (fileInputRef.current) fileInputRef.current.value = '';
  }

  async function handleRemove(adjuntoId: string) {
    if (onRemove == null) return;
    setRemovingId(adjuntoId);
    try {
      await onRemove(adjuntoId);
    } finally {
      setRemovingId(null);
    }
  }

  async function handleDescargar(adjuntoId: string) {
    if (onDescargar == null) return;
    setAccionError(null);
    try {
      await onDescargar(adjuntoId);
    } catch (err) {
      setAccionError(mensajeErrorAdjunto(err));
    }
  }

  async function confirmarBaja(motivo: string) {
    if (bajaDe == null || onDarDeBaja == null) return;
    setBajaError(null);
    try {
      await onDarDeBaja(bajaDe.id, motivo);
      setBajaDe(null);
    } catch (err) {
      setBajaError(mensajeErrorAdjunto(err));
    }
  }

  return (
    <div className="space-y-4" data-component="adjuntos-manager">
      {/* Drop zone + file picker */}
      {canUpload && (
        <div className="space-y-3">
          <div
            onDragOver={handleDragOver}
            onDragLeave={handleDragLeave}
            onDrop={handleDrop}
            className={cn(
              'rounded-md border-2 border-dashed p-6 text-center transition-colors',
              isDragOver
                ? 'border-brand bg-surface-selected'
                : 'border-line-control',
            )}
            data-dragover={isDragOver || undefined}
          >
            <Upload
              className="mx-auto h-8 w-8 text-muted-foreground"
              aria-hidden="true"
            />
            <p className="mt-2 text-sm text-muted-foreground">
              Arrastra un archivo aquí, o
            </p>
            <label
              htmlFor={fileInputId}
              className="mt-2 inline-flex cursor-pointer items-center gap-2 rounded-md border border-line-control bg-surface-card px-3 py-1.5 text-sm font-medium has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-brand hover:bg-surface-subtle"
            >
              Seleccionar archivo
              <input
                id={fileInputId}
                ref={fileInputRef}
                type="file"
                onChange={handleInputChange}
                className="sr-only"
                accept={mimeWhitelist.join(',')}
              />
            </label>
            <p className="mt-2 text-xs text-muted-foreground">
              Máximo {formatBytes(maxBytes)}. Tipos permitidos: {ayudaFormatos}.
            </p>
          </div>

          {/* Staging del archivo seleccionado */}
          {stagedFile && (
            <div
              className="rounded-md border border-line bg-surface-subtle p-3"
              data-component="adjuntos-staging"
            >
              <div className="mb-3 flex items-start justify-between gap-2">
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium">
                    {stagedFile.name}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {formatBytes(stagedFile.size)} · {stagedFile.type || 'desconocido'}
                  </p>
                </div>
                <Button
                  size="sm"
                  variant="ghost"
                  onClick={cancelarStaging}
                  disabled={isUploading}
                  aria-label="Cancelar selección"
                  className="h-7 px-2"
                >
                  <X className="h-3.5 w-3.5" />
                </Button>
              </div>
              <div className="grid gap-2 md:grid-cols-[1fr_auto]">
                <TipoDocumentoSelector
                  items={tipos}
                  loading={tiposLoading}
                  value={tipoSeleccionado}
                  onChange={setTipoSeleccionado}
                  placeholder="Tipo de documento (requerido)"
                  disabled={isUploading}
                />
                <Button
                  size="sm"
                  onClick={confirmarUpload}
                  disabled={
                    isUploading ||
                    tipoSeleccionado == null ||
                    validationError != null
                  }
                  data-action="confirmar-upload"
                >
                  {isUploading ? (
                    <>
                      <Loader2 className="mr-1 h-3.5 w-3.5 animate-spin" />
                      Subiendo {uploadProgress}%
                    </>
                  ) : (
                    <>
                      <Upload className="mr-1 h-3.5 w-3.5" />
                      Subir
                    </>
                  )}
                </Button>
              </div>
              {tipoConVigencia && (
                <div className="mt-2 grid max-w-xs gap-1.5">
                  <label
                    htmlFor={vigenciaId}
                    className="text-xs font-medium text-ink-strong"
                  >
                    Vigente hasta{' '}
                    <span className="font-normal text-ink-muted">(opcional)</span>
                  </label>
                  <input
                    id={vigenciaId}
                    type="date"
                    value={vigenteHasta}
                    onChange={(e) => setVigenteHasta(e.target.value)}
                    disabled={isUploading}
                    aria-describedby={`${vigenciaId}-ayuda`}
                    className="h-ctl-xl rounded-md border border-line-control bg-surface-card px-3 text-sm text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand disabled:opacity-50"
                  />
                  <p id={`${vigenciaId}-ayuda`} className="text-xs text-ink-muted">
                    Si la dejas vacía se calcula con {tipoActual?.vigenciaMeses}{' '}
                    {tipoActual?.vigenciaMeses === 1 ? 'mes' : 'meses'} desde hoy.
                  </p>
                </div>
              )}
              {/* Progress bar */}
              {isUploading && (
                <div
                  className="mt-2 h-1.5 overflow-hidden rounded-full bg-surface-muted"
                  role="progressbar"
                  aria-valuemin={0}
                  aria-valuemax={100}
                  aria-valuenow={uploadProgress}
                  aria-label="Progreso de subida"
                >
                  <div
                    className="h-full bg-brand transition-all"
                    style={{ width: `${uploadProgress}%` }}
                  />
                </div>
              )}
            </div>
          )}

          {validationError && (
            <p
              role="alert"
              className="text-xs text-danger-fg"
              data-component="adjuntos-validation-error"
            >
              {validationError}
            </p>
          )}
        </div>
      )}

      {puedeVerBajas && onVerBajasChange && (
        <div className="flex items-center gap-2">
          <input
            id={verBajasId}
            type="checkbox"
            checked={verBajas}
            onChange={(e) => onVerBajasChange(e.target.checked)}
            className="size-4 accent-brand focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand"
          />
          <label htmlFor={verBajasId} className="text-xs text-ink-strong">
            Ver documentos dados de baja
          </label>
        </div>
      )}

      {accionError && (
        <p role="alert" className="text-xs text-danger-fg" data-component="adjuntos-accion-error">
          {accionError}
        </p>
      )}

      {/* Lista de adjuntos */}
      {adjuntosError ? (
        <p role="alert" className="text-sm text-danger-fg" data-component="adjuntos-error">
          {adjuntosError}
        </p>
      ) : adjuntosLoading ? (
        <p role="status" className="text-sm text-ink-muted">
          Cargando documentos…
        </p>
      ) : adjuntos.length === 0 ? (
        <EmptyState
          icon={<Inbox className="h-10 w-10" />}
          title="Sin adjuntos."
          description={
            canUpload
              ? 'Arrastra archivos al área de arriba o usa el picker.'
              : 'No hay archivos cargados todavía.'
          }
        />
      ) : (
        <ul
          className="grid gap-2"
          aria-label="Lista de adjuntos"
          data-component="adjuntos-list"
        >
          {adjuntos.map((a) => {
            const tipo = tiposPorId.get(a.tipoDocumentoId);
            return (
              <AdjuntoFila
                key={a.id}
                adjunto={a}
                contenidoEndpoint={resolverContenidoUrl?.(a) ?? null}
                tipoDescripcion={tipo?.descripcion ?? null}
                tipoClave={tipo?.clave ?? null}
                esObligatorio={tipo != null && obligatoriosSet.has(tipo.id)}
                tipoExtra={tipo ? renderTipoExtra?.(tipo) : undefined}
                usuario={resolverNombreUsuario?.(a.usuarioCargaId) ?? null}
                canRemove={Boolean(canRemove && onRemove && !modoBaja)}
                removing={removingId === a.id}
                onRemove={onRemove ? handleRemove : undefined}
                onDescargar={onDescargar ? handleDescargar : undefined}
                onDarDeBaja={
                  modoBaja && canRemove && onDarDeBaja
                    ? () => {
                        setBajaError(null);
                        setBajaDe(a);
                      }
                    : undefined
                }
              />
            );
          })}
        </ul>
      )}

      {bajaDe && (
        <MotivoBajaDialog
          nombreArchivo={bajaDe.nombreArchivo}
          error={bajaError}
          onConfirmar={confirmarBaja}
          onCerrar={() => setBajaDe(null)}
        />
      )}
    </div>
  );
}

// ─── Fila de adjunto ──────────────────────────────────────────────

interface AdjuntoFilaProps {
  adjunto: AdjuntoItem;
  /** Endpoint autenticado del contenido; null = sin preview ni descarga. */
  contenidoEndpoint: string | null;
  tipoDescripcion: string | null;
  tipoClave: string | null;
  esObligatorio: boolean;
  tipoExtra?: ReactNode;
  usuario: string | null;
  canRemove: boolean;
  removing: boolean;
  onRemove?: (adjuntoId: string) => void;
  onDescargar?: (adjuntoId: string) => Promise<void>;
  /** Abre el diálogo de baja con motivo (modo baja). */
  onDarDeBaja?: () => void;
}

/**
 * Fila de un adjunto. Componente propio (no inline en el <c>map</c>) para
 * poder llamar <c>useContenidoAdjunto</c> por adjunto sin violar las
 * reglas de hooks. Con <c>contenidoEndpoint</c>, baja el contenido por el
 * backend y usa el object URL (<c>blob:</c>) para el preview inline y la
 * descarga; los DTO no traen URL de blob.
 */
function AdjuntoFila({
  adjunto: a,
  contenidoEndpoint,
  tipoDescripcion,
  tipoClave,
  esObligatorio,
  tipoExtra,
  usuario,
  canRemove,
  removing,
  onRemove,
  onDescargar,
  onDarDeBaja,
}: AdjuntoFilaProps) {
  const [descargando, setDescargando] = useState(false);
  const deBaja = a.estado === EstadoAdjunto.Baja;
  const contenidoQuery = useContenidoAdjunto(contenidoEndpoint);
  const contenidoUrl = contenidoQuery.data ?? null;
  // Revoca el object URL al desmontar o cuando cambia (evita fuga de memoria).
  useEffect(() => {
    if (!contenidoUrl) return;
    return () => URL.revokeObjectURL(contenidoUrl);
  }, [contenidoUrl]);
  const isLoading = contenidoQuery.isLoading;
  // Object URL del contenido (null mientras carga o sin resolver).
  const urlEfectiva = contenidoUrl;

  return (
    <li
      className={cn(
        'flex items-start gap-3 rounded-md border border-line bg-surface-card p-3',
        deBaja && 'opacity-70',
      )}
      data-adjunto={a.id}
    >
      <AdjuntoPreview
        blobUrl={urlEfectiva ?? ''}
        contentType={a.contentType}
        nombreArchivo={a.nombreArchivo}
        size={64}
      />
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-baseline gap-2">
          {urlEfectiva ? (
            <a
              href={urlEfectiva}
              download={a.nombreArchivo}
              className="truncate text-sm font-medium hover:underline"
              title={`Descargar ${a.nombreArchivo}`}
              data-action="descargar-adjunto"
            >
              {a.nombreArchivo}
            </a>
          ) : (
            <span
              className="truncate text-sm font-medium text-muted-foreground"
              title={a.nombreArchivo}
            >
              {a.nombreArchivo}
              {isLoading ? ' (cargando…)' : ''}
            </span>
          )}
          {tipoDescripcion && (
            <span
              className={cn(
                'rounded-sm px-1.5 py-0.5 text-2xs',
                esObligatorio
                  ? 'bg-warning-bg text-warning-fg'
                  : 'bg-surface-muted text-ink-muted',
              )}
              data-tipo={tipoClave ?? undefined}
            >
              {tipoDescripcion}
            </span>
          )}
          {tipoExtra}
          {a.estado != null && <EstadoAdjuntoBadge estado={a.estado} />}
        </div>
        <p className="mt-0.5 text-xs text-muted-foreground">
          {formatBytes(a.tamanoBytes)} · {formatDateTime(a.fechaCarga)}
          {usuario ? ` · ${usuario}` : ''}
          {a.hashSha256 && (
            <>
              {' · '}
              <span className="font-mono text-2xs" title={`SHA-256: ${a.hashSha256}`}>
                {a.hashSha256.slice(0, 8)}
              </span>
            </>
          )}
        </p>
        {a.vigenteHasta && (
          <p
            className={cn(
              'mt-0.5 text-xs',
              a.estado === EstadoAdjunto.Vencido
                ? 'font-medium text-danger-fg'
                : 'text-ink-muted',
            )}
          >
            Vigente hasta {format(parseDateOnlyLocal(a.vigenteHasta), 'dd/MM/yyyy')}
          </p>
        )}
        {deBaja && (
          <p className="mt-0.5 text-xs text-ink-secondary">
            Baja{a.bajaEn ? ` el ${formatDateTime(a.bajaEn)}` : ''}
            {a.bajaMotivo ? `: ${a.bajaMotivo}` : ''}
          </p>
        )}
      </div>
      {onDescargar && !deBaja && (
        <Button
          size="sm"
          variant="ghost"
          disabled={descargando}
          onClick={async () => {
            setDescargando(true);
            try {
              await onDescargar(a.id);
            } finally {
              setDescargando(false);
            }
          }}
          aria-label={`Descargar ${a.nombreArchivo}`}
          data-action="descargar-enlace"
        >
          {descargando ? <Loader2 className="animate-spin" /> : <Download />}
        </Button>
      )}
      {onDarDeBaja && !deBaja && (
        <Button
          size="sm"
          variant="ghost"
          onClick={onDarDeBaja}
          aria-label={`Dar de baja ${a.nombreArchivo}`}
          className="text-danger-fg hover:bg-danger-bg"
          data-action="dar-de-baja-adjunto"
        >
          <Ban />
        </Button>
      )}
      {canRemove && onRemove && (
        <Button
          size="sm"
          variant="ghost"
          onClick={() => onRemove(a.id)}
          disabled={removing}
          aria-label={`Eliminar ${a.nombreArchivo}`}
          className="h-7 px-2 text-danger-fg hover:bg-danger-bg"
          data-action="eliminar-adjunto"
        >
          {removing ? (
            <Loader2 className="h-3.5 w-3.5 animate-spin" />
          ) : (
            <Trash2 className="h-3.5 w-3.5" />
          )}
        </Button>
      )}
    </li>
  );
}

// ─── Helpers ──────────────────────────────────────────────────────

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
