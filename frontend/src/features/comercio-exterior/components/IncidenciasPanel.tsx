import { useState, type FormEvent } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { FormCampo } from '@/components/erp/forms/FormGrid';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { UsuarioSelector } from '@/components/erp/selectors/UsuarioSelector';
import { AdjuntosManager } from '@/components/erp/adjuntos/AdjuntosManager';
import {
  useAdjuntos,
  useSubirAdjunto,
  useEnlaceAdjunto,
} from '@/components/erp/adjuntos/api/hooks';
import { EstadoAdjunto } from '@/components/erp/adjuntos/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { CE_BASE, useAccionExpediente, useContenedores } from '../api/hooks';
import {
  tiposIncidencia,
  estadosIncidencia,
  type ExpedienteDetalle,
  type Incidencia,
} from '../api/types';

const control = 'h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3';
export function IncidenciasPanel({ data }: { data: ExpedienteDetalle }) {
  const gestionar =
    useHasPermission(P.ComercioExteriorIncidenciasGestionar) && data.expediente.estado === 1;
  const [seleccion, setSeleccion] = useState<Incidencia | 'nueva' | null>(null);
  const incidencias = data.incidencias ?? [];
  const ver = useHasPermission(P.ComercioExteriorExpedientesAdjuntosVer);
  const verBajas = useHasPermission(P.ComercioExteriorExpedientesAdjuntosBaja);
  const archivos = useAdjuntos(`${CE_BASE}/${data.expediente.id}`, {
    enabled: ver,
    incluirBajas: verBajas,
  });
  const enlace = useEnlaceAdjunto(`${CE_BASE}/${data.expediente.id}`);
  return (
    <section className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-md font-semibold">Incidencias del expediente</h2>
        {gestionar && <Button onClick={() => setSeleccion('nueva')}>Registrar incidencia</Button>}
      </div>
      {incidencias.some((i) => i.estado < 3) && (
        <p role="note" className="rounded-md bg-warning-note-bg px-3 py-2.5 text-warning-note-fg">
          Resuelva o cancele las incidencias pendientes para completar el cierre físico, documental
          y financiero.
        </p>
      )}
      {!incidencias.length && <p className="text-ink-muted">Sin incidencias registradas.</p>}
      {incidencias.map((i) => (
        <article key={i.id} className="space-y-2 rounded-lg bg-surface-card p-4 shadow-card-flat">
          <div className="flex items-center justify-between gap-3">
            <h3 className="font-semibold">{tiposIncidencia[i.tipo]}</h3>
            <Badge variant={i.estado < 3 ? 'warning' : i.estado === 3 ? 'success' : 'neutral'}>
              {estadosIncidencia[i.estado]}
            </Badge>
          </div>
          <p>{i.motivo}</p>
          <p className="text-xs text-ink-muted">
            {i.fecha} · {i.responsableNombre} · {i.antiguedadDias} días
          </p>
          {i.descripcion && <p>{i.descripcion}</p>}
          {i.accionCorrectiva && <p>Acción correctiva: {i.accionCorrectiva}</p>}
          {i.efectoPosterior && <p>Efecto posterior: {i.efectoPosterior}</p>}
          {i.pedimentoNuevo && (
            <p>
              Pedimento anterior: {i.pedimentoAnterior} · Nuevo pedimento: {i.pedimentoNuevo}
            </p>
          )}
          {(i.contenedorId || i.partidaId) && (
            <ReferenciaAfectada expedienteId={data.expediente.id} incidencia={i} />
          )}
          <details>
            <summary className="cursor-pointer text-brand">Historial y referencias</summary>
            <p>
              Participante:{' '}
              {data.participantes.find((p) => p.id === i.participanteId)?.nombre ??
                'Sin participante'}
            </p>
            <p>Hito: {data.hitos.find((h) => h.clave === i.hitoClave)?.nombre ?? 'Sin hito'}</p>
            <p>
              {i.documentosIds.length} documentos afectados · {i.evidenciasIds.length} evidencias
            </p>
            <ul>
              {i.documentosIds.map((id) => {
                const d = data.documentos.find((doc) => doc.id === id);
                return (
                  <li key={id}>
                    {data.requisitos.find((r) => r.id === d?.tipoDocumentoId)?.nombre ??
                      'Documento afectado'}{' '}
                    · Versión {d?.numeroVersion}
                  </li>
                );
              })}
              {ver &&
                i.evidenciasIds.map((id) => (
                  <li key={id}>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => {
                        void enlace.mutateAsync({ adjuntoId: id }).catch(() => {});
                      }}
                    >
                      Descargar{' '}
                      {archivos.data?.find((a) => a.id === id)?.nombreArchivo ?? 'evidencia'}
                    </Button>
                  </li>
                ))}
              {i.historial.map((h, n) => (
                <li key={n}>
                  {estadosIncidencia[h.estado]} · {new Date(h.fecha).toLocaleString('es-MX')} ·{' '}
                  {h.motivo}
                </li>
              ))}
            </ul>
          </details>
          {gestionar && i.estado < 3 && (
            <Button variant="outline" onClick={() => setSeleccion(i)}>
              Atender o resolver
            </Button>
          )}
        </article>
      ))}
      {enlace.isError && (
        <p role="alert" className="text-danger-fg">
          No se pudo descargar la evidencia. Revise que el archivo siga disponible.
        </p>
      )}
      {seleccion && (
        <IncidenciaForm
          key={`${seleccion === 'nueva' ? 'nueva' : seleccion.id}-${data.expediente.version}`}
          data={data}
          incidencia={seleccion === 'nueva' ? undefined : seleccion}
          onClose={() => setSeleccion(null)}
        />
      )}
      <section aria-label="Avisos de incidencias" className="space-y-2">
        <h3 className="font-semibold">Avisos al responsable del expediente</h3>
        {(data.avisosIncidencias ?? []).map((a) => (
          <p key={a.id} className="text-xs text-ink-muted">
            {new Date(a.fecha).toLocaleString('es-MX')} ·{' '}
            {incidencias.find((i) => i.id === a.incidenciaId)?.motivo}
          </p>
        ))}
      </section>
    </section>
  );
}
function IncidenciaForm({
  data,
  incidencia,
  onClose,
}: {
  data: ExpedienteDetalle;
  incidencia?: Incidencia;
  onClose: () => void;
}) {
  const id = data.expediente.id;
  const base = `${CE_BASE}/${id}`;
  const accion = useAccionExpediente(id);
  const ver = useHasPermission(P.ComercioExteriorExpedientesAdjuntosVer);
  const subir = useHasPermission(P.ComercioExteriorExpedientesAdjuntosSubir);
  const usuarios = useHasPermission(P.IdentidadUsuariosLeer);
  const archivos = useAdjuntos(base, { enabled: ver });
  const upload = useSubirAdjunto(base);
  const enlace = useEnlaceAdjunto(base);
  const keyFor = useBodyScopedIdempotencyKey();
  const [tipo, setTipo] = useState(incidencia?.tipo ?? 5);
  const [estado, setEstado] = useState(incidencia?.estado === 2 ? 3 : 2);
  const [fecha, setFecha] = useState(
    new Intl.DateTimeFormat('sv-SE', { timeZone: 'America/Merida' }).format(new Date()),
  );
  const [responsable, setResponsable] = useState<string | null>(data.responsableUsuarioId);
  const [motivo, setMotivo] = useState('');
  const [descripcion, setDescripcion] = useState('');
  const [correctiva, setCorrectiva] = useState(incidencia?.accionCorrectiva ?? '');
  const [efecto, setEfecto] = useState(incidencia?.efectoPosterior ?? '');
  const [participante, setParticipante] = useState('');
  const [hito, setHito] = useState('');
  const [documentos, setDocumentos] = useState<string[]>([]);
  const [evidencias, setEvidencias] = useState<string[]>([]);
  const [pedimento, setPedimento] = useState('');
  const [adjuntoPedimento, setAdjuntoPedimento] = useState('');
  const [contenedor, setContenedor] = useState('');
  const [partida, setPartida] = useState('');
  const vigentes = (archivos.data ?? []).filter(
    (a) => !a.bajaEn && a.estado !== EstadoAdjunto.Vencido,
  );
  async function submit(e: FormEvent) {
    e.preventDefault();
    try {
      await accion.mutateAsync({
        accion: incidencia ? `incidencias/${incidencia.id}/estado` : 'incidencias',
        body: incidencia
          ? {
              estado,
              accionTomada: correctiva || null,
              efectoPosterior: efecto || null,
              evidenciasIds: evidencias,
              motivo: motivo || null,
              nuevoPedimento: pedimento || null,
              adjuntoPedimentoId: adjuntoPedimento || null,
            }
          : {
              tipo,
              fecha,
              responsableUsuarioId: responsable,
              motivo,
              descripcion: descripcion || null,
              accionCorrectiva: correctiva || null,
              efectoPosterior: efecto || null,
              participanteId: participante || null,
              hitoClave: hito || null,
              documentosIds: documentos,
              evidenciasIds: evidencias,
              contenedorId: contenedor || null,
              partidaId: partida || null,
            },
      });
      onClose();
    } catch {
      /* El error se muestra junto al formulario. */
    }
  }
  return (
    <div className="space-y-4 rounded-lg border border-dashed border-line-control p-4">
      <form onSubmit={submit}>
        <FormSeccion titulo={incidencia ? 'Seguimiento de incidencia' : 'Registrar incidencia'}>
          {!incidencia && (
            <>
              <FormCampo span={6}>
                <Label htmlFor="inc-tipo">Tipo *</Label>
                <select
                  id="inc-tipo"
                  className={control}
                  value={tipo}
                  onChange={(e) => setTipo(Number(e.target.value) as typeof tipo)}
                >
                  {Object.entries(tiposIncidencia).map(([v, l]) => (
                    <option key={v} value={v}>
                      {l}
                    </option>
                  ))}
                </select>
              </FormCampo>
              <FormCampo span={6}>
                <Label htmlFor="inc-fecha">Fecha *</Label>
                <Input
                  id="inc-fecha"
                  type="date"
                  required
                  value={fecha}
                  onChange={(e) => setFecha(e.target.value)}
                />
              </FormCampo>
              <FormCampo span={6}>
                <p id="inc-resp-label">Responsable *</p>
                <div role="group" aria-labelledby="inc-resp-label">
                  {usuarios ? (
                    <UsuarioSelector value={responsable} onChange={setResponsable} />
                  ) : (
                    <p>{data.expediente.responsableNombre}</p>
                  )}
                </div>
              </FormCampo>
              <FormCampo span={6}>
                <Label htmlFor="inc-participante">Participante que la originó</Label>
                <select
                  id="inc-participante"
                  className={control}
                  value={participante}
                  onChange={(e) => setParticipante(e.target.value)}
                >
                  <option value="">Sin participante</option>
                  {data.participantes.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.nombre}
                    </option>
                  ))}
                </select>
              </FormCampo>
              <FormCampo span={6}>
                <Label htmlFor="inc-hito">Hito afectado</Label>
                <select
                  id="inc-hito"
                  className={control}
                  value={hito}
                  onChange={(e) => setHito(e.target.value)}
                >
                  <option value="">Sin hito</option>
                  {data.hitos.map((h) => (
                    <option key={h.id} value={h.clave}>
                      {h.nombre}
                    </option>
                  ))}
                </select>
              </FormCampo>
              <FormCampo span={6}>
                <Label htmlFor="inc-desc">Descripción {tipo === 9 ? '*' : '(opcional)'}</Label>
                <Input
                  id="inc-desc"
                  required={tipo === 9}
                  maxLength={2000}
                  value={descripcion}
                  onChange={(e) => setDescripcion(e.target.value)}
                />
              </FormCampo>
              <FormCampo span={12}>
                <fieldset>
                  <legend>Documentos afectados</legend>
                  {data.documentos.map((d) => (
                    <label key={d.id} className="flex items-center gap-2">
                      <input
                        type="checkbox"
                        checked={documentos.includes(d.id)}
                        onChange={(e) =>
                          setDocumentos(
                            e.target.checked
                              ? [...documentos, d.id]
                              : documentos.filter((x) => x !== d.id),
                          )
                        }
                      />
                      {data.requisitos.find((r) => r.id === d.tipoDocumentoId)?.nombre} · Versión{' '}
                      {d.numeroVersion}
                    </label>
                  ))}
                </fieldset>
              </FormCampo>
              {data.expediente.tipo === 2 && (
                <ReferenciasEmbarque
                  id={id}
                  contenedor={contenedor}
                  partida={partida}
                  onContenedor={(v) => {
                    setContenedor(v);
                    setPartida('');
                  }}
                  onPartida={setPartida}
                />
              )}
            </>
          )}
          {incidencia && (
            <FormCampo span={6}>
              <Label htmlFor="inc-estado">Acción *</Label>
              <select
                id="inc-estado"
                className={control}
                value={estado}
                onChange={(e) => setEstado(Number(e.target.value))}
              >
                {incidencia.estado === 1 && <option value="2">En atención</option>}
                {incidencia.estado === 2 && <option value="3">Resolver</option>}
                <option value="4">Cancelar registro por error</option>
              </select>
            </FormCampo>
          )}
          {(!incidencia || estado === 4) && (
            <FormCampo span={12}>
              <Label htmlFor="inc-motivo">
                {incidencia ? 'Motivo de cancelación' : 'Motivo'} *
              </Label>
              <Input
                id="inc-motivo"
                required
                minLength={5}
                maxLength={500}
                value={motivo}
                onChange={(e) => setMotivo(e.target.value)}
              />
            </FormCampo>
          )}
          <FormCampo span={6}>
            <Label htmlFor="inc-accion">
              {incidencia && estado === 3 ? 'Acción tomada *' : 'Acción correctiva'}
            </Label>
            <Input
              id="inc-accion"
              required={!!incidencia && estado === 3}
              maxLength={2000}
              value={correctiva}
              onChange={(e) => setCorrectiva(e.target.value)}
            />
          </FormCampo>
          <FormCampo span={6}>
            <Label htmlFor="inc-efecto">Efecto posterior</Label>
            <Input
              id="inc-efecto"
              maxLength={2000}
              value={efecto}
              onChange={(e) => setEfecto(e.target.value)}
            />
          </FormCampo>
          <FormCampo span={12}>
            <fieldset>
              <legend>Evidencias {incidencia && estado === 3 ? '*' : '(opcional)'}</legend>
              {vigentes.map((a) => (
                <label key={a.id} className="flex items-center gap-2">
                  <input
                    type="checkbox"
                    checked={
                      evidencias.includes(a.id) || !!incidencia?.evidenciasIds.includes(a.id)
                    }
                    disabled={incidencia?.evidenciasIds.includes(a.id)}
                    onChange={(e) =>
                      setEvidencias(
                        e.target.checked
                          ? [...evidencias, a.id]
                          : evidencias.filter((x) => x !== a.id),
                      )
                    }
                  />
                  {a.nombreArchivo}
                </label>
              ))}
            </fieldset>
            <p className="text-xs text-ink-muted">
              Para resolver, seleccione al menos un archivo vigente. Puede cargarlo abajo.
            </p>
            {archivos.isError && <p role="alert">No se pudieron cargar las evidencias.</p>}
          </FormCampo>
          {incidencia?.tipo === 2 && estado === 3 && (
            <>
              <FormCampo span={6}>
                <Label htmlFor="inc-pedimento">Nuevo número de pedimento *</Label>
                <Input
                  id="inc-pedimento"
                  required
                  maxLength={100}
                  value={pedimento}
                  onChange={(e) => setPedimento(e.target.value)}
                />
              </FormCampo>
              <FormCampo span={6}>
                <Label htmlFor="inc-archivo-pedimento">Archivo del nuevo pedimento *</Label>
                <select
                  id="inc-archivo-pedimento"
                  className={control}
                  required
                  value={adjuntoPedimento}
                  onChange={(e) => setAdjuntoPedimento(e.target.value)}
                >
                  <option value="">Seleccione un archivo nuevo</option>
                  {vigentes
                    .filter(
                      (a) =>
                        data.requisitos.some(
                          (r) => r.id === a.tipoDocumentoId && r.codigo.endsWith('_PEDIMENTO'),
                        ) && !data.documentos.some((d) => d.adjuntoId === a.id),
                    )
                    .map((a) => (
                      <option key={a.id} value={a.id}>
                        {a.nombreArchivo}
                      </option>
                    ))}
                </select>
              </FormCampo>
            </>
          )}
          <FormCampo span={12}>
            {accion.isError && (
              <p role="alert" className="text-danger-fg">
                {esApiError(accion.error)
                  ? (accion.error.problem.detail ?? accion.error.problem.title)
                  : 'No se pudo guardar la incidencia.'}
              </p>
            )}
            <div className="flex gap-2">
              <Button
                type="submit"
                disabled={accion.isPending || !responsable}
                title={
                  accion.isPending
                    ? 'Guardando incidencia.'
                    : !responsable
                      ? 'Seleccione un responsable.'
                      : undefined
                }
              >
                Guardar incidencia
              </Button>
              <Button type="button" variant="ghost" onClick={onClose}>
                Descartar
              </Button>
            </div>
          </FormCampo>
        </FormSeccion>
      </form>
      {ver && (
        <AdjuntosManager
          adjuntos={(archivos.data ?? []).map((a) => ({
            ...a,
            fechaCarga: a.subidoEn,
            usuarioCargaId: a.subidoPorId,
          }))}
          tipos={data.requisitos.map((r) => ({ id: r.id, clave: r.codigo, descripcion: r.nombre }))}
          canUpload={subir}
          canRemove={false}
          adjuntosLoading={archivos.isLoading}
          isUploading={upload.isUploading}
          uploadProgress={upload.progress}
          onUpload={async (args) => {
            const a = await upload.subir({
              ...args,
              idempotencyKey: keyFor({
                nombre: args.archivo.name,
                size: args.archivo.size,
                modified: args.archivo.lastModified,
                tipo: args.tipoDocumentoId,
              }),
            });
            setEvidencias((prev) => [...prev, a.id]);
          }}
          onDescargar={async (adjuntoId) => {
            await enlace.mutateAsync({ adjuntoId });
          }}
          resolverContenidoUrl={(a) => `${base}/adjuntos/${a.id}/contenido`}
        />
      )}
    </div>
  );
}
function ReferenciasEmbarque({
  id,
  contenedor,
  partida,
  onContenedor,
  onPartida,
}: {
  id: string;
  contenedor: string;
  partida: string;
  onContenedor: (v: string) => void;
  onPartida: (v: string) => void;
}) {
  const q = useContenedores(id);
  return (
    <>
      <FormCampo span={6}>
        <Label htmlFor="inc-contenedor">Contenedor afectado</Label>
        <select
          id="inc-contenedor"
          className={control}
          value={contenedor}
          onChange={(e) => onContenedor(e.target.value)}
        >
          <option value="">Sin contenedor</option>
          {q.data?.data.contenedores.map((c) => (
            <option key={c.id} value={c.id}>
              {c.numero ?? 'Sin número'}
            </option>
          ))}
        </select>
      </FormCampo>
      <FormCampo span={6}>
        <Label htmlFor="inc-partida">Partida afectada</Label>
        <select
          id="inc-partida"
          className={control}
          value={partida}
          onChange={(e) => onPartida(e.target.value)}
        >
          <option value="">Sin partida</option>
          {q.data?.data.partidas
            .filter((p) => !contenedor || p.contenedorId === contenedor)
            .map((p) => (
              <option key={p.id} value={p.id}>
                Partida {p.posicion} · {p.cantidad} {p.unidad}
              </option>
            ))}
        </select>
      </FormCampo>
      {q.isError && <p role="alert">No se pudieron cargar los contenedores y partidas.</p>}
    </>
  );
}

function ReferenciaAfectada({
  expedienteId,
  incidencia,
}: {
  expedienteId: string;
  incidencia: Incidencia;
}) {
  const q = useContenedores(expedienteId);
  if (q.isLoading) return <p role="status">Consultando referencia afectada…</p>;
  if (q.isError)
    return <p role="alert">No se pudo consultar el contenedor o la partida afectada.</p>;
  const contenedor = q.data?.data.contenedores.find((c) => c.id === incidencia.contenedorId);
  const partida = q.data?.data.partidas.find((p) => p.id === incidencia.partidaId);
  return (
    <p className="text-xs text-ink-muted">
      {incidencia.contenedorId && `Contenedor afectado: ${contenedor?.numero ?? 'Sin número'}. `}
      {incidencia.partidaId &&
        `Partida afectada: ${partida ? `${partida.posicion} · ${partida.cantidad} ${partida.unidad}` : 'Retirada del embarque; se conserva la referencia en el historial'}.`}
    </p>
  );
}
