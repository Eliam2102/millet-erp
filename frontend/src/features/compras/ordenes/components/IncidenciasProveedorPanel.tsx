import { useState } from 'react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { DateTimeDisplay, Field } from '@/components/erp';
import { FormCampo, FormGrid } from '@/components/erp/forms/FormGrid';
import { UsuarioSelector } from '@/components/erp/selectors/UsuarioSelector';
import { esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { hoyLocalISO } from '@/lib/datetime';
import {
  EstadoOrdenCompra,
  TipoIncidenciaProveedorOc,
  type IncidenciaProveedorOc,
  type OrdenCompraDetalleResponse,
} from '../api/types';
import {
  useAccionIncidenciaProveedor,
  type AccionIncidenciaArgs,
} from '../api/useIncidenciasProveedor';
import { TIPO_INCIDENCIA_LABEL } from '../lib/incidencias-proveedor';

export function EstadoIncidenciaBadge({ incidencia }: { incidencia: IncidenciaProveedorOc }) {
  if (!incidencia.abierta)
    return (
      <Badge variant={incidencia.cerradaPorRecepcion ? 'success' : 'neutral'}>
        {incidencia.cerradaPorRecepcion ? 'Cerrada: recibido' : 'Cerrada'}
      </Badge>
    );
  return incidencia.vencida ? (
    <Badge variant="danger">Compromiso vencido</Badge>
  ) : (
    <Badge variant="warning">Abierta</Badge>
  );
}

function mensajeError(error: unknown) {
  return esApiError(error)
    ? (error.problem.detail ?? error.problem.title)
    : 'No se pudo guardar. Vuelve a intentarlo.';
}

type DistributiveOmit<T, K extends PropertyKey> = T extends unknown ? Omit<T, K> : never;

/**
 * F1-COM-07: panel «Incidencias del proveedor» del detalle de OC. Faltante, dañado o atraso por
 * partida con responsable y fecha compromiso; reprogramar conserva las fechas anteriores.
 */
export function IncidenciasProveedorPanel({ oc }: { oc: OrdenCompraDetalleResponse }) {
  const puedeRegistrar = useHasPermission(
    PermisosCanonicos.ComprasOrdenesRegistrarIncidenciaProveedor,
  );
  const mutation = useAccionIncidenciaProveedor();
  const [agregando, setAgregando] = useState(false);
  const incidencias = oc.incidencias ?? [];
  const autorizada = oc.estado === EstadoOrdenCompra.Autorizada;
  if (!autorizada && incidencias.length === 0) return null;

  const partida = (lineaId: string) => {
    const l = oc.lineas.find((x) => x.id === lineaId);
    return l ? `Partida ${l.posicion} · ${l.articuloNombre ?? l.articuloClave ?? ''}` : 'Partida';
  };

  async function ejecutar(args: DistributiveOmit<AccionIncidenciaArgs, 'ordenCompraId' | 'idempotencyKey'>, ok: string) {
    await mutation.mutateAsync({
      ...args,
      ordenCompraId: oc.id,
      idempotencyKey: crypto.randomUUID(),
    } as AccionIncidenciaArgs);
    toast.success(ok);
  }

  const abiertas = incidencias.filter((i) => i.abierta).length;

  return (
    <section
      aria-label="Incidencias del proveedor"
      className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-md font-semibold">
          Incidencias del proveedor
          {incidencias.length > 0 && (
            <span className="ml-2 text-sm font-normal text-ink-muted">
              {abiertas} abierta{abiertas === 1 ? '' : 's'} de {incidencias.length}
            </span>
          )}
        </h2>
        {puedeRegistrar && !agregando && (
          <Button
            variant="secondary"
            disabled={!autorizada}
            title={autorizada ? undefined : 'Solo se registran incidencias en una orden autorizada.'}
            onClick={() => setAgregando(true)}
          >
            Registrar incidencia
          </Button>
        )}
      </div>
      <p className="text-xs text-ink-muted">
        La incidencia no cambia cantidades, recepción, factura ni pago. Se cierra sola cuando
        Almacén recibe lo pendiente de la partida.
      </p>

      {agregando && (
        <NuevaIncidenciaForm
          oc={oc}
          isPending={mutation.isPending}
          onCancel={() => setAgregando(false)}
          onSubmit={async (command) => {
            await ejecutar({ accion: 'registrar', command }, 'Incidencia registrada.');
            setAgregando(false);
          }}
        />
      )}

      {incidencias.length === 0 && !agregando && (
        <p className="text-sm text-ink-secondary">Sin incidencias registradas.</p>
      )}

      {incidencias.map((i) => (
        <IncidenciaItem
          key={i.id}
          incidencia={i}
          partida={partida(i.lineaOrdenCompraId)}
          puedeEditar={puedeRegistrar && i.abierta}
          isPending={mutation.isPending}
          onReprogramar={(command) =>
            ejecutar(
              { accion: 'reprogramar', incidenciaId: i.id, command },
              'Nueva fecha compromiso registrada.',
            )
          }
          onCerrar={(command) =>
            ejecutar({ accion: 'cerrar', incidenciaId: i.id, command }, 'Incidencia cerrada.')
          }
        />
      ))}
    </section>
  );
}

function NuevaIncidenciaForm({
  oc,
  isPending,
  onCancel,
  onSubmit,
}: {
  oc: OrdenCompraDetalleResponse;
  isPending: boolean;
  onCancel: () => void;
  onSubmit: (command: {
    lineaOrdenCompraId: string;
    tipo: TipoIncidenciaProveedorOc;
    cantidad: number;
    fechaCompromiso: string;
    responsableId: string;
    descripcion: string | null;
  }) => Promise<void>;
}) {
  const hoy = hoyLocalISO();
  const primera = oc.lineas.find((l) => l.cantidad - l.cantidadRecibida > 0) ?? oc.lineas[0];
  const [lineaId, setLineaId] = useState(primera?.id ?? '');
  const [tipo, setTipo] = useState<TipoIncidenciaProveedorOc>(TipoIncidenciaProveedorOc.Faltante);
  const linea = oc.lineas.find((l) => l.id === lineaId);
  const tope = (l = linea, t = tipo) =>
    l ? (t === TipoIncidenciaProveedorOc.Danado ? l.cantidadRecibida : l.cantidad - l.cantidadRecibida) : 0;
  const [cantidad, setCantidad] = useState(String(tope(primera) || ''));
  const [fecha, setFecha] = useState('');
  const [responsableId, setResponsableId] = useState<string | null>(oc.compradorTitularId);
  const [descripcion, setDescripcion] = useState('');
  const [error, setError] = useState<string | null>(null);

  async function enviar(e: React.FormEvent) {
    e.preventDefault();
    const cant = Number(cantidad);
    if (!lineaId || !fecha || !responsableId || !(cant > 0)) {
      setError('Completa partida, cantidad, fecha compromiso y responsable.');
      return;
    }
    setError(null);
    try {
      await onSubmit({
        lineaOrdenCompraId: lineaId,
        tipo,
        cantidad: cant,
        fechaCompromiso: fecha,
        responsableId,
        descripcion: descripcion.trim() || null,
      });
    } catch (err) {
      setError(mensajeError(err));
    }
  }

  return (
    <form
      onSubmit={enviar}
      noValidate
      aria-label="Registrar incidencia"
      className="space-y-3 rounded-md border border-dashed border-brand/40 bg-brand/5 p-3"
    >
      <FormGrid>
        <FormCampo span={6}>
          <Field label="Partida" required>
            <Select
              value={lineaId}
              onValueChange={(v) => {
                setLineaId(v);
                setCantidad(String(tope(oc.lineas.find((l) => l.id === v)) || ''));
              }}
            >
              <SelectTrigger aria-label="Partida">
                <SelectValue placeholder="Selecciona la partida" />
              </SelectTrigger>
              <SelectContent>
                {oc.lineas.map((l) => (
                  <SelectItem key={l.id} value={l.id}>
                    Partida {l.posicion} · {l.articuloNombre ?? l.articuloClave} (pendiente{' '}
                    {l.cantidad - l.cantidadRecibida} de {l.cantidad})
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>
        </FormCampo>
        <FormCampo span={3}>
          <Field label="Tipo" required>
            <Select
              value={String(tipo)}
              onValueChange={(v) => {
                const t = Number(v) as TipoIncidenciaProveedorOc;
                setTipo(t);
                setCantidad(String(tope(linea, t) || ''));
              }}
            >
              <SelectTrigger aria-label="Tipo de incidencia">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {Object.values(TipoIncidenciaProveedorOc).map((t) => (
                  <SelectItem key={t} value={String(t)}>
                    {TIPO_INCIDENCIA_LABEL[t]}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>
        </FormCampo>
        <FormCampo span={3}>
          <Field
            label="Cantidad afectada"
            required
            hint={
              tipo === TipoIncidenciaProveedorOc.Danado
                ? `Máximo lo recibido: ${tope()}`
                : `Máximo lo pendiente: ${tope()}`
            }
          >
            <Input
              type="number"
              min={0}
              step="any"
              inputMode="decimal"
              className="tabular-nums"
              value={cantidad}
              onChange={(e) => setCantidad(e.target.value)}
            />
          </Field>
        </FormCampo>
        <FormCampo span={3}>
          <Field label="Fecha compromiso" required>
            <Input type="date" min={hoy} value={fecha} onChange={(e) => setFecha(e.target.value)} />
          </Field>
        </FormCampo>
        <FormCampo span={3}>
          <Field label="Responsable del seguimiento" required>
            <UsuarioSelector value={responsableId} onChange={setResponsableId} />
          </Field>
        </FormCampo>
        <FormCampo span={6}>
          <Field label="Descripción" optional>
            <Textarea
              rows={2}
              maxLength={500}
              className="resize-none"
              placeholder="Qué pasó y qué acordaste con el proveedor"
              value={descripcion}
              onChange={(e) => setDescripcion(e.target.value)}
            />
          </Field>
        </FormCampo>
      </FormGrid>
      {error && (
        <p role="alert" className="text-sm text-danger-fg">
          {error}
        </p>
      )}
      <div className="flex justify-end gap-2">
        <Button type="button" variant="ghost" onClick={onCancel} disabled={isPending}>
          Cancelar
        </Button>
        <Button type="submit" disabled={isPending}>
          Registrar incidencia
        </Button>
      </div>
    </form>
  );
}

function IncidenciaItem({
  incidencia: i,
  partida,
  puedeEditar,
  isPending,
  onReprogramar,
  onCerrar,
}: {
  incidencia: IncidenciaProveedorOc;
  partida: string;
  puedeEditar: boolean;
  isPending: boolean;
  onReprogramar: (command: { fechaCompromiso: string; motivo: string }) => Promise<void>;
  onCerrar: (command: { motivo: string }) => Promise<void>;
}) {
  const [modo, setModo] = useState<'reprogramar' | 'cerrar' | null>(null);
  const [fecha, setFecha] = useState('');
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<string | null>(null);

  async function enviar(e: React.FormEvent) {
    e.preventDefault();
    if (!motivo.trim() || (modo === 'reprogramar' && !fecha)) {
      setError(modo === 'reprogramar' ? 'Indica la nueva fecha y el motivo.' : 'Indica el motivo del cierre.');
      return;
    }
    setError(null);
    try {
      if (modo === 'reprogramar') await onReprogramar({ fechaCompromiso: fecha, motivo: motivo.trim() });
      else await onCerrar({ motivo: motivo.trim() });
      setModo(null);
      setFecha('');
      setMotivo('');
    } catch (err) {
      setError(mensajeError(err));
    }
  }

  return (
    <div className="space-y-1 border-t border-line-divider pt-3 text-sm">
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-medium">
          Incidencia {i.numero} · {TIPO_INCIDENCIA_LABEL[i.tipo]}
        </span>
        <EstadoIncidenciaBadge incidencia={i} />
      </div>
      <p className="text-ink-secondary">
        {partida} · <span className="tabular-nums">{i.cantidad}</span> pza · Compromiso vigente:{' '}
        <DateTimeDisplay value={i.fechaCompromisoVigente} /> · Responsable:{' '}
        {i.responsableNombre ?? 'Usuario no disponible'}
      </p>
      {i.descripcion && <p className="text-ink-secondary">{i.descripcion}</p>}
      {i.avisoVencidaEnviadoEn && (
        <p className="text-xs text-ink-muted">
          Aviso de vencimiento enviado al responsable el{' '}
          <DateTimeDisplay value={i.avisoVencidaEnviadoEn} variant="datetime" />
        </p>
      )}
      {!i.abierta && (
        <p className="text-xs text-ink-muted">
          Cerrada el <DateTimeDisplay value={i.cerradaEn!} variant="datetime" />: {i.motivoCierre}
        </p>
      )}
      {i.compromisos.length > 1 && (
        <details>
          <summary className="cursor-pointer text-xs text-ink-secondary">
            Historial de compromisos ({i.compromisos.length})
          </summary>
          <ol className="mt-1 space-y-0.5 pl-4 text-xs text-ink-secondary">
            {i.compromisos.map((c) => (
              <li key={c.id}>
                <DateTimeDisplay value={c.fechaCompromiso} />
                {c.motivo ? ` · ${c.motivo}` : ' · compromiso inicial'} (registrado{' '}
                <DateTimeDisplay value={c.registradoEn} variant="datetime" />)
              </li>
            ))}
          </ol>
        </details>
      )}
      {puedeEditar && modo === null && (
        <div className="flex gap-2 pt-1">
          <Button size="sm" variant="secondary" onClick={() => setModo('reprogramar')}>
            Nueva fecha compromiso
          </Button>
          <Button size="sm" variant="secondary" onClick={() => setModo('cerrar')}>
            Cerrar incidencia
          </Button>
        </div>
      )}
      {modo && (
        <form
          onSubmit={enviar}
          noValidate
          aria-label={modo === 'reprogramar' ? 'Nueva fecha compromiso' : 'Cerrar incidencia'}
          className="mt-2 space-y-3 rounded-md border border-warning bg-warning-bg/40 p-3"
        >
          <FormGrid>
            {modo === 'reprogramar' && (
              <FormCampo span={6}>
                <Field label="Nueva fecha compromiso" required>
                  <Input
                    type="date"
                    min={hoyLocalISO()}
                    value={fecha}
                    onChange={(e) => setFecha(e.target.value)}
                  />
                </Field>
              </FormCampo>
            )}
            <FormCampo span={modo === 'reprogramar' ? 6 : 12}>
              <Field label={modo === 'reprogramar' ? 'Motivo del cambio' : 'Motivo del cierre'} required>
                <Input maxLength={300} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
              </Field>
            </FormCampo>
          </FormGrid>
          {error && (
            <p role="alert" className="text-sm text-danger-fg">
              {error}
            </p>
          )}
          <div className="flex justify-end gap-2">
            <Button type="button" variant="ghost" onClick={() => setModo(null)} disabled={isPending}>
              Cancelar
            </Button>
            <Button type="submit" disabled={isPending}>
              {modo === 'reprogramar' ? 'Guardar fecha' : 'Cerrar incidencia'}
            </Button>
          </div>
        </form>
      )}
    </div>
  );
}
