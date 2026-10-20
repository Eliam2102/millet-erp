import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { FormCampo } from '@/components/erp/forms/FormGrid';
import { estadosHito, textoFinanciero } from '../lib/estados-ui';
import { useState, type FormEvent } from 'react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { DateTimeDisplay } from '@/components/erp';
import { UsuarioSelector } from '@/components/erp/selectors/UsuarioSelector';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { EstadoAdjunto } from '@/components/erp/adjuntos/api/types';
import { useAdjuntos } from '@/components/erp/adjuntos/api/hooks';
import { CE_BASE, useAccionExpediente } from '../api/hooks';
import { EstadoExpediente, type ExpedienteDetalle, type Hito } from '../api/types';

export function HitosPanel({ data }: { data: ExpedienteDetalle }) {
  const puedeRegistrar = useHasPermission(P.ComercioExteriorExpedientesHitosRegistrar);
  const [seleccion, setSeleccion] = useState<Hito | null>(null);
  return (
    <section className="space-y-4">
      <h2 className="text-md font-semibold">Hitos de la operación</h2>
      <p role="note" className="rounded-md bg-warning-note-bg px-3 py-2.5 text-warning-note-fg">
        Financiero: {textoFinanciero(data.estadoFinanciero)}. El hito Cerrado se habilitará al
        completar esa dimensión.
      </p>
      <ol className="grid grid-cols-1 gap-4 lg:grid-cols-3">
        {data.hitos.map((h) => (
          <li key={h.id} className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <h3 className="flex items-center gap-2 font-medium">
                <span
                  aria-hidden="true"
                  className={`flex size-6 shrink-0 items-center justify-center rounded-full ${h.fechaReal ? 'bg-success-bg text-success-fg' : h.atrasado ? 'bg-danger-bg text-danger-fg' : h.omitido ? 'bg-neutral-bg text-neutral-fg' : 'bg-warning-bg text-warning-fg'}`}
                >
                  {h.fechaReal ? '✓' : h.omitido ? '—' : '!'}
                </span>
                {h.orden}. {h.nombre}
              </h3>
              <Badge
                variant={
                  estadosHito[
                    h.omitido
                      ? 'omitido'
                      : h.fechaReal
                        ? 'registrado'
                        : h.atrasado
                          ? 'atrasado'
                          : 'pendiente'
                  ].tono
                }
              >
                {h.omitido
                  ? 'Omitido'
                  : h.fechaReal
                    ? 'Registrado'
                    : h.atrasado
                      ? `${h.diasAtraso} ${h.diasAtraso === 1 ? 'día' : 'días'} de atraso`
                      : 'Pendiente'}
              </Badge>
            </div>
            <dl className="grid grid-cols-2 gap-3">
              <div>
                <dt className="text-xs text-ink-muted">Compromiso</dt>
                <dd className={h.atrasado ? 'font-medium text-danger-fg' : ''}>
                  {h.fechaCompromiso ? (
                    <DateTimeDisplay value={h.fechaCompromiso} />
                  ) : (
                    'Sin compromiso'
                  )}
                </dd>
              </div>
              <div>
                <dt className="text-xs text-ink-muted">Real</dt>
                <dd>{h.fechaReal ? <DateTimeDisplay value={h.fechaReal} /> : 'Sin fecha real'}</dd>
              </div>
              <div>
                <dt className="text-xs text-ink-muted">Responsable</dt>
                <dd>{h.responsableNombre}</dd>
              </div>
              <div>
                <dt className="text-xs text-ink-muted">Evidencia</dt>
                <dd>{h.evidenciaAdjuntoId ? 'Adjunto del expediente' : 'Sin evidencia'}</dd>
              </div>
            </dl>
            {h.motivo && <p className="text-xs text-ink-secondary">Motivo: {h.motivo}</p>}
            {h.motivoCambioCompromiso && (
              <p className="text-xs text-ink-secondary">
                Cambio de compromiso: {h.motivoCambioCompromiso}
              </p>
            )}
            {puedeRegistrar &&
              data.expediente.estado === EstadoExpediente.Abierto &&
              !h.fechaReal &&
              !h.omitido &&
              (h.clave === 'cerrado' ? (
                <Button
                  variant="outline"
                  size="sm"
                  disabled
                  title={textoFinanciero(data.estadoFinanciero)}
                >
                  Registrar Cerrado
                </Button>
              ) : (
                <Button variant="outline" size="sm" onClick={() => setSeleccion(h)}>
                  Actualizar {h.nombre}
                </Button>
              ))}
            {seleccion?.id === h.id && (
              <HitoForm
                key={`${h.id}-${data.expediente.version}`}
                hito={h}
                data={data}
                onClose={() => setSeleccion(null)}
              />
            )}
          </li>
        ))}
      </ol>
      <section className="space-y-3" aria-label="Avisos de hitos vencidos">
        <h3 className="font-semibold">Avisos de hitos vencidos</h3>
        <p className="text-xs text-ink-muted">
          Un aviso por hito y fecha compromiso. Los avisos anteriores se conservan al registrar la
          fecha real o reprogramar.
        </p>
        {data.avisos.length === 0 ? (
          <p className="text-ink-muted">Sin avisos registrados.</p>
        ) : (
          <ul className="divide-y divide-line-row">
            {data.avisos.map((a) => (
              <li key={a.id} className="py-2 text-ink-strong">
                {a.hitoNombre} · Compromiso: <DateTimeDisplay value={a.fechaCompromiso} /> · Aviso:{' '}
                <DateTimeDisplay value={a.fechaAviso} />
              </li>
            ))}
          </ul>
        )}
      </section>
    </section>
  );
}

function HitoForm({
  hito,
  data,
  onClose,
}: {
  hito: Hito;
  data: ExpedienteDetalle;
  onClose: () => void;
}) {
  const [operacion, setOperacion] = useState('compromiso');
  const [fecha, setFecha] = useState(hito.fechaCompromiso ?? '');
  const [motivo, setMotivo] = useState('');
  const [responsable, setResponsable] = useState<string | null>(hito.responsableUsuarioId);
  const [evidencia, setEvidencia] = useState('');
  const puedeLeerUsuarios = useHasPermission(P.IdentidadUsuariosLeer);
  const puedeVerAdjuntos = useHasPermission(P.ComercioExteriorExpedientesAdjuntosVer);
  const adjuntos = useAdjuntos(`${CE_BASE}/${data.expediente.id}`, { enabled: puedeVerAdjuntos });
  const accion = useAccionExpediente(data.expediente.id);
  const requiereMotivo =
    operacion === 'omitir' ||
    (operacion === 'compromiso' && hito.fechaCompromiso !== null && fecha !== hito.fechaCompromiso);
  async function submit(event: FormEvent) {
    event.preventDefault();
    try {
      await accion.mutateAsync({
        accion: `hitos/${hito.clave}/${operacion}`,
        body: {
          clave: hito.clave,
          fechaCompromiso: operacion === 'compromiso' ? fecha : null,
          fechaReal: operacion === 'real' ? fecha : null,
          responsableUsuarioId: responsable,
          evidenciaAdjuntoId: operacion === 'real' && evidencia ? evidencia : null,
          motivo: motivo.trim() || null,
        },
      });
      onClose();
    } catch {
      /* El error se muestra en el formulario. */
    }
  }
  return (
    <form
      onSubmit={submit}
      className="space-y-3 rounded-lg border border-dashed border-line-control p-4"
    >
      <FormSeccion titulo="Actualizar hito">
        <FormCampo span={6} className="space-y-1.5">
          <Label htmlFor={`ce-h-operacion-${hito.id}`}>Acción del hito</Label>
          <select
            id={`ce-h-operacion-${hito.id}`}
            className="h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3"
            value={operacion}
            onChange={(e) => {
              setOperacion(e.target.value);
              setFecha('');
            }}
          >
            <option value="compromiso">Fecha compromiso</option>
            <option value="real">Registrar fecha real</option>
            {![
              'solicitado',
              'documentacion-completa',
              'despacho-aduanal',
              'entregado',
              'cerrado',
            ].includes(hito.clave) && <option value="omitir">Omitir con motivo</option>}
          </select>
        </FormCampo>
        {operacion !== 'omitir' && (
          <>
            <FormCampo span={6} className="space-y-1.5">
              <Label htmlFor={`ce-h-fecha-${hito.id}`}>
                {operacion === 'real' ? 'Fecha real' : 'Fecha compromiso'}
                <span aria-hidden="true">*</span>
              </Label>
              <Input
                id={`ce-h-fecha-${hito.id}`}
                type="date"
                required
                value={fecha}
                onChange={(e) => setFecha(e.target.value)}
              />
            </FormCampo>
          </>
        )}
        <FormCampo span={12}>
          <p id={`ce-h-responsable-${hito.id}`} className="text-xs font-medium text-ink-strong">
            Responsable del hito
          </p>
        </FormCampo>
        <FormCampo span={12}>
          <div role="group" aria-labelledby={`ce-h-responsable-${hito.id}`}>
            {puedeLeerUsuarios ? (
              <UsuarioSelector value={responsable} onChange={setResponsable} />
            ) : (
              <p>{hito.responsableNombre}</p>
            )}
          </div>
        </FormCampo>
        {operacion === 'real' && puedeVerAdjuntos && (
          <>
            <FormCampo span={6} className="space-y-1.5">
              <Label htmlFor={`ce-h-evidencia-${hito.id}`}>Evidencia (opcional)</Label>
              <select
                id={`ce-h-evidencia-${hito.id}`}
                className="h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3"
                value={evidencia}
                onChange={(e) => setEvidencia(e.target.value)}
              >
                <option value="">Sin evidencia</option>
                {adjuntos.data
                  ?.filter((a) => !a.bajaEn && a.estado !== EstadoAdjunto.Vencido)
                  .map((a) => (
                    <option key={a.id} value={a.id}>
                      {a.nombreArchivo}
                    </option>
                  ))}
              </select>
            </FormCampo>
            {adjuntos.isError && (
              <p role="alert" className="text-danger-fg">
                No se pudieron cargar los adjuntos.
              </p>
            )}
          </>
        )}
        <FormCampo span={6} className="space-y-1.5">
          <Label htmlFor={`ce-h-motivo-${hito.id}`}>
            Motivo {requiereMotivo ? '(obligatorio, 5 a 500 caracteres)' : '(opcional)'}{' '}
            {requiereMotivo && <span aria-hidden="true">*</span>}
          </Label>
          <Input
            id={`ce-h-motivo-${hito.id}`}
            required={requiereMotivo}
            minLength={5}
            maxLength={500}
            value={motivo}
            onChange={(e) => setMotivo(e.target.value)}
          />
        </FormCampo>
        <FormCampo span={12}>
          {accion.isError && (
            <p role="alert" className="text-danger-fg">
              {esApiError(accion.error)
                ? (accion.error.problem.detail ?? accion.error.problem.title)
                : 'No se pudo actualizar el hito.'}
            </p>
          )}
        </FormCampo>
        <FormCampo span={12}>
          <div className="flex gap-2">
            <Button
              type="submit"
              disabled={accion.isPending || !responsable}
              title={
                accion.isPending
                  ? 'Guardando hito.'
                  : !responsable
                    ? 'Seleccione un responsable.'
                    : undefined
              }
            >
              Guardar hito
            </Button>
            <Button type="button" variant="ghost" onClick={onClose}>
              Descartar
            </Button>
          </div>
        </FormCampo>
      </FormSeccion>
    </form>
  );
}
