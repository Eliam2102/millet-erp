import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
import { PaginacionTabla } from '@/components/erp';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { FormCampo, FormGrid } from '@/components/erp/forms/FormGrid';
import { estadosContenedor, estadosPartida } from '../lib/estados-ui';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { isValidElement, useState, type FormEvent, type ReactNode } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import {
  Sheet,
  SheetContent,
  SheetBody,
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
            Asigne cada partida y registre su embarque real para liberar la factura completa del
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
          <Table className="w-full text-sm">
            <TableHeader className="h-9 bg-surface-subtle text-left text-2xs uppercase text-ink-muted">
              <TableRow>
                {['Contenedor', 'BL o guía', 'Booking', 'Embarque real', 'Estado', 'Acciones'].map(
                  (t) => (
                    <TableHead key={t} className="px-3">
                      {t}
                    </TableHead>
                  ),
                )}
              </TableRow>
            </TableHeader>
            <TableBody>
              {d.contenedores.slice(pagina * 10, pagina * 10 + 10).map((c) => (
                <TableRow key={c.id} className="h-12 border-b border-line-row">
                  <TableCell className="px-3 font-mono text-xs">
                    {c.numero ?? 'Por asignar'}
                  </TableCell>
                  <TableCell className="px-3">{c.blGuia ?? 'Pendiente'}</TableCell>
                  <TableCell className="px-3">{c.booking ?? <span className="text-ink-muted">Sin booking</span>}</TableCell>
                  <TableCell className="px-3">
                    {c.fechaEmbarqueReal ? (
                      <DateTimeDisplay value={c.fechaEmbarqueReal} />
                    ) : (
                      'Pendiente'
                    )}
                  </TableCell>
                  <TableCell className="px-3">
                    <Badge variant={estadosContenedor[c.estado].tono}>
                      {estadosContenedor[c.estado].texto}
                    </Badge>
                    {c.motivoCancelacion && (
                      <p className="text-xs text-ink-muted">{c.motivoCancelacion}</p>
                    )}
                  </TableCell>
                  <TableCell className="px-3">
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
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
        <PaginacionTabla
          total={d.contenedores.length}
          offset={pagina * 10}
          limit={10}
          tamanos={[]}
          onChange={(offset) => setPagina(offset / 10)}
        />
      </div>
      {contenedor && (
        <div className="space-y-4 rounded-lg bg-surface-card p-4 shadow-card-flat">
          <FormCampo span={6} className="space-y-1.5">
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
                  {c.numero ?? <span className="text-ink-muted">Sin número</span>}
                </option>
              ))}
            </select>
          </FormCampo>
          <div className="overflow-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Pedido · cliente</TableHead>
                  <TableHead>Partida</TableHead>
                  <TableHead className="text-right">Cantidad</TableHead>
                  <TableHead>Estado</TableHead>
                  <TableHead>Factura</TableHead>
                  <TableHead>Acciones</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {d.partidas
                  .filter((p) => p.contenedorId === contenedor.id)
                  .map((p) => (
                    <TableRow key={p.id}>
                      <TableCell>
                        <span className="font-mono text-xs">
                          {pedido(p.pedidoFacturableId)?.numeroPedido ?? <span className="text-ink-muted">Sin número</span>}
                        </span>
                        <p className="text-xs text-ink-muted">
                          {pedido(p.pedidoFacturableId)?.clienteNombre}
                        </p>
                      </TableCell>
                      <TableCell className="tabular-nums">{p.posicion}</TableCell>
                      <TableCell className="text-right tabular-nums">
                        {p.cantidad} {p.unidad}
                      </TableCell>
                      <TableCell>
                        <Badge variant={estadosPartida[p.estado].tono}>
                          {estadosPartida[p.estado].texto}
                        </Badge>
                      </TableCell>
                      <TableCell className="font-mono text-xs">
                        {pedido(p.pedidoFacturableId)?.facturaFolio
                          ? `Facturado: ${pedido(p.pedidoFacturableId)?.facturaFolio} · ${pedido(p.pedidoFacturableId)?.facturaUuid ?? 'UUID pendiente'}`
                          : 'Sin factura vigente'}
                      </TableCell>
                      <TableCell>
                        {' '}
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
                              disabled={
                                mutar.isPending || pedido(p.pedidoFacturableId)?.facturaVigente
                              }
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
                      </TableCell>
                    </TableRow>
                  ))}
              </TableBody>
            </Table>
          </div>
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
                  <FormSeccion titulo="Datos de captura">
                    <FormCampo span={12}>
                      {accion === 'embarcar' ? (
                        <>
                          <FormCampo span={6} className="space-y-1.5">
                            <Label htmlFor="ce-fecha-embarque">
                              Fecha real de embarque <span aria-hidden="true">*</span>
                            </Label>
                            <Input
                              id="ce-fecha-embarque"
                              type="date"
                              required
                              value={fecha}
                              onChange={(e) => setFecha(e.target.value)}
                            />
                          </FormCampo>
                          <p className="text-xs text-ink-muted">
                            Requiere número de contenedor, BL o guía y pedimento de exportación
                            vigente.
                          </p>
                        </>
                      ) : (
                        <>
                          <FormCampo span={6} className="space-y-1.5">
                            <Label htmlFor="ce-motivo-contenedor">
                              Motivo de cancelación <span aria-hidden="true">*</span>
                            </Label>
                            <Input
                              id="ce-motivo-contenedor"
                              required
                              minLength={5}
                              maxLength={500}
                              value={motivo}
                              onChange={(e) => setMotivo(e.target.value)}
                            />
                          </FormCampo>
                          <p className="text-xs text-ink-muted">
                            Los pedidos conservan sus partidas. Reasígnelas a un contenedor activo
                            para facturar.
                          </p>
                        </>
                      )}
                    </FormCampo>
                    <FormCampo span={12}>
                      <Button
                        type="submit"
                        disabled={mutar.isPending}
                        title={mutar.isPending ? 'Guardando cambios.' : undefined}
                      >
                        {accion === 'embarcar' ? 'Confirmar embarque' : 'Confirmar cancelación'}
                      </Button>
                    </FormCampo>
                    <FormCampo span={12}>
                      <Button type="button" variant="ghost" onClick={() => setAccion(null)}>
                        Descartar
                      </Button>
                    </FormCampo>
                  </FormSeccion>
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
      <FormSeccion titulo="Datos de captura">
        <FormCampo span={12}>
          <h3 className="font-semibold">Pedimento de exportación</h3>
        </FormCampo>
        <FormCampo span={12}>
          <FormSeccion titulo="Datos de captura" disabled={!gestionar}>
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
          </FormSeccion>
        </FormCampo>
        <FormCampo span={12}>
          <p className="text-xs text-ink-muted">
            Al cambiar el pedimento queda revisión pendiente. Las facturas emitidas conservan su
            copia. La inclusión del número en el XML está pendiente de validación fiscal.
          </p>
        </FormCampo>
        <FormCampo span={12}>
          {mutar.isError && (
            <p role="alert" className="text-danger-fg">
              {esApiError(mutar.error)
                ? (mutar.error.problem.detail ?? mutar.error.problem.title)
                : 'No se pudo guardar el pedimento.'}
            </p>
          )}
        </FormCampo>
        <FormCampo span={12}>
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
        </FormCampo>
      </FormSeccion>
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
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(onClose);
  function cerrar() {
    solicitarCierre();
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
      <SheetContent size="lg">
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
          className="flex min-h-0 flex-1 flex-col"
          onChange={() => setDirty(true)}
        >
          <SheetBody className="space-y-5">
            <FormSeccion titulo="1. Transporte">
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
            </FormSeccion>
            <FormSeccion titulo="2. Carga">
              <Campo label="Fecha de carga prevista" id="ce-c-fecha">
                <Input
                  id="ce-c-fecha"
                  type="date"
                  value={datos.fechaCargaPrevista ?? ''}
                  onChange={(e) =>
                    setDatos({ ...datos, fechaCargaPrevista: e.target.value || null })
                  }
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
            </FormSeccion>
            {mutar.isError && (
              <p role="alert" className="text-danger-fg">
                {esApiError(mutar.error)
                  ? (mutar.error.problem.detail ?? mutar.error.problem.title)
                  : 'No se pudo guardar.'}
              </p>
            )}
          </SheetBody>
          <SheetFooter>
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
      {dialogo}
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
      <FormSeccion titulo="Datos de captura">
        <FormCampo span={12}>
          <h3 className="font-semibold">Asignar partida</h3>
        </FormCampo>
        <FormCampo span={12}>
          <FormGrid>
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
            <Campo label="Partida del pedido" id="ce-as-posicion">
              <select
                id="ce-as-posicion"
                className={selectClass}
                required
                value={posicion}
                onChange={(e) => setPosicion(e.target.value)}
              >
                <option value="">Seleccionar partida</option>
                {pedido?.lineas.map((l) => (
                  <option key={l.posicion} value={l.posicion}>
                    {l.posicion} · {l.descripcion} · {l.cantidad} {l.unidad}
                  </option>
                ))}
              </select>
            </Campo>
            <Campo label={`Cantidad (${linea?.unidad ?? 'Sin unidad'})`} id="ce-as-cantidad">
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
          </FormGrid>
        </FormCampo>
        <FormCampo span={12}>
          <p className="text-xs text-ink-muted">
            {linea
              ? `Pendiente por asignar: ${pendiente} ${linea.unidad}.`
              : 'Seleccione cliente, pedido y partida.'}{' '}
            {clientes.length === 0 ? 'Vincule el cliente en la pestaña Participantes.' : ''}
          </p>
        </FormCampo>
        <FormCampo span={12}>
          {q.isError && (
            <p role="alert" className="text-danger-fg">
              No se pudieron consultar los pedidos. Cambie de cliente para reintentar.
            </p>
          )}
        </FormCampo>
        <FormCampo span={12}>
          {mutar.isError && (
            <p role="alert" className="text-danger-fg">
              {esApiError(mutar.error)
                ? (mutar.error.problem.detail ?? mutar.error.problem.title)
                : 'No se pudo asignar.'}
            </p>
          )}
        </FormCampo>
        <FormCampo span={12}>
          <Button
            type="submit"
            variant="outline"
            disabled={mutar.isPending || !linea || pendiente <= 0}
            title={
              mutar.isPending
                ? 'Guardando partida.'
                : !linea
                  ? 'Seleccione una partida del pedido.'
                  : pendiente <= 0
                    ? 'La partida ya está asignada al 100 %.'
                    : undefined
            }
          >
            Asignar cantidad
          </Button>
        </FormCampo>
      </FormSeccion>
    </form>
  );
}
function Campo({ label, id, children }: { label: string; id: string; children: ReactNode }) {
  const requerido = isValidElement<{ required?: boolean }>(children) && children.props.required;
  return (
    <FormCampo span={6} className="space-y-1.5">
      <Label htmlFor={id}>
        {label}
        {requerido && <span aria-hidden="true"> *</span>}
      </Label>
      {children}
    </FormCampo>
  );
}
