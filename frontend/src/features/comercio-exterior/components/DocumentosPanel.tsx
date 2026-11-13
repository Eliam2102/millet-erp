import { FormCampo } from '@/components/erp/forms/FormGrid';
import { estadosDocumento } from '../lib/estados-ui';
import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { AdjuntosManager } from '@/components/erp/adjuntos/AdjuntosManager';
import {
  useAdjuntos,
  useSubirAdjunto,
  useDarDeBajaAdjunto,
  useEnlaceAdjunto,
} from '@/components/erp/adjuntos/api/hooks';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { DateTimeDisplay } from '@/components/erp';
import { CE_BASE, ceKeys, useAccionExpediente } from '../api/hooks';
import type { ExpedienteDetalle } from '../api/types';

export function DocumentosPanel({ data }: { data: ExpedienteDetalle }) {
  const id = data.expediente.id;
  const base = `${CE_BASE}/${id}`;
  const ver = useHasPermission(P.ComercioExteriorExpedientesAdjuntosVer);
  const subir =
    useHasPermission(P.ComercioExteriorExpedientesAdjuntosSubir) && data.expediente.estado === 1;
  const permisoBaja = useHasPermission(P.ComercioExteriorExpedientesAdjuntosBaja);
  const baja = permisoBaja && data.expediente.estado === 1;
  const revisar = useHasPermission(P.ComercioExteriorExpedientesDocumentosRevisar);
  const [verBajas, setVerBajas] = useState(false);
  const archivos = useAdjuntos(base, { enabled: ver, incluirBajas: verBajas });
  const upload = useSubirAdjunto(base);
  const darBaja = useDarDeBajaAdjunto(base);
  const enlace = useEnlaceAdjunto(base);
  const accion = useAccionExpediente(id);
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  const [motivo, setMotivo] = useState('');
  const [reemplazaA, setReemplazaA] = useState<string | null>(null);
  const [numeroPedimento, setNumeroPedimento] = useState('');
  const [error, setError] = useState<string | null>(null);
  async function registrar(tipoDocumentoId: string, adjuntoId: string | null, noAplica = false) {
    setError(null);
    try {
      await accion.mutateAsync({
        accion: 'documentos',
        body: {
          tipoDocumentoId,
          adjuntoId,
          noAplica,
          motivo: motivo || null,
          ...(reemplazaA ? { reemplazaA } : {}),
          ...(!noAplica &&
          numeroPedimento &&
          data.requisitos.some(
            (r) =>
              r.id === tipoDocumentoId &&
              (r.codigo === 'IMP_PEDIMENTO' || r.codigo === 'EXP_PEDIMENTO'),
          )
            ? { numeroPedimento }
            : {}),
        },
      });
      setMotivo('');
      setReemplazaA(null);
      setNumeroPedimento('');
    } catch (err) {
      setError(
        esApiError(err)
          ? (err.problem.detail ?? err.problem.title ?? 'No se pudo registrar la versión.')
          : 'No se pudo registrar la versión.',
      );
      throw err;
    }
  }
  const pendientes = (archivos.data ?? []).filter(
    (a) => !a.bajaEn && !data.documentos.some((d) => d.adjuntoId === a.id),
  );
  return (
    <section className="space-y-5">
      {data.requisitos.some((r) => !r.hitoClave) && (
        <p role="note" className="rounded-md bg-warning-note-bg px-3 py-2.5 text-warning-note-fg">
          Hay tipos de documento sin etapa asignada.{' '}
          <a className="underline" href="#ce-configuracion-documentos">
            Ir a la configuración
          </a>
        </p>
      )}
      <div
        id="ce-configuracion-documentos"
        className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat"
      >
        <h3 className="font-semibold">Documentos requeridos por hito</h3>
        <p className="text-xs text-ink-muted">
          Esta configuración aplica a todos los expedientes del mismo tipo de operación. Un
          documento condicional se pide sólo cuando aplica: puede registrarse «No aplica» con
          motivo.
        </p>
        {data.requisitos.map((r) => (
          <FormCampo key={r.id} span={6} className="space-y-1.5">
            <Label htmlFor={`ce-requisito-hito-${r.id}`}>{r.nombre}</Label>
            <select
              id={`ce-requisito-hito-${r.id}`}
              className="h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3 text-sm"
              disabled={!revisar || accion.isPending || data.expediente.estado !== 1}
              value={r.hitoClave ?? ''}
              onChange={(e) => {
                void accion
                  .mutateAsync({
                    accion: 'requisito-hito',
                    body: { tipoDocumentoId: r.id, hitoClave: e.target.value },
                  })
                  .catch(() => {});
              }}
            >
              <option value="">Seleccione el hito requerido</option>
              {data.hitos
                .filter((h) => h.clave !== 'solicitado' && h.clave !== 'cerrado')
                .map((h) => (
                  <option key={h.clave} value={h.clave}>
                    {h.nombre}
                  </option>
                ))}
            </select>
            <div className="flex items-center gap-2">
              <Checkbox
                id={`ce-requisito-condicional-${r.id}`}
                checked={r.condicional}
                disabled={!revisar || accion.isPending || data.expediente.estado !== 1}
                onCheckedChange={(valor) => {
                  void accion
                    .mutateAsync({
                      accion: 'requisito-condicional',
                      body: { tipoDocumentoId: r.id, condicional: valor === true },
                    })
                    .catch(() => {});
                }}
              />
              <Label htmlFor={`ce-requisito-condicional-${r.id}`} className="font-normal">
                Condicional: se pide sólo cuando aplica
              </Label>
            </div>
          </FormCampo>
        ))}
      </div>
      {reemplazaA && subir && (
        <p role="status" className="text-sm text-ink-muted">
          El siguiente archivo reemplazará el documento seleccionado.
          <Button variant="ghost" size="sm" onClick={() => setReemplazaA(null)}>
            Cancelar reemplazo
          </Button>
        </p>
      )}
      {subir && (
        <div className="space-y-1.5">
          <FormCampo span={6} className="space-y-1.5">
            <Label htmlFor="ce-doc-numero-pedimento">
              Número del pedimento (al registrar su versión documental)
            </Label>
            <Input
              id="ce-doc-numero-pedimento"
              maxLength={100}
              value={numeroPedimento}
              onChange={(e) => setNumeroPedimento(e.target.value)}
            />
          </FormCampo>
          <p className="text-xs text-ink-muted">
            Capture el número de la versión vigente antes de registrar el documento del pedimento de
            exportación.
          </p>
        </div>
      )}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-md font-semibold">
          Requisitos documentales{' '}
          <Badge variant={data.faltantes.length ? 'warning' : 'info'}>
            {data.estadoDocumental}
          </Badge>
        </h2>
        {revisar && (
          <Button
            variant="outline"
            disabled={accion.isPending || data.expediente.estado !== 1}
            title={
              data.expediente.estado !== 1
                ? 'El expediente está cancelado.'
                : accion.isPending
                  ? 'Guardando revisión.'
                  : 'Validar los requisitos y confirmar la revisión documental.'
            }
            onClick={() => {
              void accion.mutateAsync({ accion: 'revisar-documentos' }).catch(() => {});
            }}
          >
            Revisar documentos
          </Button>
        )}
      </div>
      {data.faltantes.length > 0 && (
        <div role="note" className="rounded-md bg-warning-note-bg px-3 py-2.5 text-warning-note-fg">
          Faltan: {data.faltantes.map((r) => r.nombre).join(', ')}. Registra una versión vigente
          para cada requisito obligatorio.
        </div>
      )}
      <p className="text-xs text-ink-muted">
        Revisa los requisitos obligatorios indicados antes de confirmar la documentación.
      </p>
      {(accion.isError || error) && (
        <p role="alert" className="text-danger-fg">
          {error ??
            (esApiError(accion.error)
              ? (accion.error.problem.detail ?? accion.error.problem.title)
              : 'No se pudo completar la acción.')}
        </p>
      )}
      {subir && (
        <div className="space-y-1.5">
          <FormCampo span={6} className="space-y-1.5">
            <Label htmlFor="ce-motivo-doc">
              Motivo de sustitución o «No aplica» (5 a 500 caracteres)
            </Label>
            <Input
              id="ce-motivo-doc"
              value={motivo}
              maxLength={500}
              onChange={(e) => setMotivo(e.target.value)}
            />
          </FormCampo>
          <p className="text-xs text-ink-muted">
            Agregar conserva los demás archivos. Para reemplazar, selecciona el documento y captura
            el motivo de la corrección.
          </p>
        </div>
      )}
      <div className="space-y-3">
        {data.requisitos.map((r) => (
          <article key={r.id} className="rounded-lg bg-surface-card p-4 shadow-card-flat">
            <div className="flex items-center justify-between gap-3">
              <h3 className="font-medium">
                {r.nombre}{' '}
                <span className="text-xs text-ink-muted">
                  {r.obligatorio
                    ? r.condicional
                      ? 'Obligatorio cuando aplica'
                      : 'Obligatorio'
                    : r.condicional
                      ? 'Condicional'
                      : 'Opcional'}
                </span>
              </h3>
              {r.condicional && subir && (
                <Button
                  variant="outline"
                  size="sm"
                  disabled={motivo.trim().length < 5 || accion.isPending}
                  title={
                    motivo.trim().length < 5
                      ? 'Capture un motivo de al menos 5 caracteres.'
                      : accion.isPending
                        ? 'Guardando.'
                        : undefined
                  }
                  onClick={() => {
                    void registrar(r.id, null, true).catch(() => {});
                  }}
                >
                  No aplica
                </Button>
              )}
            </div>
            {subir && (
              <div className="mt-2 space-y-1.5">
                <Label htmlFor={`ce-archivo-existente-${r.id}`}>Usar un archivo ya cargado</Label>
                <select
                  id={`ce-archivo-existente-${r.id}`}
                  className="h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3 text-sm"
                  value=""
                  disabled={accion.isPending}
                  onChange={(event) => {
                    if (event.target.value)
                      void registrar(r.id, event.target.value).catch(() => {});
                  }}
                >
                  <option value="">Seleccione un archivo del expediente</option>
                  {(archivos.data ?? [])
                    .filter(
                      (a) =>
                        !a.bajaEn &&
                        !data.documentos.some(
                          (d) => d.tipoDocumentoId === r.id && d.adjuntoId === a.id,
                        ),
                    )
                    .map((a) => (
                      <option key={a.id} value={a.id}>
                        {a.nombreArchivo}
                      </option>
                    ))}
                </select>
              </div>
            )}
            <ul className="mt-2 space-y-2">
              {data.documentos
                .filter((d) => d.tipoDocumentoId === r.id)
                .map((d) => (
                  <li
                    key={d.id}
                    className="flex flex-wrap items-center gap-2 border-t border-line-row pt-2 text-xs"
                  >
                    <span>Versión {d.numeroVersion}</span>
                    {d.numeroPedimento && <span>Pedimento: {d.numeroPedimento}</span>}
                    <Badge
                      variant={
                        estadosDocumento[
                          d.estado === 2 ? 'reemplazada' : d.noAplica ? 'noAplica' : 'vigente'
                        ].tono
                      }
                    >
                      {
                        estadosDocumento[
                          d.estado === 2 ? 'reemplazada' : d.noAplica ? 'noAplica' : 'vigente'
                        ].texto
                      }
                    </Badge>
                    <span>{d.responsableNombre}</span>
                    <DateTimeDisplay value={d.createdAt} />
                    {d.motivo && <span>Motivo: {d.motivo}</span>}
                    {d.estado === 1 && subir && (
                      <Button variant="outline" size="sm" onClick={() => setReemplazaA(d.id)}>
                        {reemplazaA === d.id
                          ? 'Seleccionado para reemplazar'
                          : 'Reemplazar este documento'}
                      </Button>
                    )}
                    {d.estado === 1 && baja && (
                      <Button
                        variant="ghost"
                        size="sm"
                        disabled={motivo.trim().length < 5 || accion.isPending}
                        title={
                          motivo.trim().length < 5
                            ? 'Capture el motivo antes de quitar el documento.'
                            : undefined
                        }
                        onClick={() => {
                          void accion
                            .mutateAsync({ accion: `documentos/${d.id}/retirar`, body: { motivo } })
                            .catch(() => {});
                        }}
                      >
                        Quitar documento
                      </Button>
                    )}
                    {d.adjuntoId && ver && (
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => {
                          void enlace.mutateAsync({ adjuntoId: d.adjuntoId! }).catch(() => {});
                        }}
                      >
                        Descargar versión {d.numeroVersion}
                      </Button>
                    )}
                  </li>
                ))}
            </ul>
            {!data.documentos.some((d) => d.tipoDocumentoId === r.id) && (
              <p className="mt-2 text-xs text-ink-muted">Sin versión registrada.</p>
            )}
          </article>
        ))}
      </div>
      {ver && (
        <>
          <h2 className="text-md font-semibold">Archivos del expediente</h2>
          <p className="text-xs text-ink-muted">
            PDF, XML, imágenes, Office, TXT/CSV o archivo M original (M seguido de siete dígitos y
            extensión numérica). Máximo 20 MB por archivo.
          </p>
          <AdjuntosManager
            adjuntos={(archivos.data ?? []).map((a) => ({
              ...a,
              fechaCarga: a.subidoEn,
              usuarioCargaId: a.subidoPorId,
            }))}
            tipos={data.requisitos.map((r) => ({
              id: r.id,
              clave: r.codigo,
              descripcion: r.nombre,
            }))}
            mimeWhitelist={[]}
            canUpload={subir}
            canRemove={baja}
            adjuntosLoading={archivos.isLoading}
            adjuntosError={archivos.isError ? 'No se pudieron cargar los archivos.' : null}
            onUpload={async (args) => {
              if (
                !/\.(pdf|jpe?g|png|webp|gif|xlsx?|docx?|txt|csv|xml)$/i.test(args.archivo.name) &&
                !/^M[0-9]{7}\.[0-9]{3}$/i.test(args.archivo.name)
              )
                throw new Error(
                  'Formato no permitido. Usa PDF, XML, imágenes, Office, texto o el archivo M original.',
                );
              if (reemplazaA && motivo.trim().length < 5)
                throw new Error('Capture el motivo del reemplazo.');
              const extension = args.archivo.name.split('.').pop()?.toLowerCase();
              const mime =
                extension === 'xml'
                  ? 'application/xml'
                  : /^M[0-9]{7}\.[0-9]{3}$/i.test(args.archivo.name)
                    ? 'text/plain'
                    : args.archivo.type;
              const archivo = new File([args.archivo], args.archivo.name, {
                type: mime,
                lastModified: args.archivo.lastModified,
              });
              const a = await upload.subir({
                ...args,
                archivo,
                idempotencyKey: keyFor({
                  nombre: args.archivo.name,
                  size: args.archivo.size,
                  modified: args.archivo.lastModified,
                  tipo: args.tipoDocumentoId,
                  motivo,
                }),
              });
              await registrar(args.tipoDocumentoId, a.id);
            }}
            isUploading={upload.isUploading || accion.isPending}
            uploadProgress={upload.progress}
            modoBaja
            onDarDeBaja={async (adjuntoId, motivoBaja) => {
              await darBaja.mutateAsync({ adjuntoId, motivo: motivoBaja });
              await qc.invalidateQueries({ queryKey: ceKeys.detalle(id) });
            }}
            onDescargar={async (adjuntoId) => {
              await enlace.mutateAsync({ adjuntoId });
            }}
            resolverContenidoUrl={(a) => `${base}/adjuntos/${a.id}/contenido`}
            puedeVerBajas={permisoBaja}
            verBajas={verBajas}
            onVerBajasChange={setVerBajas}
          />
          {pendientes.length > 0 && (
            <div className="space-y-2">
              <p className="text-xs text-ink-muted">
                Archivos sin versión documental: una carga sola no satisface los requisitos.
              </p>
              {pendientes.map((a) => (
                <div key={a.id} className="flex items-center justify-between">
                  <span>{a.nombreArchivo}</span>
                  {subir && (
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => {
                        void registrar(a.tipoDocumentoId, a.id).catch(() => {});
                      }}
                    >
                      Registrar versión
                    </Button>
                  )}
                </div>
              ))}
            </div>
          )}
        </>
      )}
    </section>
  );
}
