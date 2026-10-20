import {
  Table,
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
} from '@/components/ui/table';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { FormCampo, FormGrid } from '@/components/erp/forms/FormGrid';
import { textoFinanciero } from '../lib/estados-ui';
import { PageHeader } from '@/components/erp/PageHeader';
import { useState, type FormEvent } from 'react';
import { Link } from '@tanstack/react-router';
import { useQuery } from '@tanstack/react-query';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/tabs';
import { ErrorState, DateTimeDisplay, Breadcrumbs } from '@/components/erp';
import { IncotermSelector } from '@/components/erp/selectors/IncotermSelector';
import { MonedaSelector } from '@/components/erp/selectors/MonedaSelector';
import { ReferenciaSelector } from '../components/ReferenciaSelector';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { apiRequest, esApiError } from '@/lib/api';
import { CE_BASE, useAccionExpediente, useExpediente, useExpedientes } from '../api/hooks';
import { EstadoExpedienteBadge } from '../components/EstadoExpedienteBadge';
import { OtrPanel } from '../components/OtrPanel';
import { ContenedoresPanel } from '../components/ContenedoresPanel';
import { IncidenciasPanel } from '../components/IncidenciasPanel';
import { HitosPanel } from '../components/HitosPanel';
import { DocumentosPanel } from '../components/DocumentosPanel';
import {
  tiposParticipante,
  type ExpedienteDetalle,
  type Participante,
  type TipoParticipante,
} from '../api/types';
import {
  Select,
  SelectTrigger,
  SelectValue,
  SelectContent,
  SelectItem,
} from '@/components/ui/select';

export function ExpedienteDetallePage({ id }: { id: string }) {
  const query = useExpediente(id);
  const lista = useExpedientes({});
  const data = query.data?.data;
  const editar = useHasPermission(P.ComercioExteriorExpedientesEditar);
  const cerrar = useHasPermission(P.ComercioExteriorExpedientesCerrar);
  const cancelar = useHasPermission(P.ComercioExteriorExpedientesCancelar);
  const accion = useAccionExpediente(id);
  const [edicion, setEdicion] = useState(false);
  const [cancelando, setCancelando] = useState(false);
  const [motivo, setMotivo] = useState('');
  if (query.isError)
    return (
      <ErrorState
        title="No se pudo cargar el expediente"
        problem={esApiError(query.error) ? query.error.problem : undefined}
        onRetry={() => {
          void query.refetch();
        }}
      />
    );
  if (!data)
    return (
      <p role="status" className="p-6 text-sm text-ink-muted">
        Cargando expediente…
      </p>
    );
  const abierto = data.expediente.estado === 1;
  return (
    <div className="flex h-full min-h-0 gap-5 text-sm">
      <aside
        className="hidden w-80 shrink-0 overflow-auto border-r border-line bg-surface-card lg:block"
        data-print="hidden"
      >
        <div className="border-b border-line-divider p-4">
          <Link to="/comercio-exterior/expedientes" className="text-brand">
            Todos los expedientes
          </Link>
        </div>
        {lista.data?.items.map((e) => (
          <Link
            key={e.id}
            to="/comercio-exterior/expedientes/$id"
            params={{ id: e.id }}
            className={`block border-b border-line-row px-3.5 py-3 ${e.id === id ? 'bg-surface-selected shadow-list-selected' : ''}`}
          >
            <span className="font-mono text-xs text-brand">{e.folio}</span>
            <span className="mt-1 block truncate">{e.referencia}</span>
          </Link>
        ))}
      </aside>
      <main className="flex min-w-0 flex-1 flex-col gap-5 overflow-auto">
        <Breadcrumbs
          items={[
            { label: 'Comercio Exterior' },
            { label: 'Expedientes', to: '/comercio-exterior/expedientes' },
            { label: data.expediente.folio },
          ]}
        />
        <PageHeader
          mono
          titulo={data.expediente.folio}
          descripcion={data.expediente.referencia}
          estado={
            <>
              {' '}
              <EstadoExpedienteBadge estado={data.expediente.estado} />
              <Badge variant="warning">
                {(data.incidencias ?? []).filter((i) => i.estado < 3).length} incidencias pendientes
              </Badge>
              {data.expediente.revisionPendiente && (
                <Badge variant="warning">Revisión pendiente</Badge>
              )}
            </>
          }
          acciones={
            <>
              {' '}
              {editar && abierto && (
                <Button variant="outline" onClick={() => setEdicion(!edicion)}>
                  Editar datos
                </Button>
              )}
              {cerrar && abierto && (
                <Button
                  variant="outline"
                  disabled
                  title={
                    (data.incidencias ?? [])
                      .filter((i) => i.estado < 3)
                      .map((i) => i.motivo)
                      .join('; ') || textoFinanciero(data.estadoFinanciero)
                  }
                >
                  Cerrar expediente
                </Button>
              )}
              {cancelar && abierto && (
                <Button variant="ghost" onClick={() => setCancelando(!cancelando)}>
                  Cancelar expediente
                </Button>
              )}
            </>
          }
        />
        {cancelando && (
          <form
            className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat"
            onSubmit={(e) => {
              e.preventDefault();
              void accion
                .mutateAsync({ accion: 'cancelar', body: { motivo } })
                .then(() => setCancelando(false))
                .catch(() => {});
            }}
          >
            <FormSeccion titulo="Datos de captura">
              <FormCampo span={6} className="space-y-1.5">
                <Label htmlFor="ce-cancelar">
                  Motivo de cancelación (5 a 500 caracteres) <span aria-hidden="true">*</span>
                </Label>
                <Input
                  id="ce-cancelar"
                  required
                  minLength={5}
                  maxLength={500}
                  value={motivo}
                  onChange={(e) => setMotivo(e.target.value)}
                />
              </FormCampo>
              <FormCampo span={12}>
                <p className="text-xs text-ink-muted">
                  Se conserva el expediente; la OC y los pedidos continúan su proceso.
                </p>
              </FormCampo>
              <FormCampo span={12}>
                <Button
                  type="submit"
                  disabled={accion.isPending}
                  title={accion.isPending ? 'Guardando cancelación.' : undefined}
                >
                  Confirmar cancelación
                </Button>
              </FormCampo>
            </FormSeccion>
          </form>
        )}
        {accion.isError && (
          <p role="alert" className="text-danger-fg">
            {esApiError(accion.error)
              ? (accion.error.problem.detail ?? accion.error.problem.title)
              : 'No se pudo guardar.'}
          </p>
        )}
        {edicion && (
          <EditarDatos
            key={data.expediente.version}
            data={data}
            onClose={() => setEdicion(false)}
          />
        )}
        <dl className="grid grid-cols-2 gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat xl:grid-cols-4">
          {[
            ['Tipo', data.expediente.tipo === 1 ? 'Importación' : 'Exportación'],
            ['Sucursal', data.expediente.sucursalNombre],
            ['Incoterm', data.incotermCodigo],
            ['Moneda', data.expediente.moneda],
            ['Origen', data.ocFolio ?? 'Solicitud de contenedor'],
            ['Responsable', data.expediente.responsableNombre],
            ['Documentación', data.estadoDocumental],
            ['Físico', data.estadoFisico],
            ['Financiero', textoFinanciero(data.estadoFinanciero)],
            ['TC', data.tc === null ? 'Sin TC capturado' : String(data.tc)],
          ].map(([label, value]) => (
            <div key={label}>
              <dt className="text-xs text-ink-muted">{label}</dt>
              <dd className="mt-1 font-medium">{value}</dd>
            </div>
          ))}
        </dl>
        <Tabs defaultValue="resumen">
          <TabsList>
            <TabsTrigger value="resumen">Resumen</TabsTrigger>
            <TabsTrigger value="participantes">Participantes</TabsTrigger>
            <TabsTrigger value="documentos">Documentos</TabsTrigger>
            <TabsTrigger value="incidencias">Incidencias</TabsTrigger>
            <TabsTrigger value="hitos">Hitos</TabsTrigger>
            {data.expediente.tipo === 2 && (
              <>
                <TabsTrigger value="contenedores">Contenedores</TabsTrigger>
                <TabsTrigger value="otr">A+W OTR</TabsTrigger>
              </>
            )}
            <TabsTrigger value="bitacora">Bitácora</TabsTrigger>
          </TabsList>
          <TabsContent value="resumen" className="space-y-4">
            <h2 className="text-md font-semibold">Datos de la operación</h2>
            {data.motivoCancelacion && <p>Cancelación: {data.motivoCancelacion}</p>}
            {(data.ocId || data.informacionImportacion) && (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Orden de compra</TableHead>
                    <TableHead>País de origen</TableHead>
                    <TableHead>Contenedor</TableHead>
                    <TableHead>Ruta</TableHead>
                    <TableHead>Pedimento</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  <TableRow>
                    <TableCell className="font-mono text-xs">
                      {data.ocFolio ?? 'Sin número'}
                    </TableCell>
                    <TableCell>
                      {data.informacionImportacion?.paisOrigen ?? (
                        <span className="text-ink-muted">Sin país de origen</span>
                      )}
                    </TableCell>
                    <TableCell className="font-mono text-xs">
                      {data.informacionImportacion?.numeroContenedor ?? (
                        <span className="text-ink-muted">Sin número</span>
                      )}
                    </TableCell>
                    <TableCell>
                      {data.informacionImportacion?.codigoRuta ?? (
                        <span className="text-ink-muted">Sin ruta</span>
                      )}
                    </TableCell>
                    <TableCell className="font-mono text-xs">
                      {data.informacionImportacion?.numeroPedimento ?? (
                        <span className="text-ink-muted">Sin número</span>
                      )}
                    </TableCell>
                  </TableRow>
                </TableBody>
              </Table>
            )}
            {data.fechaTc && (
              <p>
                Fecha del TC: <DateTimeDisplay value={data.fechaTc} /> · Fuente: {data.fuenteTc}
              </p>
            )}
            <p className="text-xs text-ink-muted">
              Consulta la fuente capturada del tipo de cambio y los requisitos documentales de la
              operación.
            </p>
          </TabsContent>
          <TabsContent value="participantes">
            <Participantes data={data} puedeEditar={editar && abierto} />
          </TabsContent>
          <TabsContent value="documentos">
            <DocumentosPanel data={data} />
          </TabsContent>
          <TabsContent value="incidencias">
            <IncidenciasPanel data={data} />
          </TabsContent>
          <TabsContent value="hitos">
            <HitosPanel data={data} />
          </TabsContent>
          {data.expediente.tipo === 2 && (
            <TabsContent value="contenedores">
              <ContenedoresPanel data={data} />
            </TabsContent>
          )}
          {data.expediente.tipo === 2 && (
            <TabsContent value="otr">
              <OtrPanel expedienteId={id} abierto={abierto} />
            </TabsContent>
          )}
          <TabsContent value="bitacora">
            <Bitacora id={id} />
          </TabsContent>
        </Tabs>
      </main>
    </div>
  );
}
function EditarDatos({ data, onClose }: { data: ExpedienteDetalle; onClose: () => void }) {
  const [datos, setDatos] = useState({
    referencia: data.expediente.referencia,
    incotermId: data.incotermId,
    moneda: data.expediente.moneda,
    responsableUsuarioId: data.responsableUsuarioId,
    tc: data.tc,
    fechaTc: data.fechaTc,
    fuenteTc: data.fuenteTc,
  });
  const accion = useAccionExpediente(data.expediente.id);
  return (
    <form
      className="space-y-3 rounded-lg border border-dashed border-line-control p-4"
      onSubmit={(e) => {
        e.preventDefault();
        void accion
          .mutateAsync({ accion: '', method: 'PATCH', body: datos })
          .then(onClose)
          .catch(() => {});
      }}
    >
      <FormSeccion titulo="Datos de captura">
        <FormCampo span={6} className="space-y-1.5">
          <Label htmlFor="ce-ed-ref">
            Referencia <span aria-hidden="true">*</span>
          </Label>
          <Input
            id="ce-ed-ref"
            required
            maxLength={200}
            value={datos.referencia}
            onChange={(e) => setDatos({ ...datos, referencia: e.target.value })}
          />
        </FormCampo>
        <FormCampo span={6} className="space-y-1.5">
          <Label>Incoterm</Label>
          <IncotermSelector
            value={datos.incotermId}
            onChange={(id) => setDatos({ ...datos, incotermId: id ?? '' })}
          />
        </FormCampo>
        <FormCampo span={6} className="space-y-1.5">
          <Label>Moneda</Label>
          <MonedaSelector
            value={datos.moneda}
            onChange={(m) => setDatos({ ...datos, moneda: m ?? '' })}
          />
        </FormCampo>
        <FormCampo span={6} className="space-y-1.5">
          <Label htmlFor="ce-ed-tc">TC (opcional)</Label>
          <Input
            id="ce-ed-tc"
            type="number"
            min="0.000001"
            step="0.000001"
            value={datos.tc ?? ''}
            onChange={(e) =>
              setDatos({
                ...datos,
                tc: e.target.value ? Number(e.target.value) : null,
                ...(e.target.value ? {} : { fechaTc: null, fuenteTc: null }),
              })
            }
          />
        </FormCampo>
        {datos.tc !== null && (
          <>
            <FormCampo span={6} className="space-y-1.5">
              <Label htmlFor="ce-ed-fecha">
                Fecha del TC <span aria-hidden="true">*</span>
              </Label>
              <Input
                id="ce-ed-fecha"
                type="date"
                required
                value={datos.fechaTc ?? ''}
                onChange={(e) => setDatos({ ...datos, fechaTc: e.target.value })}
              />
            </FormCampo>
            <FormCampo span={6} className="space-y-1.5">
              <Label htmlFor="ce-ed-fuente">
                Fuente del TC <span aria-hidden="true">*</span>
              </Label>
              <Input
                id="ce-ed-fuente"
                required
                maxLength={200}
                value={datos.fuenteTc ?? ''}
                onChange={(e) => setDatos({ ...datos, fuenteTc: e.target.value })}
              />
            </FormCampo>
          </>
        )}
        <FormCampo span={12}>
          {accion.isError && (
            <p role="alert" className="text-danger-fg">
              {esApiError(accion.error)
                ? (accion.error.problem.detail ?? accion.error.problem.title)
                : 'No se pudo guardar.'}
            </p>
          )}
        </FormCampo>
        <FormCampo span={12}>
          <div className="flex gap-2">
            <Button
              type="submit"
              disabled={accion.isPending}
              title={accion.isPending ? 'Guardando datos.' : undefined}
            >
              Guardar datos
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
function Participantes({ data, puedeEditar }: { data: ExpedienteDetalle; puedeEditar: boolean }) {
  const vacio = {
    id: '',
    nombre: '',
    tipo: 9 as TipoParticipante,
    descripcion: '',
    contacto: '',
    papel: 1 as 1 | 2 | 3,
    proveedorId: null as string | null,
    clienteId: null as string | null,
  };
  const [p, setP] = useState(vacio);
  const accion = useAccionExpediente(data.expediente.id);
  async function submit(e: FormEvent) {
    e.preventDefault();
    try {
      await accion.mutateAsync({
        accion: p.id ? `participantes/${p.id}` : 'participantes',
        method: p.id ? 'PATCH' : 'POST',
        body: { ...p, id: undefined },
      });
      setP(vacio);
    } catch {
      /* Error visible. */
    }
  }
  function editar(participante: Participante) {
    setP({
      ...participante,
      contacto: participante.contacto ?? '',
      descripcion: participante.descripcion ?? '',
    });
  }
  return (
    <section className="space-y-4">
      <h2 className="text-md font-semibold">Participantes de la operación</h2>
      <ul className="divide-y divide-line-row">
        {data.participantes.map((p) => (
          <li key={p.id} className="flex items-start justify-between py-3">
            <div>
              <p className="font-medium">{p.nombre}</p>
              <p className="text-xs text-ink-muted">
                {tiposParticipante[p.tipo]} · {['', 'Captura', 'Revisa', 'Autoriza'][p.papel]} ·{' '}
                {p.contacto ?? 'Sin contacto capturado'}
              </p>
              {p.descripcion && <p className="text-xs text-ink-muted">{p.descripcion}</p>}
            </div>
            {puedeEditar && (
              <Button size="sm" variant="ghost" onClick={() => editar(p)}>
                Editar participante
              </Button>
            )}
          </li>
        ))}
      </ul>
      {puedeEditar && (
        <form
          onSubmit={submit}
          className="space-y-3 rounded-lg border border-dashed border-line-control p-4"
        >
          <FormSeccion titulo="Datos de captura">
            <FormCampo span={12}>
              <h3 className="font-medium">
                {p.id ? 'Editar participante' : 'Agregar participante'}
              </h3>
            </FormCampo>
            <FormCampo span={12}>
              <FormGrid>
                {(['nombre', 'contacto', 'descripcion'] as const).map((k) => (
                  <FormCampo key={k} span={6} className="space-y-1.5">
                    <Label htmlFor={`ce-p-${k}`}>
                      {k === 'nombre'
                        ? 'Nombre *'
                        : k === 'contacto'
                          ? 'Contacto (opcional)'
                          : 'Descripción (opcional)'}
                    </Label>
                    <Input
                      id={`ce-p-${k}`}
                      required={k === 'nombre'}
                      maxLength={k === 'nombre' ? 200 : 300}
                      value={p[k]}
                      onChange={(e) => setP({ ...p, [k]: e.target.value })}
                    />
                  </FormCampo>
                ))}
                <FormCampo span={6} className="space-y-1.5">
                  <Label htmlFor="ce-p-tipo">Tipo de participante</Label>
                  <Select
                    value={String(p.tipo)}
                    onValueChange={(value) =>
                      setP({ ...p, tipo: Number(value) as TipoParticipante })
                    }
                  >
                    <SelectTrigger id="ce-p-tipo">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {Object.entries(tiposParticipante).map(([value, label]) => (
                        <SelectItem key={value} value={value}>
                          {label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </FormCampo>
                <FormCampo span={6} className="space-y-1.5">
                  <Label htmlFor="ce-p-papel">Papel en la etapa</Label>
                  <select
                    id="ce-p-papel"
                    className="h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3"
                    value={p.papel}
                    onChange={(e) => setP({ ...p, papel: Number(e.target.value) as 1 | 2 | 3 })}
                  >
                    <option value={1}>Captura</option>
                    <option value={2}>Revisa</option>
                    <option value={3}>Autoriza</option>
                  </select>
                </FormCampo>
              </FormGrid>
            </FormCampo>
            <FormCampo span={6} className="space-y-1.5">
              <Label>Proveedor (opcional)</Label>
              <ReferenciaSelector
                tipo="proveedores"
                value={p.proveedorId}
                onChange={(id) => setP({ ...p, proveedorId: id, clienteId: null })}
              />
            </FormCampo>
            <FormCampo span={6} className="space-y-1.5">
              <Label>Cliente (opcional)</Label>
              <ReferenciaSelector
                tipo="clientes"
                value={p.clienteId}
                onChange={(id) => setP({ ...p, clienteId: id, proveedorId: null })}
              />
            </FormCampo>
            <FormCampo span={12}>
              {accion.isError && (
                <p role="alert" className="text-danger-fg">
                  {esApiError(accion.error)
                    ? (accion.error.problem.detail ?? accion.error.problem.title)
                    : 'No se pudo guardar el participante.'}
                </p>
              )}
            </FormCampo>
            <FormCampo span={12}>
              <Button
                type="submit"
                disabled={accion.isPending}
                title={accion.isPending ? 'Guardando participante.' : undefined}
              >
                Guardar participante
              </Button>
            </FormCampo>
          </FormSeccion>
        </form>
      )}
    </section>
  );
}
function Bitacora({ id }: { id: string }) {
  const query = useQuery({
    queryKey: ['comercio-exterior', 'bitacora', id],
    queryFn: async () =>
      (
        await apiRequest<{
          items: {
            id: string;
            timestamp: string;
            actorNombre: string;
            resumen: string;
            cambios: string;
          }[];
        }>(`${CE_BASE}/${id}/bitacora`)
      ).data,
  });
  return (
    <section>
      <h2 className="mb-3 text-md font-semibold">Actividad de los últimos 90 días</h2>
      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la bitácora"
          onRetry={() => {
            void query.refetch();
          }}
        />
      ) : query.isLoading ? (
        <p role="status">Cargando actividad…</p>
      ) : !query.data?.items.length ? (
        <p className="text-ink-muted">Sin actividad en este periodo.</p>
      ) : (
        <ul className="divide-y divide-line-row">
          {query.data.items.map((a) => (
            <li key={a.id} className="py-3">
              <div className="flex justify-between gap-3">
                <span>
                  {a.actorNombre} · {a.resumen}
                </span>
                <DateTimeDisplay value={a.timestamp} />
              </div>
              <details className="mt-1 text-xs text-ink-muted">
                <summary>Ver cambios</summary>
                <pre className="overflow-auto whitespace-pre-wrap">
                  {JSON.stringify(JSON.parse(a.cambios), null, 2)}
                </pre>
              </details>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
