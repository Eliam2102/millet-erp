import { useState, type FormEvent, type ReactNode } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetDescription,
  SheetFooter,
} from '@/components/ui/sheet';
import { DateTimeDisplay, ErrorState } from '@/components/erp';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { useContenedores, useMutarContenedor, usePedidosEmbarcables } from '../api/hooks';
import type {
  Contenedor,
  ContenedoresDetalle,
  DatosContenedor,
  ExpedienteDetalle,
} from '../api/types';

const selectClass =
  'h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3 text-sm';
const vacio: DatosContenedor = {
  numero: '',
  sello: '',
  booking: '',
  blGuia: '',
  ruta: '',
  fechaCargaPrevista: null,
  pesoKg: null,
  bultos: null,
};
export function ContenedoresPanel({ data }: { data: ExpedienteDetalle }) {
  const id = data.expediente.id;
  const query = useContenedores(id);
  const mutar = useMutarContenedor(id);
  const gestionar =
    useHasPermission(P.ComercioExteriorContenedoresGestionar) && data.expediente.estado === 1;
  const [edicion, setEdicion] = useState<Contenedor | 'nuevo' | null>(null);
  const [seleccion, setSeleccion] = useState('');
  const [pagina, setPagina] = useState(0);
  const [fecha, setFecha] = useState('');
  const [motivo, setMotivo] = useState('');
  const [accion, setAccion] = useState<'embarcar' | 'cancelar' | null>(null);
  const d = query.data?.data;
  if (query.isError)
    return (
      <ErrorState
        title="No se pudieron cargar los contenedores"
        onRetry={() => {
          void query.refetch();
        }}
      />
    );
  if (!d) return <p role="status">Cargando contenedores…</p>;
  const activos = d.contenedores.filter((c) => c.estado === 1);
  const contenedor = activos.find((c) => c.id === seleccion) ?? activos[0];
  const pedido = (pid: string) => d.pedidos.find((p) => p.id === pid);
  const error = mutar.isError
    ? esApiError(mutar.error)
      ? (mutar.error.problem.detail ?? mutar.error.problem.title)
      : 'No se pudo guardar el contenedor.'
    : null;
  async function registrar(e: FormEvent) {
    e.preventDefault();
    if (!contenedor || !accion) return;
    try {
      await mutar.mutateAsync({
        ruta: `contenedores/${contenedor.id}/${accion}`,
        body: accion === 'embarcar' ? { fecha } : { motivo },
      });
      setAccion(null);
      setMotivo('');
    } catch {
      /* Error visible en el panel. */
    }
  }
  return (
    <section className="space-y-5">
      <div className="flex items-center justify-between gap-3">
        <div>
          <h2 className="text-md font-semibold">Contenedores y partidas</h2>
          <p className="text-xs text-ink-muted">
            Asigne cada posición y registre su embarque real para liberar la factura completa del
            pedido.
          </p>
        </div>
        {gestionar && <Button onClick={() => setEdicion('nuevo')}>Agregar contenedor</Button>}
      </div>
      <PedimentoForm
        key={`${d.version}-pedimento`}
        id={id}
        d={d}
        gestionar={gestionar}
        documentos={data}
      />
      {error && (
        <p role="alert" className="text-danger-fg">
          {error}
        </p>
      )}
      <div className="overflow-hidden rounded-t-lg bg-surface-card shadow-card-flat">
        <div className="flex h-11 items-center border-b border-line-divider px-4 text-ink-muted">
          {d.contenedores.length} contenedores
        </div>
        <div className="overflow-auto">
          <table className="w-full text-sm">
            <thead className="h-9 bg-surface-subtle text-left text-2xs uppercase text-ink-muted">
              <tr>
                {['Contenedor', 'BL o guía', 'Booking', 'Embarque real', 'Estado', 'Acciones'].map(
                  (t) => (
                    <th key={t} className="px-3">
                      {t}
                    </th>
                  ),
                )}
              </tr>
            </thead>
            <tbody>
              {d.contenedores.slice(pagina * 10, pagina * 10 + 10).map((c) => (
                <tr key={c.id} className="h-12 border-b border-line-row">
                  <td className="px-3 font-mono text-xs">{c.numero ?? 'Por asignar'}</td>
                  <td className="px-3">{c.blGuia ?? 'Pendiente'}</td>
                  <td className="px-3">{c.booking ?? 'Sin booking'}</td>
                  <td className="px-3">
                    {c.fechaEmbarqueReal ? (
                      <DateTimeDisplay value={c.fechaEmbarqueReal} />
                    ) : (
                      'Pendiente'
                    )}
                  </td>
                  <td className="px-3">
                    <Badge variant={c.estado === 2 ? 'neutral' : 'info'}>
                      {c.estado === 2 ? 'Cancelado' : 'Activo'}
                    </Badge>
                    {c.motivoCancelacion && (
                      <p className="text-xs text-ink-muted">{c.motivoCancelacion}</p>
                    )}
                  </td>
                  <td className="px-3">
                    {gestionar && c.estado === 1 && (
                      <Button
                        size="sm"
                        variant="ghost"
                        onClick={() => {
                          setSeleccion(c.id);
                          setEdicion(c);
                        }}
                      >
                        Editar contenedor
                      </Button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className="flex h-11 items-center justify-between border-t border-line-divider px-4 text-xs text-ink-muted">
          <span>
            {d.contenedores.length ? pagina * 10 + 1 : 0}–
            {Math.min((pagina + 1) * 10, d.contenedores.length)} de {d.contenedores.length}
          </span>
          <div className="flex gap-2">
            <Button
              size="sm"
              variant="outline"
              disabled={!pagina}
              title={!pagina ? 'Primera página.' : undefined}
              onClick={() => setPagina(pagina - 1)}
            >
              Anterior
            </Button>
            <Button
              size="sm"
              variant="outline"
              disabled={(pagina + 1) * 10 >= d.contenedores.length}
              title={(pagina + 1) * 10 >= d.contenedores.length ? 'Última página.' : undefined}
              onClick={() => setPagina(pagina + 1)}
            >
              Siguiente
            </Button>
          </div>
        </div>
      </div>
      {contenedor && (
        <div className="space-y-4 rounded-lg bg-surface-card p-4 shadow-card-flat">
          <Label htmlFor="ce-contenedor-seleccion">Consultar partidas del contenedor</Label>
          <select
            id="ce-contenedor-seleccion"
            className={selectClass}
            value={contenedor.id}
            onChange={(e) => {
              setSeleccion(e.target.value);
              setAccion(null);
            }}
          >
            {activos.map((c) => (
              <option key={c.id} value={c.id}>
                {c.numero ?? '[CONTENEDOR SIN NÚMERO]'}
              </option>
            ))}
          </select>
          <ul className="divide-y divide-line-row">
            {d.partidas
              .filter((p) => p.contenedorId === contenedor.id)
              .map((p) => (
                <li key={p.id} className="flex flex-wrap items-center justify-between gap-3 py-3">
                  <div>
                    <p>
                      {pedido(p.pedidoFacturableId)?.clienteNombre} ·{' '}
                      <span className="font-mono text-xs">
                        {pedido(p.pedidoFacturableId)?.numeroPedido ?? '[PEDIDO]'}
                      </span>{' '}
                      · posición {p.posicion} · {p.cantidad} {p.unidad}
                    </p>
                    <Badge
                      variant={p.estado === 3 ? 'neutral' : p.estado === 2 ? 'warning' : 'info'}
                    >
                      {p.estado === 3
                        ? 'Liberada por cancelación del expediente'
                        : p.estado === 2
                          ? 'Revisión: pedido modificado en A+W'
                          : 'Vigente'}
                    </Badge>
                    <p className="text-xs text-ink-muted">
                      {pedido(p.pedidoFacturableId)?.facturaFolio
                        ? `Facturado: ${pedido(p.pedidoFacturableId)?.facturaFolio} · ${pedido(p.pedidoFacturableId)?.facturaUuid ?? 'UUID pendiente'}`
                        : 'Sin factura vigente'}
                    </p>
                  </div>
                  {gestionar && p.estado !== 3 && (
                    <div className="flex gap-2">
                      {p.estado === 2 && (
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={mutar.isPending}
                          title={mutar.isPending ? 'Guardando confirmación.' : undefined}
                          onClick={() => {
                            void mutar
                              .mutateAsync({ ruta: `partidas/${p.id}/confirmar` })
                              .catch(() => {});
                          }}
                        >
                          Confirmar partida
                        </Button>
                      )}
                      <Button
                        size="sm"
                        variant="ghost"
                        disabled={mutar.isPending || pedido(p.pedidoFacturableId)?.facturaVigente}
                        title={
                          pedido(p.pedidoFacturableId)?.facturaVigente
                            ? 'La partida tiene una factura vigente.'
                            : mutar.isPending
                              ? 'Guardando cambios.'
                              : undefined
                        }
                        onClick={() => {
                          void mutar
                            .mutateAsync({ ruta: `partidas/${p.id}`, method: 'DELETE' })
                            .catch(() => {});
                        }}
                      >
                        Retirar partida
                      </Button>
                    </div>
                  )}
                </li>
              ))}
          </ul>
          {gestionar && (
            <>
              <AsignarPartida key={contenedor.id} data={data} d={d} contenedorId={contenedor.id} />
              <div className="flex gap-2">
                <Button
                  variant="outline"
                  disabled={Boolean(contenedor.fechaEmbarqueReal)}
                  title={
                    contenedor.fechaEmbarqueReal
                      ? 'El embarque real ya está registrado.'
                      : undefined
                  }
                  onClick={() => setAccion('embarcar')}
                >
                  Registrar embarque real
                </Button>
                <Button variant="ghost" onClick={() => setAccion('cancelar')}>
                  Cancelar contenedor o booking
                </Button>
              </div>
              {accion && (
                <form
                  onSubmit={registrar}
                  className="space-y-3 rounded-lg border border-dashed border-line-control p-4"
                >
                  {accion === 'embarcar' ? (
                    <>
                      <Label htmlFor="ce-fecha-embarque">Fecha real de embarque</Label>
                      <Input
                        id="ce-fecha-embarque"
                        type="date"
                        required
                        value={fecha}
                        onChange={(e) => setFecha(e.target.value)}
                      />
                      <p className="text-xs text-ink-muted">
                        Requiere número de contenedor, BL o guía y pedimento de exportación vigente.
                      </p>
                    </>
                  ) : (
                    <>
                      <Label htmlFor="ce-motivo-contenedor">Motivo de cancelación</Label>
                      <Input
                        id="ce-motivo-contenedor"
                        required
                        minLength={5}
                        maxLength={500}
                        value={motivo}
                        onChange={(e) => setMotivo(e.target.value)}
                      />
                      <p className="text-xs text-ink-muted">
                        Los pedidos conservan sus partidas. Reasígnelas a un contenedor activo para
                        facturar.
                      </p>
                    </>
                  )}
                  <Button
                    type="submit"
                    disabled={mutar.isPending}
                    title={mutar.isPending ? 'Guardando cambios.' : undefined}
                  >
                    {accion === 'embarcar' ? 'Confirmar embarque' : 'Confirmar cancelación'}
                  </Button>
                  <Button type="button" variant="ghost" onClick={() => setAccion(null)}>
                    Descartar
                  </Button>
                </form>
              )}
            </>
          )}
        </div>
      )}
      {edicion && (
        <ContenedorForm
          key={edicion === 'nuevo' ? 'nuevo' : edicion.id}
          id={id}
          inicial={edicion}
          onClose={() => setEdicion(null)}
        />
      )}
    </section>
  );
}
function PedimentoForm({
  id,
  d,
  gestionar,
  documentos,
}: {
  id: string;
  d: ContenedoresDetalle;
  gestionar: boolean;
  documentos: ExpedienteDetalle;
}) {
  const [p, setP] = useState(
    d.pedimento ?? { numero: '', clave: 'A1', fecha: '', documentoId: null },
  );
  const mutar = useMutarContenedor(id);
  const tipoId = documentos.requisitos.find((r) => r.codigo === 'EXP_PEDIMENTO')?.id;
  const docs = documentos.documentos.filter(
    (doc) => doc.tipoDocumentoId === tipoId && doc.estado === 1 && !doc.noAplica,
  );
  return (
    <form
      className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat"
      onSubmit={(e) => {
        e.preventDefault();
        void mutar
          .mutateAsync({ ruta: 'pedimento-exportacion', method: 'PUT', body: p })
          .catch(() => {});
      }}
    >
      <h3 className="font-semibold">Pedimento de exportación</h3>
      <fieldset disabled={!gestionar} className="grid grid-cols-2 gap-4">
        <Campo label="Origen del número" id="ce-ped-doc">
          <select
            id="ce-ped-doc"
            className={selectClass}
            value={p.documentoId ?? ''}
            onChange={(e) =>
              setP({
                ...p,
                documentoId: e.target.value || null,
                numero: docs.find((d) => d.id === e.target.value)?.numeroPedimento ?? '',
              })
            }
          >
            <option value="">Captura del expediente</option>
            {docs.map((doc) => (
              <option key={doc.id} value={doc.id}>
                Versión documental {doc.numeroVersion} ·{' '}
                {doc.numeroPedimento ?? 'Sin número capturado'}
              </option>
            ))}
          </select>
        </Campo>
        <Campo label="Número de pedimento" id="ce-ped-numero">
          <Input
            id="ce-ped-numero"
            required
            maxLength={100}
            readOnly={Boolean(p.documentoId)}
            value={p.numero}
            onChange={(e) => setP({ ...p, numero: e.target.value })}
          />
        </Campo>
        <Campo label="Clave de pedimento" id="ce-ped-clave">
          <select
            id="ce-ped-clave"
            className={selectClass}
            value={p.clave}
            onChange={(e) => setP({ ...p, clave: e.target.value })}
          >
            <option value="A1">A1 · Con CCE</option>
            <option value="RT">RT · Sin CCE</option>
          </select>
        </Campo>
        <Campo label="Fecha del pedimento" id="ce-ped-fecha">
          <Input
            id="ce-ped-fecha"
            required
            type="date"
            value={p.fecha}
            onChange={(e) => setP({ ...p, fecha: e.target.value })}
          />
        </Campo>
      </fieldset>
      <p className="text-xs text-ink-muted">
        Al cambiar el pedimento queda revisión pendiente. Las facturas emitidas conservan su copia.
        Número en XML: Por confirmar con Fiscal (VAL-CE-08).
      </p>
      {mutar.isError && (
        <p role="alert" className="text-danger-fg">
          {esApiError(mutar.error)
            ? (mutar.error.problem.detail ?? mutar.error.problem.title)
            : 'No se pudo guardar el pedimento.'}
        </p>
      )}
      {gestionar && (
        <Button
          type="submit"
          variant="outline"
          disabled={mutar.isPending}
          title={mutar.isPending ? 'Guardando pedimento.' : undefined}
        >
          Guardar pedimento
        </Button>
      )}
    </form>
  );
}
function ContenedorForm({
  id,
  inicial,
  onClose,
}: {
  id: string;
  inicial: Contenedor | 'nuevo';
  onClose: () => void;
}) {
  const [datos, setDatos] = useState<DatosContenedor>(inicial === 'nuevo' ? vacio : inicial);
  const mutar = useMutarContenedor(id);
  const [dirty, setDirty] = useState(false);
  function cerrar() {
    if (!dirty || window.confirm('Hay cambios sin guardar. ¿Descartarlos?')) onClose();
  }
  async function submit(e: FormEvent) {
    e.preventDefault();
    const body = {
      ...datos,
      id: undefined,
      estado: undefined,
      motivoCancelacion: undefined,
      fechaEmbarqueReal: undefined,
    };
    try {
      await mutar.mutateAsync({
        ruta: inicial === 'nuevo' ? 'contenedores' : `contenedores/${inicial.id}`,
        method: inicial === 'nuevo' ? 'POST' : 'PATCH',
        body,
      });
      onClose();
    } catch {
      /* Error visible. */
    }
  }
  return (
    <Sheet
      open
      onOpenChange={(open) => {
        if (!open) cerrar();
      }}
    >
      <SheetContent className="flex w-full flex-col sm:max-w-[560px]">
        <SheetHeader>
          <SheetTitle>
            {inicial === 'nuevo' ? 'Agregar contenedor' : 'Editar contenedor'}
          </SheetTitle>
          <SheetDescription>
            Referencias de transporte del expediente de exportación.
          </SheetDescription>
        </SheetHeader>
        <form
          onSubmit={submit}
          className="flex min-h-0 flex-1 flex-col gap-4 overflow-auto px-4"
          onChange={() => setDirty(true)}
        >
          <fieldset className="grid grid-cols-2 gap-x-4 gap-y-3.5">
            <legend className="mb-3 text-sm font-semibold">1. Transporte</legend>
            {(
              [
                ['numero', 'Número de contenedor'],
                ['sello', 'Sello'],
                ['booking', 'Booking'],
                ['blGuia', 'BL o guía'],
                ['ruta', 'Ruta'],
              ] as const
            ).map(([k, label]) => (
              <Campo key={k} label={label} id={`ce-c-${k}`}>
                <Input
                  id={`ce-c-${k}`}
                  maxLength={200}
                  value={datos[k] ?? ''}
                  onChange={(e) => setDatos({ ...datos, [k]: e.target.value || null })}
                />
              </Campo>
            ))}
          </fieldset>
          <fieldset className="grid grid-cols-2 gap-x-4 gap-y-3.5">
            <legend className="mb-3 text-sm font-semibold">2. Carga</legend>
            <Campo label="Fecha de carga prevista" id="ce-c-fecha">
              <Input
                id="ce-c-fecha"
                type="date"
                value={datos.fechaCargaPrevista ?? ''}
                onChange={(e) => setDatos({ ...datos, fechaCargaPrevista: e.target.value || null })}
              />
            </Campo>
            <Campo label="Peso (kg)" id="ce-c-peso">
              <Input
                id="ce-c-peso"
                type="number"
                min="0"
                step="0.000001"
                value={datos.pesoKg ?? ''}
                onChange={(e) =>
                  setDatos({ ...datos, pesoKg: e.target.value ? Number(e.target.value) : null })
                }
              />
            </Campo>
            <Campo label="Bultos" id="ce-c-bultos">
              <Input
                id="ce-c-bultos"
                type="number"
                min="0"
                step="1"
                value={datos.bultos ?? ''}
                onChange={(e) =>
                  setDatos({ ...datos, bultos: e.target.value ? Number(e.target.value) : null })
                }
              />
            </Campo>
          </fieldset>
          {mutar.isError && (
            <p role="alert" className="text-danger-fg">
              {esApiError(mutar.error)
                ? (mutar.error.problem.detail ?? mutar.error.problem.title)
                : 'No se pudo guardar.'}
            </p>
          )}
          <SheetFooter className="mt-auto border-t border-line-divider py-4">
            <Button type="button" variant="ghost" onClick={cerrar}>
              Cancelar
            </Button>
            <Button
              type="submit"
              disabled={mutar.isPending}
              title={mutar.isPending ? 'Guardando contenedor.' : undefined}
            >
              Guardar contenedor
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
function AsignarPartida({
  data,
  d,
  contenedorId,
}: {
  data: ExpedienteDetalle;
  d: ContenedoresDetalle;
  contenedorId: string;
}) {
  const [cliente, setCliente] = useState('');
  const [pedidoId, setPedido] = useState('');
  const [posicion, setPosicion] = useState('');
  const [cantidad, setCantidad] = useState('');
  const q = usePedidosEmbarcables(data.expediente.id, cliente);
  const mutar = useMutarContenedor(data.expediente.id);
  const clientes = data.participantes.filter(
    (p, i, all) => p.clienteId && all.findIndex((x) => x.clienteId === p.clienteId) === i,
  );
  const pedido = q.data?.find((p) => p.id === pedidoId);
  const linea = pedido?.lineas.find((l) => l.posicion === Number(posicion));
  const pendiente = linea
    ? linea.cantidad -
      d.partidas
        .filter(
          (p) =>
            p.pedidoFacturableId === pedidoId &&
            p.posicion === linea.posicion &&
            p.estado !== 3 &&
            d.contenedores.some((c) => c.id === p.contenedorId && c.estado === 1),
        )
        .reduce((s, p) => s + p.cantidad, 0)
    : 0;
  return (
    <form
      className="space-y-3 rounded-lg border border-dashed border-line-control p-4"
      onSubmit={(e) => {
        e.preventDefault();
        void mutar
          .mutateAsync({
            ruta: `contenedores/${contenedorId}/partidas`,
            body: {
              pedidoFacturableId: pedidoId,
              posicion: Number(posicion),
              cantidad: Number(cantidad),
            },
          })
          .then(() => setCantidad(''))
          .catch(() => {});
      }}
    >
      <h3 className="font-semibold">Asignar partida</h3>
      <div className="grid grid-cols-2 gap-4">
        <Campo label="Cliente del expediente" id="ce-as-cliente">
          <select
            id="ce-as-cliente"
            className={selectClass}
            required
            value={cliente}
            onChange={(e) => {
              setCliente(e.target.value);
              setPedido('');
              setPosicion('');
            }}
          >
            <option value="">Seleccionar cliente</option>
            {clientes.map((c) => (
              <option key={c.clienteId} value={c.clienteId!}>
                {c.nombre}
              </option>
            ))}
          </select>
        </Campo>
        <Campo label="Pedido de exportación" id="ce-as-pedido">
          <select
            id="ce-as-pedido"
            className={selectClass}
            required
            value={pedidoId}
            onChange={(e) => {
              setPedido(e.target.value);
              setPosicion('');
            }}
          >
            <option value="">Seleccionar pedido</option>
            {q.data?.map((p) => (
              <option key={p.id} value={p.id}>
                {p.numeroPedido}
              </option>
            ))}
          </select>
        </Campo>
        <Campo label="Posición del pedido" id="ce-as-posicion">
          <select
            id="ce-as-posicion"
            className={selectClass}
            required
            value={posicion}
            onChange={(e) => setPosicion(e.target.value)}
          >
            <option value="">Seleccionar posición</option>
            {pedido?.lineas.map((l) => (
              <option key={l.posicion} value={l.posicion}>
                {l.posicion} · {l.descripcion} · {l.cantidad} {l.unidad}
              </option>
            ))}
          </select>
        </Campo>
        <Campo label={`Cantidad (${linea?.unidad ?? '[UNIDAD]'})`} id="ce-as-cantidad">
          <Input
            id="ce-as-cantidad"
            required
            type="number"
            min="0.000001"
            max={pendiente}
            step="0.000001"
            value={cantidad}
            onChange={(e) => setCantidad(e.target.value)}
          />
        </Campo>
      </div>
      <p className="text-xs text-ink-muted">
        {linea
          ? `Pendiente por asignar: ${pendiente} ${linea.unidad}.`
          : 'Seleccione cliente, pedido y posición.'}{' '}
        {clientes.length === 0 ? 'Vincule el cliente en la pestaña Participantes.' : ''}
      </p>
      {q.isError && (
        <p role="alert" className="text-danger-fg">
          No se pudieron consultar los pedidos. Cambie de cliente para reintentar.
        </p>
      )}
      {mutar.isError && (
        <p role="alert" className="text-danger-fg">
          {esApiError(mutar.error)
            ? (mutar.error.problem.detail ?? mutar.error.problem.title)
            : 'No se pudo asignar.'}
        </p>
      )}
      <Button
        type="submit"
        variant="outline"
        disabled={mutar.isPending || !linea || pendiente <= 0}
        title={
          mutar.isPending
            ? 'Guardando partida.'
            : !linea
              ? 'Seleccione una posición del pedido.'
              : pendiente <= 0
                ? 'La posición ya está asignada al 100 %.'
                : undefined
        }
      >
        Asignar cantidad
      </Button>
    </form>
  );
}
function Campo({ label, id, children }: { label: string; id: string; children: ReactNode }) {
  return (
    <div className="space-y-1.5">
      <Label htmlFor={id}>{label}</Label>
      {children}
    </div>
  );
}
