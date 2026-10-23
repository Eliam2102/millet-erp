import { FormCampo } from '@/components/erp/forms/FormGrid';
import { estadosDocumento } from '../lib/estados-ui';
import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
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
          ...(!noAplica && numeroPedimento ? { numeroPedimento } : {}),
        },
      });
      setMotivo('');
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
      <div className="flex items-center justify-between">
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
            La versión anterior y su archivo se conservan. Captura el motivo antes de subir una
            corrección.
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
                  {r.obligatorio ? 'Obligatorio' : r.condicional ? 'Condicional' : 'Opcional'}
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
            canUpload={subir}
            canRemove={baja}
            adjuntosLoading={archivos.isLoading}
            adjuntosError={archivos.isError ? 'No se pudieron cargar los archivos.' : null}
            onUpload={async (args) => {
              const anterior = data.documentos.some(
                (d) => d.tipoDocumentoId === args.tipoDocumentoId,
              );
              if (anterior && motivo.trim().length < 5)
                throw new Error(
                  'Capture un motivo de al menos 5 caracteres antes de subir una corrección.',
                );
              const a = await upload.subir({
                ...args,
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
