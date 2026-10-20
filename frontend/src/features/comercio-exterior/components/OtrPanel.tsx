import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { FormCampo, FormGrid } from '@/components/erp/forms/FormGrid';
import { estadosOtr, estadosPartidaOtr, estadosSalidaOtr } from '../lib/estados-ui';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { useState } from 'react';
import { TriangleAlert } from 'lucide-react';
import { Link } from '@tanstack/react-router';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Sheet,
  SheetContent,
  SheetBody,
  SheetFooter,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { ErrorState, DateTimeDisplay } from '@/components/erp';
import { UsuarioSelector } from '@/components/erp/selectors/UsuarioSelector';
import { MonedaSelector } from '@/components/erp/selectors/MonedaSelector';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { useOtrEnvios, useOtrAccion, useOtrCandidatos, type OtrEnvio } from '../api/otr';

export function OtrPanel({
  expedienteId,
  abierto = true,
}: {
  expedienteId: string;
  abierto?: boolean;
}) {
  const q = useOtrEnvios(expedienteId);
  if (q.isError)
    return (
      <ErrorState
        title="No se pudieron cargar los envíos OTR"
        onRetry={() => {
          void q.refetch();
        }}
      />
    );
  if (!q.data) return <p role="status">Cargando A+W OTR…</p>;
  return (
    <section className="space-y-4" aria-label="A+W OTR">
      <h2 className="text-md font-semibold">A+W OTR</h2>
      <p
        role="note"
        className="flex items-start gap-2 rounded-md bg-warning-note-bg px-3 py-2.5 text-warning-note-fg"
      >
        <TriangleAlert aria-hidden="true" className="size-4 shrink-0" />
        La conexión con A+W OTR está pendiente de habilitación. Consulta los envíos recibidos y sus
        incidencias.
      </p>
      {q.data.length === 0 && (
        <p className="text-ink-muted">
          Sin envíos OTR vinculados. Vincule uno desde{' '}
          <Link className="text-brand" to="/comercio-exterior/otr">
            Envíos OTR por vincular
          </Link>
          .
        </p>
      )}
      {q.data.map((envio) => (
        <OtrDetalle key={envio.id} envio={envio} abierto={abierto} />
      ))}
    </section>
  );
}
export function OtrDetalle({ envio, abierto = true }: { envio: OtrEnvio; abierto?: boolean }) {
  const permisoGestionar = useHasPermission(P.ComercioExteriorOtrGestionar);
  const gestionar = permisoGestionar && abierto && envio.estado === 2;
  const accion = useOtrAccion();
  return (
    <article className="space-y-4 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <header className="flex flex-wrap items-center gap-3">
        <h3 className="font-mono font-semibold">{envio.idOrigen}</h3>
        <Badge variant={estadosOtr[envio.estado].tono}>{estadosOtr[envio.estado].texto}</Badge>
        <span className="text-xs text-ink-muted">Versión {envio.versionAplicada}</span>
      </header>
      {envio.motivoVinculacion && <p className="text-warning-fg">{envio.motivoVinculacion}</p>}
      <dl className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {Object.entries({
          Cliente: envio.contenido.cliente,
          Entrega: envio.contenido.direccionEntrega,
          Contenedor: envio.contenido.contenedor.numero ?? 'Sin número',
          Ruta: envio.contenido.contenedor.ruta ?? 'Sin ruta',
        }).map(([campo, valor]) => (
          <div key={campo}>
            <dt className="text-xs text-ink-muted">{campo}</dt>
            <dd>{valor}</dd>
          </div>
        ))}
      </dl>
      <div className="overflow-x-auto">
        <Table className="grid min-w-0 text-sm">
          <caption className="pb-2 text-left font-medium">Partidas recibidas</caption>
          <TableHeader className="h-9 bg-surface-subtle text-left text-2xs uppercase text-ink-muted">
            <TableRow className="grid h-9 grid-cols-[160px_80px_100px_80px_minmax(0,1.6fr)] items-center">
              {['Pedido A+W', 'Partida', 'Cantidad', 'Versión', 'Estado y motivo'].map((x) => (
                <TableHead className="px-3" key={x}>
                  {x}
                </TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {envio.lineas.map((l) => (
              <TableRow
                className="grid min-h-12 grid-cols-[160px_80px_100px_80px_minmax(0,1.6fr)] items-center border-b border-line-row"
                key={l.id}
              >
                <TableCell className="px-3 font-mono">{l.numeroPedido}</TableCell>
                <TableCell className="px-3 tabular-nums">{l.posicion}</TableCell>
                <TableCell className="px-3 text-right tabular-nums">
                  {l.cantidad} {l.unidad}
                </TableCell>
                <TableCell className="px-3 tabular-nums">{l.versionOrigen}</TableCell>
                <TableCell className="px-3">
                  <Badge variant={estadosPartidaOtr[l.estado].tono}>
                    {estadosPartidaOtr[l.estado].texto}
                  </Badge>
                  <p className="text-xs text-ink-muted">{l.motivo}</p>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      <section className="space-y-3" aria-label="Conflictos OTR">
        <h4 className="font-medium">Conflictos</h4>
        {envio.conflictos.length === 0 && <p className="text-ink-muted">Sin conflictos.</p>}
        {envio.conflictos.map((c) => (
          <Conflicto key={c.id} envio={envio} conflicto={c} gestionar={gestionar} />
        ))}
      </section>
      <section className="space-y-2" aria-label="Salidas OTR">
        <h4 className="font-medium">Salidas y acuse A+W</h4>
        {envio.salidas.length === 0 && <p className="text-ink-muted">Sin salidas.</p>}
        {envio.salidas.map((s) => (
          <div
            className="flex flex-wrap items-center gap-3 border-b border-line-row py-3"
            key={s.id}
          >
            <span>
              {s.tipo} · versión ERP {s.versionErp} · intento {s.intento}
            </span>
            <Badge variant={s.sinAcuse ? 'warning' : estadosSalidaOtr[s.estado].tono}>
              {s.sinAcuse ? 'Sin acuse' : estadosSalidaOtr[s.estado].texto}
            </Badge>
            <span className="text-xs text-ink-muted">Intentos: {s.intentos}</span>
            {s.acuseAt && (
              <span>
                {s.acuseResultado} · <DateTimeDisplay value={s.acuseAt} />
              </span>
            )}
            {s.acuseMotivo && <p>{s.acuseMotivo}</p>}
            {s.ultimoError && <p className="text-danger-fg">{s.ultimoError}</p>}
            {permisoGestionar && s.estado === 4 && (
              <Button
                variant="outline"
                disabled={accion.isPending}
                title={accion.isPending ? 'Guardando reintento.' : undefined}
                onClick={() => {
                  void accion
                    .mutateAsync({ envio, ruta: `salidas/${s.id}/reintentar` })
                    .catch(() => {});
                }}
              >
                Reintentar salida
              </Button>
            )}
          </div>
        ))}
      </section>
      {accion.isError && (
        <p role="alert" className="text-danger-fg">
          {esApiError(accion.error) ? accion.error.message : 'No se pudo reintentar la salida.'}
        </p>
      )}
    </article>
  );
}
function Conflicto({
  envio,
  conflicto: c,
  gestionar,
}: {
  envio: OtrEnvio;
  conflicto: OtrEnvio['conflictos'][number];
  gestionar: boolean;
}) {
  const [motivo, setMotivo] = useState('');
  const accion = useOtrAccion();
  const linea = envio.lineas.find((l) => `Partida:${l.id}` === c.campo);
  const resolver = (usarOtr: boolean) => {
    void accion
      .mutateAsync({ envio, ruta: `conflictos/${c.id}/resolver`, body: { usarOtr, motivo } })
      .catch(() => {});
  };
  return (
    <div className="space-y-2 rounded-md border border-line p-3">
      <p className="font-medium">
        {linea ? `Pedido ${linea.numeroPedido} · partida ${linea.posicion}` : c.campo} · versión OTR{' '}
        {c.versionOrigen}
      </p>
      <p>
        OTR: <span className="font-mono">{c.valorOtr}</span> · ERP:{' '}
        <span className="font-mono">{c.valorErp}</span>
      </p>
      <p className="text-xs text-ink-muted">
        ERP: {c.actorErp} · <DateTimeDisplay value={c.fechaErp} />. Recibido:{' '}
        <DateTimeDisplay value={c.recibidoAt} />
      </p>
      {c.estado === 'Superado' ? (
        <p className="text-ink-muted">Superado por versión OTR {c.superadoPorVersion}</p>
      ) : c.resueltoPorId ? (
        <p>
          Decisión: {c.usarOtr ? 'Usar OTR' : 'Conservar ERP'} · {c.motivo} · {c.resueltoPorId}
          {c.resueltoAt && (
            <>
              {' '}
              · <DateTimeDisplay value={c.resueltoAt} />
            </>
          )}
        </p>
      ) : gestionar && c.versionOrigen === envio.versionAplicada ? (
        <>
          <FormCampo span={6} className="space-y-1.5">
            <Label htmlFor={`motivo-${c.id}`}>
              Motivo de la decisión (5 a 500 caracteres) <span aria-hidden="true">*</span>
            </Label>
            <Input
              id={`motivo-${c.id}`}
              value={motivo}
              maxLength={500}
              onChange={(e) => setMotivo(e.target.value)}
            />
          </FormCampo>
          <div className="flex gap-2">
            {[true, false].map((usar) => (
              <Button
                key={String(usar)}
                variant={usar ? 'default' : 'outline'}
                disabled={motivo.trim().length < 5 || accion.isPending}
                title={
                  motivo.trim().length < 5
                    ? 'Capture un motivo de al menos 5 caracteres.'
                    : accion.isPending
                      ? 'Guardando decisión.'
                      : undefined
                }
                onClick={() => resolver(usar)}
              >
                {usar ? 'Usar OTR' : 'Conservar ERP'}
              </Button>
            ))}
          </div>
        </>
      ) : (
        <p className="text-ink-muted">
          {c.versionOrigen !== envio.versionAplicada
            ? 'Histórico: existe una versión OTR posterior.'
            : 'Requiere permiso de gestión OTR y un expediente abierto.'}
        </p>
      )}
      {accion.isError && (
        <p role="alert" className="text-danger-fg">
          {esApiError(accion.error) ? accion.error.message : 'No se pudo resolver el conflicto.'}
        </p>
      )}
    </div>
  );
}
export function VincularOtrSheet({ envio, onClose }: { envio: OtrEnvio; onClose: () => void }) {
  const candidatos = useOtrCandidatos(envio.id);
  const accion = useOtrAccion();
  const [expediente, setExpediente] = useState('');
  const user = useAuthStore((s) => s.user);
  const [responsable, setResponsable] = useState(user?.id ?? '');
  const [moneda, setMoneda] = useState('USD');
  const crear = useHasPermission(P.ComercioExteriorExpedientesCrear);
  const consultarUsuarios = useHasPermission(P.IdentidadUsuariosLeer);
  const ejecutar = (ruta: string, body: unknown) => {
    void accion
      .mutateAsync({ envio, ruta, body })
      .then(onClose)
      .catch(() => {});
  };
  return (
    <Sheet
      open
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
    >
      <SheetContent size="form">
        <SheetHeader>
          <SheetTitle>Vincular {envio.idOrigen}</SheetTitle>
          <SheetDescription>
            Seleccione un expediente de exportación abierto del mismo cliente o abra uno desde el
            envío.
          </SheetDescription>
        </SheetHeader>
        <SheetBody className="space-y-5">
          <p>
            {envio.contenido.cliente} · {envio.contenido.direccionEntrega}
          </p>
          <FormGrid>
            {' '}
            <FormCampo span={12} className="space-y-1.5">
              <Label htmlFor="otr-expediente">
                Expediente abierto <span aria-hidden="true">*</span>
              </Label>
              <select
                className="h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3"
                id="otr-expediente"
                value={expediente}
                onChange={(e) => setExpediente(e.target.value)}
              >
                <option value="">Seleccione un expediente</option>
                {candidatos.data?.map((e) => (
                  <option key={e.id} value={e.id}>
                    {e.folio}
                  </option>
                ))}
              </select>
            </FormCampo>
          </FormGrid>
          {candidatos.isError && (
            <p role="alert">
              No se pudieron consultar los expedientes.{' '}
              <Button
                variant="link"
                onClick={() => {
                  void candidatos.refetch();
                }}
              >
                Reintentar consulta
              </Button>
            </p>
          )}

          {crear && (
            <FormSeccion titulo="Abrir expediente nuevo" className="border-t border-line pt-4">
              <FormCampo span={12} className="space-y-3">
                <p className="text-xs text-ink-muted">
                  Responsable del expediente <span aria-hidden="true">*</span>
                </p>
                {consultarUsuarios ? (
                  <UsuarioSelector
                    value={responsable}
                    onChange={(id) => setResponsable(id ?? '')}
                  />
                ) : (
                  <p>
                    Usuario de la sesión. Cambiar responsable requiere permiso de consulta de
                    usuarios.
                  </p>
                )}
                <p className="text-xs text-ink-muted">
                  Moneda <span aria-hidden="true">*</span>
                </p>
                <MonedaSelector value={moneda} onChange={(id) => setMoneda(id ?? '')} />
                <p className="text-xs text-ink-muted">
                  Se propone DDP; se validan responsable, sucursal y moneda en el ERP.
                </p>
                <Button
                  variant="outline"
                  disabled={!responsable || !moneda || accion.isPending}
                  title={
                    !responsable || !moneda
                      ? 'Seleccione responsable y moneda.'
                      : accion.isPending
                        ? 'Abriendo expediente.'
                        : undefined
                  }
                  onClick={() =>
                    ejecutar('abrir-expediente', { responsableUsuarioId: responsable, moneda })
                  }
                >
                  Abrir expediente desde envío
                </Button>
              </FormCampo>
            </FormSeccion>
          )}
          {accion.isError && (
            <p role="alert" className="text-danger-fg">
              {esApiError(accion.error) ? accion.error.message : 'No se pudo vincular el envío.'}
            </p>
          )}
        </SheetBody>
        <SheetFooter>
          <Button variant="ghost" onClick={onClose}>
            Cancelar
          </Button>
          <Button
            disabled={!expediente || accion.isPending}
            title={
              !expediente
                ? 'Seleccione un expediente abierto.'
                : accion.isPending
                  ? 'Guardando vínculo.'
                  : undefined
            }
            onClick={() => ejecutar('vincular', { expedienteId: expediente })}
          >
            Vincular envío
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  );
}
