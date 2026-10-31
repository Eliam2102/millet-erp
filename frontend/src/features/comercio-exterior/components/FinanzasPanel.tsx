import { useState, type FormEvent, type ReactNode } from 'react';
import { useMutation } from '@tanstack/react-query';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import {
  Table,
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
  TableContainer,
} from '@/components/ui/table';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { FormCampo } from '@/components/erp/forms/FormGrid';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { ErrorState } from '@/components/erp';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { apiRequest, esApiError } from '@/lib/api';
import { CE_BASE, useContenedores, useExpedientes, useMutarContenedor } from '../api/hooks';
import {
  useFinanzas,
  useOrdenesServicio,
  type OrdenServicio,
  useMutarFinanzas,
  type Distribucion,
  type Gasto,
  type Finanzas,
  type PartidaCosto,
  type Anticipo,
} from '../api/finanzas';
import type { ExpedienteDetalle } from '../api/types';
import { ReferenciaSelector } from './ReferenciaSelector';
const dinero = (n: number) => n.toLocaleString('es-MX', { style: 'currency', currency: 'MXN' });
const bases: Record<number, string> = {
  1: 'Valor en aduana',
  2: 'Peso',
  3: 'Cantidad',
  4: 'Directo por partida',
};
const errorTexto = (error: unknown) =>
  esApiError(error)
    ? (error.problem.detail ?? error.problem.title)
    : 'No se pudo completar la operación. Revisa los datos e intenta de nuevo.';
function Campo({ label, id, children }: { label: string; id: string; children: ReactNode }) {
  return (
    <FormCampo span={6} className="space-y-1.5">
      <Label htmlFor={id}>{label}</Label>
      {children}
    </FormCampo>
  );
}
const selectClass =
  'h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3 text-sm';
export function FinanzasPanel({
  data,
  tab,
}: {
  data: ExpedienteDetalle;
  tab: 'gastos' | 'anticipos';
}) {
  const query = useFinanzas(data.expediente.id);
  if (query.isError)
    return (
      <ErrorState
        title="No se pudieron cargar las finanzas"
        onRetry={() => {
          void query.refetch();
        }}
      />
    );
  if (!query.data) return <p role="status">Cargando finanzas…</p>;
  return (
    <section className="space-y-4 text-sm">
      <Badge variant={query.data.data.pendientes.length ? 'warning' : 'success'}>
        {query.data.data.estadoFinanciero}
      </Badge>
      {tab === 'gastos' ? (
        <Gastos
          key={data.expediente.id}
          data={data}
          finanzas={query.data.data}
          etag={query.data.etag}
        />
      ) : (
        <Anticipos key={data.expediente.id} data={data} finanzas={query.data.data} />
      )}
    </section>
  );
}
function Gastos({
  data,
  finanzas: f,
  etag,
}: {
  data: ExpedienteDetalle;
  finanzas: Finanzas;
  etag?: string;
}) {
  const id = data.expediente.id;
  const accion = useMutarFinanzas(id);
  const registrar =
    useHasPermission(P.ComercioExteriorGastosRegistrar) && data.expediente.estado === 1;
  const distribuir =
    useHasPermission(P.ComercioExteriorGastosDistribuir) && data.expediente.estado === 1;
  const [editar, setEditar] = useState<Gasto | null | undefined>(undefined);
  const [partida, setPartida] = useState<PartidaCosto | null>(null);
  const [previa, setPrevia] = useState<{ resultado: Distribucion; etag?: string } | null>(null);
  const preview = useMutation({
    mutationFn: async () => ({
      resultado: (
        await apiRequest<Distribucion>(`${CE_BASE}/${id}/gastos/vista-previa`, { ifMatch: etag })
      ).data,
      etag,
    }),
    onSuccess: setPrevia,
  });
  const nombres = new Map(
    data.participantes.filter((p) => p.proveedorId).map((p) => [p.proveedorId, p.nombre]),
  );
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap gap-2">
        {registrar && <Button onClick={() => setEditar(null)}>Registrar gasto</Button>}
        {distribuir && (
          <Button
            variant="outline"
            onClick={() => {
              void preview.mutateAsync().catch(() => {});
            }}
            disabled={preview.isPending || !f.gastos.length}
            title={
              !f.gastos.length
                ? 'Registra un gasto para distribuir.'
                : preview.isPending
                  ? 'Calculando distribución.'
                  : undefined
            }
          >
            Distribuir
          </Button>
        )}
      </div>
      <section aria-label="Órdenes de servicio del expediente" className="space-y-2">
        <h3 className="text-md font-semibold">Órdenes de servicio del expediente</h3>
        {!f.ordenesServicio?.length ? (
          <p className="text-ink-muted">No hay órdenes de servicio ligadas.</p>
        ) : (
          <TableContainer maxHeightClassName="">
            <Table>
              <TableHeader>
                <TableRow>
                  {[
                    'Orden',
                    'Moneda',
                    'Total OC',
                    'Asignado al expediente',
                    'Facturado',
                    'Diferencia',
                  ].map((h) => (
                    <TableHead key={h}>{h}</TableHead>
                  ))}
                </TableRow>
              </TableHeader>
              <TableBody>
                {f.ordenesServicio.map((o) => (
                  <TableRow key={o.id}>
                    <TableCell className="font-mono">{o.folio}</TableCell>
                    <TableCell>{o.moneda}</TableCell>
                    {[o.total, o.asignado, o.facturado, o.diferencia].map((n, i) => (
                      <TableCell key={i} className="text-right tabular-nums">
                        {dinero(n)}
                      </TableCell>
                    ))}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}
        <p className="text-xs text-ink-muted">
          La diferencia compara lo facturado con el importe asignado. Un valor positivo indica
          excedente.
        </p>
      </section>
      {!f.conceptos.length && registrar && (
        <Button
          variant="outline"
          onClick={() => {
            void accion.mutateAsync({ ruta: 'gastos/conceptos/inicializar' }).catch(() => {});
          }}
        >
          Preparar catálogo de conceptos
        </Button>
      )}
      {(accion.isError || preview.isError) && (
        <p role="alert" className="text-danger-fg">
          {errorTexto(accion.error ?? preview.error)}
        </p>
      )}
      {editar !== undefined && (
        <GastoForm
          key={editar?.id ?? 'nuevo'}
          data={data}
          f={f}
          gasto={editar}
          onClose={() => setEditar(undefined)}
        />
      )}
      <TableContainer maxHeightClassName="">
        <Table>
          <TableHeader>
            <TableRow>
              {[
                'Concepto',
                'Proveedor',
                'Factura',
                'Moneda',
                'Facturado',
                'Cotizado',
                'Diferencia',
                'Acciones',
              ].map((h) => (
                <TableHead key={h}>{h}</TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {f.gastos.map((g) => (
              <TableRow key={g.id}>
                <TableCell>{g.concepto}</TableCell>
                <TableCell>
                  {f.nombres[g.proveedorId] ?? nombres.get(g.proveedorId) ?? 'Proveedor vinculado'}
                </TableCell>
                <TableCell className="font-mono">
                  {g.factura}
                  {g.facturaProveedorId && (
                    <span className="block text-xs text-ink-muted">
                      Vinculada a Cuentas por Pagar
                    </span>
                  )}
                </TableCell>
                <TableCell>{g.moneda}</TableCell>
                <TableCell className="text-right tabular-nums">{dinero(g.importe)}</TableCell>
                <TableCell className="text-right tabular-nums">
                  {g.cotizado === null ? 'Sin cotización' : dinero(g.cotizado)}
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  {g.diferencia === null ? 'Sin cotización' : dinero(g.diferencia)}
                </TableCell>
                <TableCell>
                  {registrar && (
                    <Button variant="ghost" size="sm" onClick={() => setEditar(g)}>
                      Editar
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      {!f.gastos.length && <p className="text-ink-muted">Sin gastos registrados.</p>}
      {data.expediente.tipo === 1 && <ContenedorImportacion id={id} />}
      <h3 className="text-md font-semibold">Bases de las partidas</h3>
      <p className="text-xs text-ink-muted">
        Captura el valor en MXN al tipo de cambio del pedimento y el peso de cada partida.
      </p>
      {!f.partidas.length && (
        <p className="text-ink-muted">
          {data.expediente.tipo === 1
            ? 'No hay recepciones de la orden de compra disponibles.'
            : 'Agrega partidas al contenedor para repartir el costo logístico.'}
        </p>
      )}
      {f.partidas.map((p) => (
        <div
          key={p.partidaId}
          className="flex items-center justify-between gap-3 border-b border-line-row py-2"
        >
          <span>
            {p.descripcion} ·{' '}
            <span className="tabular-nums">
              {dinero(p.valorAduanaMxn)} · {p.pesoKg} kg
            </span>
          </span>
          {registrar && (
            <Button variant="ghost" onClick={() => setPartida(p)}>
              Capturar bases
            </Button>
          )}
        </div>
      ))}
      {partida && (
        <BasesForm
          key={partida.partidaId}
          id={id}
          partida={partida}
          pedimento={f.bases.find((b) => b.id === partida.partidaId)?.pedimento ?? ''}
          onClose={() => setPartida(null)}
        />
      )}
      {registrar && (
        <details>
          <summary className="cursor-pointer text-brand">Configurar bases por concepto</summary>
          <p className="my-2 text-xs text-ink-muted">
            Los valores iniciales son una propuesta VILO pendiente de validación por Laura Cerón.
            Los cambios aplican a la empresa y requieren volver a distribuir los gastos afectados.
          </p>
          {f.conceptos.map((c) => (
            <form
              key={c.id}
              className="flex flex-wrap items-end gap-3 py-2"
              onSubmit={(ev) => {
                ev.preventDefault();
                const d = new FormData(ev.currentTarget);
                void accion
                  .mutateAsync({
                    ruta: `gastos/conceptos/${c.id}`,
                    method: 'PUT',
                    body: {
                      importacion: Number(d.get('importacion')),
                      exportacion: Number(d.get('exportacion')),
                      requiereOrdenServicio: d.get('requiereOrdenServicio') === 'on',
                    },
                  })
                  .catch(() => {});
              }}
            >
              <span>{c.nombre}</span>
              <label className="flex items-center gap-2 text-xs">
                <input
                  type="checkbox"
                  name="requiereOrdenServicio"
                  defaultChecked={c.requiereOrdenServicio}
                />
                Requiere orden de servicio
              </label>
              {(['importacion', 'exportacion'] as const).map((tipo) => (
                <label key={tipo} className="space-y-1 text-xs">
                  {tipo === 'importacion' ? 'Importación' : 'Exportación'}
                  <select
                    name={tipo}
                    className={selectClass}
                    defaultValue={tipo === 'importacion' ? c.baseImportacion : c.baseExportacion}
                  >
                    {Object.entries(bases)
                      .filter(([k]) => tipo === 'importacion' || Number(k) <= 2)
                      .map(([k, v]) => (
                        <option key={k} value={k}>
                          {v}
                        </option>
                      ))}
                  </select>
                </label>
              ))}
              <Button
                variant="outline"
                size="sm"
                disabled={accion.isPending}
                title={accion.isPending ? 'Guardando configuración.' : undefined}
              >
                Guardar base
              </Button>
            </form>
          ))}
        </details>
      )}
      {previa && (
        <section className="space-y-3 rounded-lg bg-surface-subtle p-4">
          <h3 className="text-md font-semibold">Vista previa de distribución</h3>
          <DistribucionTabla d={previa.resultado} nombres={f.nombres} />
          <p className="text-xs text-ink-muted">
            El ajuste definitivo se calcula con la existencia disponible cuando Almacén lo aplica.
          </p>
          <Button
            disabled={accion.isPending || previa.etag !== etag}
            title={
              previa.etag !== etag
                ? 'El expediente cambió. Vuelve a calcular la vista previa.'
                : accion.isPending
                  ? 'Guardando distribución.'
                  : undefined
            }
            onClick={() => {
              void accion
                .mutateAsync({ ruta: 'gastos/distribuir', etag: previa.etag })
                .then(() => setPrevia(null))
                .catch(() => {});
            }}
          >
            Confirmar distribución
          </Button>
          <Button variant="ghost" onClick={() => setPrevia(null)}>
            Descartar
          </Button>
        </section>
      )}
      {data.expediente.tipo === 2 && f.distribuciones.length > 0 && (
        <section>
          <h3 className="text-md font-semibold">Costo logístico por cliente</h3>
          <TableContainer maxHeightClassName="">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Cliente</TableHead>
                  <TableHead className="text-right">Costo MXN</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {Object.entries(
                  [...f.distribuciones]
                    .sort((a, b) => b.numeroVersion - a.numeroVersion)[0]
                    .partidas.reduce<Record<string, number>>((totales, p) => {
                      if (p.clienteId)
                        totales[p.clienteId] = (totales[p.clienteId] ?? 0) + p.importeMxn;
                      return totales;
                    }, {}),
                ).map(([cliente, total]) => (
                  <TableRow key={cliente}>
                    <TableCell>{f.nombres[cliente] ?? 'Cliente del contenedor'}</TableCell>
                    <TableCell className="text-right tabular-nums">{dinero(total)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        </section>
      )}
      <h3 className="text-md font-semibold">Historial de versiones</h3>
      {[...f.distribuciones]
        .sort((a, b) => b.numeroVersion - a.numeroVersion)
        .map((d) => (
          <details key={d.numeroVersion}>
            <summary className="cursor-pointer">
              Versión {d.numeroVersion} · {new Date(d.fecha).toLocaleString('es-MX')}
            </summary>
            <DistribucionTabla d={d} nombres={f.nombres} />
          </details>
        ))}
    </div>
  );
}
export function DistribucionTabla({
  d,
  nombres = {},
}: {
  d: Distribucion;
  nombres?: Record<string, string>;
}) {
  return (
    <div className="space-y-3 overflow-x-auto">
      {d.partidas.some((p) => p.tieneSalidas) && (
        <AvisoEnLinea tono="advertencia">
          Parte de la mercancía ya salió: el gasto de esas partidas va completo a gastos de
          importación y no cambia su costo
        </AvisoEnLinea>
      )}
      <Table>
        <TableHeader>
          <TableRow>
            {[
              'Partida',
              'Base usada',
              'Total MXN',
              'Ajuste MXN',
              'Inventario',
              'A gastos de importación',
              'Costo unitario antes',
              'Costo unitario después',
            ].map((h) => (
              <TableHead key={h}>{h}</TableHead>
            ))}
          </TableRow>
        </TableHeader>
        <TableBody>
          {d.partidas.map((p) => (
            <TableRow key={p.partidaId}>
              <TableCell>
                {p.descripcion}
                {p.clienteId && (
                  <span className="block text-xs text-ink-muted">
                    {nombres[p.clienteId] ?? 'Cliente del contenedor'}
                  </span>
                )}
              </TableCell>
              <TableCell>
                {d.renglones
                  .filter((r) => r.partidaId === p.partidaId)
                  .map((r, i) => (
                    <div key={i}>
                      {bases[r.base]}: {r.baseUsada.toLocaleString('es-MX')}
                    </div>
                  ))}
              </TableCell>
              {[
                p.importeMxn,
                p.diferenciaMxn,
                p.inventarioMxn,
                p.gastoImportacionMxn,
                p.costoUnitarioAntes,
                p.costoUnitarioDespues,
              ].map((v, i) => (
                <TableCell key={i} className="text-right tabular-nums">
                  {dinero(v)}
                </TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
function GastoForm({
  data,
  f,
  gasto,
  onClose,
}: {
  data: ExpedienteDetalle;
  f: Finanzas;
  gasto: Gasto | null;
  onClose: () => void;
}) {
  const [proveedor, setProveedor] = useState<string | null>(gasto?.proveedorId ?? null);
  const [concepto, setConcepto] = useState(gasto?.conceptoId ?? f.conceptos[0]?.id ?? '');
  const [busquedaOrden, setBusquedaOrden] = useState('');
  const [orden, setOrden] = useState<OrdenServicio | null>(
    gasto?.ordenServicioId
      ? {
          id: gasto.ordenServicioId,
          folio: gasto.ordenServicioFolio!,
          total: gasto.ordenServicioTotal!,
          moneda: gasto.ordenServicioMoneda!,
          proveedor: '',
          tieneCotizacion: false,
          cotizacionAdjuntoId: null,
          contenedor: null,
        }
      : null,
  );
  const [moneda, setMoneda] = useState(gasto?.moneda ?? 'MXN');
  const [contenedor, setContenedor] = useState(gasto?.contenedorId ?? '');
  const [fuenteTc, setFuenteTc] = useState(gasto?.fuenteTipoCambio ?? 'Banxico');
  const ordenes = useOrdenesServicio(data.expediente.id, proveedor, busquedaOrden);
  const requiereOrden = f.conceptos.find((c) => c.id === concepto)?.requiereOrdenServicio;
  const accion = useMutarFinanzas(data.expediente.id);
  const contenedores = useContenedores(data.expediente.id);
  const directo =
    data.expediente.tipo === 1 && f.conceptos.find((c) => c.id === concepto)?.baseImportacion === 4;
  function submit(ev: FormEvent<HTMLFormElement>) {
    ev.preventDefault();
    const d = new FormData(ev.currentTarget);
    const body = {
      ordenServicioId: orden?.id ?? null,
      importeAsignado: orden && d.get('asignado') ? Number(d.get('asignado')) : null,
      conceptoId: concepto,
      proveedorId: proveedor,
      factura: d.get('factura'),
      moneda: d.get('moneda'),
      importe: Number(d.get('importe')),
      fechaFactura: d.get('fechaFactura'),
      pedimento: d.get('pedimento') || null,
      cotizado: d.get('cotizado') ? Number(d.get('cotizado')) : null,
      contenedorId: d.get('contenedorId') || null,
      ...(moneda === 'USD' && fuenteTc !== 'Banxico'
        ? {
            fuenteTipoCambio: fuenteTc,
            tipoCambioUsado: d.get('tipoCambioUsado') ? Number(d.get('tipoCambioUsado')) : null,
            notaTipoCambio: d.get('notaTipoCambio'),
          }
        : {}),
      importesDirectos: directo
        ? Object.fromEntries(f.partidas.map((p) => [p.partidaId, Number(d.get(p.partidaId))]))
        : null,
    };
    void accion
      .mutateAsync({
        ruta: gasto ? `gastos/${gasto.id}` : 'gastos',
        method: gasto ? 'PUT' : 'POST',
        body,
      })
      .then(onClose)
      .catch(() => {});
  }
  return (
    <form onSubmit={submit} className="rounded-lg border border-dashed border-line-control p-4">
      <FormSeccion titulo={gasto ? 'Editar gasto' : 'Registrar gasto'}>
        <Campo label="Concepto *" id="fg-concepto">
          <select
            id="fg-concepto"
            className={selectClass}
            required
            value={concepto}
            onChange={(ev) => setConcepto(ev.target.value)}
          >
            {f.conceptos.map((c) => (
              <option key={c.id} value={c.id}>
                {c.nombre}
              </option>
            ))}
          </select>
        </Campo>
        <FormCampo span={6}>
          <Label>Proveedor *</Label>
          <ReferenciaSelector
            tipo="proveedores"
            value={proveedor}
            onChange={(value) => {
              setProveedor(value);
              setOrden(null);
            }}
          />
        </FormCampo>
        <Campo label="Buscar orden por folio o descripción" id="fg-buscar-orden">
          <Input
            id="fg-buscar-orden"
            value={busquedaOrden}
            onChange={(ev) => setBusquedaOrden(ev.target.value)}
          />
        </Campo>
        <Campo label={`Orden de servicio${requiereOrden ? ' *' : ' (opcional)'}`} id="fg-orden">
          <select
            id="fg-orden"
            className={selectClass}
            value={orden?.id ?? ''}
            required={requiereOrden}
            onChange={(ev) => {
              const seleccion = ordenes.data?.data.find((o) => o.id === ev.target.value) ?? null;
              setOrden(seleccion);
              if (seleccion) {
                setMoneda(seleccion.moneda);
                const sugerido = contenedores.data?.data.contenedores.find(
                  (c) => c.estado === 1 && c.numero === seleccion.contenedor,
                );
                if (sugerido) setContenedor(sugerido.id);
              }
            }}
          >
            <option value="">Selecciona una orden del proveedor</option>
            {orden && !ordenes.data?.data.some((o) => o.id === orden.id) && (
              <option value={orden.id}>{orden.folio}</option>
            )}
            {ordenes.data?.data.map((o) => (
              <option key={o.id} value={o.id}>
                {o.folio} · {o.moneda} {dinero(o.total)} ·{' '}
                {o.tieneCotizacion ? 'Con cotización' : 'Sin cotización'}
              </option>
            ))}
          </select>
          {ordenes.isError && (
            <p role="alert" className="text-danger-fg">
              No se pudieron cargar las órdenes de servicio.
            </p>
          )}
          {orden?.contenedor && (
            <p className="text-xs text-ink-muted">Contenedor sugerido: {orden.contenedor}</p>
          )}
        </Campo>
        {orden && (
          <Campo label={`Asignado a este expediente (${orden.moneda})`} id="fg-asignado">
            <Input
              key={orden.id}
              id="fg-asignado"
              name="asignado"
              type="number"
              min="0.01"
              max={orden.total}
              step="0.01"
              defaultValue={
                gasto?.ordenServicioId === orden.id ? (gasto.ordenServicioAsignado ?? '') : ''
              }
            />
            <p className="text-xs text-ink-muted">
              Para repartir entre expedientes, indica el importe. Se cuenta una vez por expediente,
              aunque tenga varias facturas.
            </p>
          </Campo>
        )}
        <Campo label="UUID o folio de factura *" id="fg-factura">
          <Input
            id="fg-factura"
            name="factura"
            required
            maxLength={100}
            defaultValue={gasto?.factura}
          />
        </Campo>
        <Campo label="Moneda *" id="fg-moneda">
          <select
            id="fg-moneda"
            name="moneda"
            className={selectClass}
            value={moneda}
            onChange={(ev) => setMoneda(ev.target.value)}
          >
            <option>MXN</option>
            <option>USD</option>
          </select>
        </Campo>
        <Campo label="Importe facturado *" id="fg-importe">
          <Input
            id="fg-importe"
            name="importe"
            type="number"
            required
            min="0.01"
            step="0.01"
            defaultValue={gasto?.importe}
          />
        </Campo>
        <Campo
          label={
            orden ? `Cotizado de la orden (${orden.moneda})` : 'Importe cotizado (misma moneda)'
          }
          id="fg-cotizado"
        >
          <Input
            id="fg-cotizado"
            name="cotizado"
            type="number"
            min="0"
            step="0.01"
            key={orden?.id ?? 'manual'}
            readOnly={Boolean(orden)}
            defaultValue={orden?.total ?? gasto?.cotizado ?? ''}
          />
        </Campo>
        <Campo label="Fecha de factura *" id="fg-fecha">
          <Input
            id="fg-fecha"
            name="fechaFactura"
            type="date"
            required
            defaultValue={gasto?.fechaFactura}
          />
        </Campo>
        <Campo label="Pedimento" id="fg-pedimento">
          <Input
            id="fg-pedimento"
            name="pedimento"
            maxLength={100}
            defaultValue={gasto?.pedimento ?? ''}
          />
        </Campo>
        <Campo label="Contenedor (opcional)" id="fg-contenedor">
          <select
            id="fg-contenedor"
            name="contenedorId"
            className={selectClass}
            value={contenedor}
            onChange={(ev) => setContenedor(ev.target.value)}
          >
            <option value="">Todo el expediente</option>
            {contenedores.data?.data.contenedores
              .filter((c) => c.estado === 1)
              .map((c) => (
                <option key={c.id} value={c.id}>
                  {c.numero ?? 'Sin número de contenedor'}
                </option>
              ))}
          </select>
        </Campo>
        {directo &&
          f.partidas.map((p) => (
            <Campo
              key={p.partidaId}
              label={`${p.descripcion} · importe directo MXN`}
              id={`fg-${p.partidaId}`}
            >
              <Input
                id={`fg-${p.partidaId}`}
                name={p.partidaId}
                type="number"
                min="0"
                step="0.01"
                required
                defaultValue={gasto?.importesDirectos[p.partidaId] ?? 0}
              />
            </Campo>
          ))}
        {moneda === 'USD' && (
          <>
            <Campo label="Fuente del tipo de cambio" id="fg-fuente-tc">
              <select
                id="fg-fuente-tc"
                className={selectClass}
                value={fuenteTc}
                onChange={(ev) => setFuenteTc(ev.target.value)}
              >
                <option value="Banxico">Banxico (sugerido)</option>
                <option value="Negociado">Negociado</option>
                <option value="CFDI">CFDI</option>
                <option value="Pedimento">Pedimento</option>
              </select>
            </Campo>
            {fuenteTc !== 'Banxico' && (
              <>
                <Campo label={`Tipo de cambio usado${fuenteTc === 'CFDI' ? '' : ' *'}`} id="fg-tc">
                  <Input
                    id="fg-tc"
                    name="tipoCambioUsado"
                    type="number"
                    min="0.0001"
                    step="0.0001"
                    required={fuenteTc !== 'CFDI'}
                    defaultValue={gasto?.fuenteTipoCambio === fuenteTc ? gasto.tipoCambio : ''}
                  />
                  {fuenteTc === 'CFDI' && (
                    <p className="text-xs text-ink-muted">
                      Si la factura está en Cuentas por Pagar se toma de ella; si no, captúralo.
                    </p>
                  )}
                </Campo>
                <Campo label="Nota del cambio (mín. 10 caracteres) *" id="fg-nota-tc">
                  <Input
                    id="fg-nota-tc"
                    name="notaTipoCambio"
                    required
                    minLength={10}
                    maxLength={500}
                    defaultValue={gasto?.notaTipoCambio ?? ''}
                  />
                </Campo>
              </>
            )}
          </>
        )}
        <FormCampo span={12}>
          <p className="text-xs text-ink-muted">
            Para USD se sugiere Banxico de la fecha de factura y puede cambiarse. Si la factura
            existe en Cuentas por Pagar, se vincula automáticamente.
          </p>
          {accion.isError && (
            <p role="alert" className="text-danger-fg">
              {errorTexto(accion.error)}
            </p>
          )}
          <div className="mt-3 flex gap-2">
            <Button
              disabled={!proveedor || !concepto || (requiereOrden && !orden) || accion.isPending}
              title={
                !proveedor || !concepto || (requiereOrden && !orden)
                  ? 'Selecciona proveedor, concepto y la orden requerida.'
                  : accion.isPending
                    ? 'Guardando gasto.'
                    : undefined
              }
            >
              Guardar gasto
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
function BasesForm({
  id,
  partida: p,
  pedimento,
  onClose,
}: {
  id: string;
  partida: PartidaCosto;
  pedimento: string;
  onClose: () => void;
}) {
  const accion = useMutarFinanzas(id);
  const contenedores = useContenedores(id);
  return (
    <form
      className="rounded-lg border border-dashed border-line-control p-4"
      onSubmit={(ev) => {
        ev.preventDefault();
        const d = new FormData(ev.currentTarget);
        void accion
          .mutateAsync({
            ruta: 'gastos/partidas',
            method: 'PUT',
            body: {
              ...p,
              contenedorId: d.get('contenedorId') || null,
              valorAduanaMxn: Number(d.get('valor')),
              pesoKg: Number(d.get('peso')),
              pedimento: d.get('pedimento'),
            },
          })
          .then(onClose)
          .catch(() => {});
      }}
    >
      <FormSeccion titulo={p.descripcion}>
        <Campo label="Contenedor de la partida" id="base-contenedor">
          <select
            name="contenedorId"
            id="base-contenedor"
            className={selectClass}
            defaultValue={p.contenedorId ?? ''}
          >
            <option value="">Sin contenedor</option>
            {contenedores.data?.data.contenedores
              .filter((c) => c.estado === 1)
              .map((c) => (
                <option key={c.id} value={c.id}>
                  {c.numero ?? 'Sin número'}
                </option>
              ))}
          </select>
        </Campo>
        <Campo label="Valor en aduana MXN *" id="base-valor">
          <Input
            id="base-valor"
            name="valor"
            type="number"
            min="0"
            step="0.01"
            required
            defaultValue={p.valorAduanaMxn}
          />
        </Campo>
        <Campo label="Peso kg *" id="base-peso">
          <Input
            id="base-peso"
            name="peso"
            type="number"
            min="0"
            step="0.000001"
            required
            defaultValue={p.pesoKg}
          />
        </Campo>
        <Campo label="Pedimento *" id="base-pedimento">
          <Input
            id="base-pedimento"
            name="pedimento"
            required
            maxLength={100}
            defaultValue={pedimento}
          />
        </Campo>
        <FormCampo span={12}>
          {accion.isError && (
            <p role="alert" className="text-danger-fg">
              {errorTexto(accion.error)}
            </p>
          )}
          <Button
            disabled={accion.isPending}
            title={accion.isPending ? 'Guardando bases.' : undefined}
          >
            Guardar bases
          </Button>
          <Button type="button" variant="ghost" onClick={onClose}>
            Descartar
          </Button>
        </FormCampo>
      </FormSeccion>
    </form>
  );
}
function Anticipos({ data, finanzas: f }: { data: ExpedienteDetalle; finanzas: Finanzas }) {
  const gestionar =
    useHasPermission(P.ComercioExteriorAnticiposGestionar) && data.expediente.estado === 1;
  const [nuevo, setNuevo] = useState(false);
  const [seleccion, setSeleccion] = useState<Anticipo | null>(null);
  const nombres = new Map(
    data.participantes.filter((p) => p.proveedorId).map((p) => [p.proveedorId, p.nombre]),
  );
  return (
    <div className="space-y-4">
      {gestionar && <Button onClick={() => setNuevo(true)}>Registrar anticipo</Button>}
      {nuevo && <NuevoAnticipo id={data.expediente.id} onClose={() => setNuevo(false)} />}
      <TableContainer maxHeightClassName="">
        <Table>
          <TableHeader>
            <TableRow>
              {[
                'Agente',
                'Pedimento',
                'Anticipado',
                'Aplicado',
                'Diferencia',
                'Saldo abierto',
                'Estado',
                'Acciones',
              ].map((h) => (
                <TableHead key={h}>{h}</TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {f.anticipos.map((a) => (
              <TableRow key={a.id}>
                <TableCell>
                  {f.nombres[a.agenteId] ?? nombres.get(a.agenteId) ?? 'Agente vinculado'}
                </TableCell>
                <TableCell className="font-mono">{a.pedimento}</TableCell>
                {[a.anticipado, a.aplicado, a.diferencia, a.saldo].map((n, i) => (
                  <TableCell key={i} className="text-right tabular-nums">
                    {n === null ? 'Sin comprobar' : dinero(n)}
                  </TableCell>
                ))}
                <TableCell>
                  <Badge variant={a.saldo === 0 ? 'success' : 'warning'}>{a.estado}</Badge>
                </TableCell>
                <TableCell>
                  {gestionar && (a.aplicado === null || a.saldo !== 0) && (
                    <Button variant="ghost" onClick={() => setSeleccion(a)}>
                      {a.aplicado === null ? 'Comprobar' : 'Resolver saldo'}
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      {seleccion && (
        <ResolverAnticipo
          key={seleccion.id}
          data={data}
          a={seleccion}
          gastos={f.gastos}
          onClose={() => setSeleccion(null)}
        />
      )}
      {f.anticipos.map(
        (a) =>
          a.resoluciones.length > 0 && (
            <details key={a.id}>
              <summary>Resoluciones del pedimento {a.pedimento}</summary>
              {a.resoluciones.map((r, i) => (
                <p key={i} className="py-2 tabular-nums">
                  {r.tipo === 'saldo-al-agente'
                    ? 'Saldo trasladado a la cuenta del agente'
                    : r.tipo === 'compensar'
                      ? 'Compensación a otro anticipo'
                      : r.tipo === 'devolucion'
                        ? 'Devolución recibida'
                        : 'Pago complementario'}{' '}
                  · {dinero(Math.abs(r.importe))} · {r.referencia}
                </p>
              ))}
            </details>
          ),
      )}
    </div>
  );
}
function Impuestos() {
  return (
    <>
      {['iva', 'igi', 'prv'].map((k) => (
        <Campo key={k} label={`${k.toUpperCase()} MXN *`} id={`ant-${k}`}>
          <Input
            id={`ant-${k}`}
            name={k}
            type="number"
            required
            min="0"
            step="0.01"
            defaultValue={0}
          />
        </Campo>
      ))}
    </>
  );
}
function NuevoAnticipo({ id, onClose }: { id: string; onClose: () => void }) {
  const [agente, setAgente] = useState<string | null>(null);
  const accion = useMutarFinanzas(id);
  return (
    <form
      className="rounded-lg border border-dashed border-line-control p-4"
      onSubmit={(ev) => {
        ev.preventDefault();
        const d = new FormData(ev.currentTarget);
        void accion
          .mutateAsync({
            ruta: 'anticipos',
            body: {
              agenteId: agente,
              pedimento: d.get('pedimento'),
              impuestos: {
                iva: Number(d.get('iva')),
                igi: Number(d.get('igi')),
                prv: Number(d.get('prv')),
              },
              fecha: d.get('fecha'),
              referenciaPago: d.get('referencia') || null,
              cuentaPeca: d.get('cuentaPeca') === 'on',
            },
          })
          .then(onClose)
          .catch(() => {});
      }}
    >
      <FormSeccion titulo="Anticipo al agente aduanal">
        <FormCampo span={6}>
          <Label>Agente aduanal *</Label>
          <ReferenciaSelector tipo="proveedores" value={agente} onChange={setAgente} />
        </FormCampo>
        <Campo label="Pedimento *" id="ant-ped">
          <Input id="ant-ped" name="pedimento" required maxLength={100} />
        </Campo>
        <FormCampo span={6}>
          <label className="flex items-center gap-2">
            <Checkbox name="cuentaPeca" /> Cuenta PECA (sólo impuestos)
          </label>
        </FormCampo>
        <Impuestos />
        <Campo label="Fecha *" id="ant-fecha">
          <Input id="ant-fecha" name="fecha" type="date" required />
        </Campo>
        <Campo label="Referencia del pago de Tesorería" id="ant-ref">
          <Input id="ant-ref" name="referencia" maxLength={200} />
        </Campo>
        <FormCampo span={12}>
          {accion.isError && (
            <p role="alert" className="text-danger-fg">
              {errorTexto(accion.error)}
            </p>
          )}
          <Button
            disabled={!agente || accion.isPending}
            title={
              !agente
                ? 'Selecciona el agente.'
                : accion.isPending
                  ? 'Guardando anticipo.'
                  : undefined
            }
          >
            Guardar anticipo
          </Button>
          <Button type="button" variant="ghost" onClick={onClose}>
            Descartar
          </Button>
        </FormCampo>
      </FormSeccion>
    </form>
  );
}
export function ResolverAnticipo({
  data,
  a,
  gastos = [],
  onClose,
}: {
  data: ExpedienteDetalle;
  a: Anticipo;
  gastos?: Gasto[];
  onClose: () => void;
}) {
  const accion = useMutarFinanzas(data.expediente.id);
  const [tipo, setTipo] = useState(a.saldo && a.saldo < 0 ? 'pago-complementario' : 'devolucion');
  const [destino, setDestino] = useState('');
  const [busquedaDestino, setBusquedaDestino] = useState('');
  const expedientes = useExpedientes({ q: busquedaDestino });
  const destinoQuery = useFinanzas(destino);
  const evidencia = data.documentos.filter((d) => d.estado === 1 && d.adjuntoId);
  function submit(ev: FormEvent<HTMLFormElement>) {
    ev.preventDefault();
    const d = new FormData(ev.currentTarget);
    const body =
      a.aplicado === null
        ? {
            impuestos: {
              iva: Number(d.get('iva')),
              igi: Number(d.get('igi')),
              prv: Number(d.get('prv')),
            },
            gastoIds: a.cuentaPeca ? [] : d.getAll('gastoIds'),
            pedimentoPagadoId: d.get('pedimentoPagadoId'),
            comprobantePagoId: d.get('evidencia'),
            fecha: d.get('fecha'),
          }
        : {
            tipo,
            importe: Number(d.get('importe')),
            fecha: d.get('fecha'),
            referencia: d.get('referencia'),
            evidenciaId: d.get('evidencia'),
            anticipoDestinoId: tipo === 'compensar' ? d.get('anticipoDestino') : null,
            versionDestino: tipo === 'compensar' ? destinoQuery.data?.data.version : null,
          };
    void accion
      .mutateAsync({
        ruta: `anticipos/${a.id}/${a.aplicado === null ? 'comprobar' : 'resolver'}`,
        body,
      })
      .then(onClose)
      .catch(() => {});
  }
  const selectorEvidencia = (name: string, label: string) => (
    <Campo label={label} id={`res-${name}`}>
      <select required name={name} id={`res-${name}`} className={selectClass}>
        <option value="">Selecciona un documento vigente</option>
        {evidencia.map((d) => (
          <option key={d.id} value={d.adjuntoId!}>
            {data.requisitos.find((r) => r.id === d.tipoDocumentoId)?.nombre ?? 'Documento'} ·
            versión {d.numeroVersion}
          </option>
        ))}
      </select>
    </Campo>
  );
  return (
    <form onSubmit={submit} className="rounded-lg border border-dashed border-line-control p-4">
      <FormSeccion titulo={a.aplicado === null ? 'Comprobar anticipo' : 'Resolver saldo'}>
        {a.aplicado === null ? (
          <>
            <Impuestos />
            {selectorEvidencia('pedimentoPagadoId', 'Pedimento pagado *')}
            {!a.cuentaPeca && (
              <FormCampo span={12}>
                <fieldset className="space-y-2">
                  <legend>Gastos del agente a comprobar</legend>
                  {gastos
                    .filter((g) => g.proveedorId === a.agenteId && !g.anticipoAgenteId)
                    .map((g) => (
                      <label key={g.id} className="flex items-center gap-2">
                        <Checkbox name="gastoIds" value={g.id} />
                        {g.concepto} · {g.factura} · {dinero(g.importeMxn)}
                      </label>
                    ))}
                </fieldset>
              </FormCampo>
            )}
          </>
        ) : (
          <>
            <Campo label="Resolución *" id="res-tipo">
              <select
                id="res-tipo"
                className={selectClass}
                value={tipo}
                onChange={(e) => setTipo(e.target.value)}
              >
                {(a.saldo ?? 0) > 0 ? (
                  <>
                    <option value="devolucion">Devolución recibida</option>
                    <option value="saldo-al-agente">Dejar saldo en la cuenta del agente</option>
                    <option value="compensar">Compensar en otro anticipo</option>
                  </>
                ) : (
                  <option value="pago-complementario">Pago complementario al agente</option>
                )}
              </select>
            </Campo>
            <Campo label="Importe MXN *" id="res-importe">
              <Input
                id="res-importe"
                name="importe"
                type="number"
                min="0.01"
                max={Math.abs(a.saldo ?? 0)}
                step="0.01"
                required
                defaultValue={Math.abs(a.saldo ?? 0)}
              />
            </Campo>
            <Campo label="Referencia *" id="res-ref">
              <Input id="res-ref" name="referencia" required maxLength={200} />
            </Campo>
            {tipo === 'compensar' && (
              <>
                <Campo label="Buscar expediente destino" id="res-buscar">
                  <Input
                    id="res-buscar"
                    value={busquedaDestino}
                    onChange={(e) => setBusquedaDestino(e.target.value)}
                    placeholder="Folio o referencia"
                  />
                </Campo>
                <Campo label="Expediente destino *" id="res-destino">
                  <select
                    id="res-destino"
                    className={selectClass}
                    value={destino}
                    required
                    onChange={(e) => setDestino(e.target.value)}
                  >
                    <option value="">Selecciona un expediente</option>
                    {expedientes.data?.items.map((e) => (
                      <option key={e.id} value={e.id}>
                        {e.folio} · {e.referencia}
                      </option>
                    ))}
                  </select>
                </Campo>
                <Campo label="Anticipo destino *" id="res-anticipo">
                  <select id="res-anticipo" name="anticipoDestino" className={selectClass} required>
                    <option value="">Selecciona un anticipo del mismo agente</option>
                    {destinoQuery.data?.data.anticipos
                      .filter(
                        (b) => b.id !== a.id && b.agenteId === a.agenteId && b.aplicado === null,
                      )
                      .map((b) => (
                        <option key={b.id} value={b.id}>
                          {b.pedimento} · {dinero(b.anticipado - b.compensacionRecibida)}
                        </option>
                      ))}
                  </select>
                </Campo>
              </>
            )}
          </>
        )}
        <Campo label="Fecha *" id="res-fecha">
          <Input id="res-fecha" name="fecha" type="date" required />
        </Campo>
        {selectorEvidencia('evidencia', 'Comprobante de pago o resolución *')}
        <FormCampo span={12}>
          <p className="text-xs text-ink-muted">
            Adjunta primero los comprobantes en la pestaña Documentos.
          </p>
          {accion.isError && (
            <p role="alert" className="text-danger-fg">
              {errorTexto(accion.error)}
            </p>
          )}
          <Button
            disabled={accion.isPending}
            title={accion.isPending ? 'Guardando comprobación.' : undefined}
          >
            Guardar
          </Button>
          <Button type="button" variant="ghost" onClick={onClose}>
            Descartar
          </Button>
        </FormCampo>
      </FormSeccion>
    </form>
  );
}

function ContenedorImportacion({ id }: { id: string }) {
  const puede = useHasPermission(P.ComercioExteriorContenedoresGestionar);
  const accion = useMutarContenedor(id);
  useContenedores(id);
  if (!puede) return null;
  return (
    <details>
      <summary>Agregar contenedor de importación</summary>
      <form
        className="mt-3 flex items-end gap-3"
        onSubmit={(ev) => {
          ev.preventDefault();
          const d = new FormData(ev.currentTarget);
          void accion
            .mutateAsync({ ruta: 'contenedores', body: { numero: d.get('numero') } })
            .catch(() => {});
        }}
      >
        <label className="space-y-1">
          Número de contenedor *<Input name="numero" required maxLength={200} />
        </label>
        <Button
          disabled={accion.isPending}
          title={accion.isPending ? 'Guardando contenedor.' : undefined}
        >
          Guardar contenedor
        </Button>
      </form>
      {accion.isError && (
        <p role="alert" className="text-danger-fg">
          {errorTexto(accion.error)}
        </p>
      )}
    </details>
  );
}
