import { DocumentoProveedorSelector } from '../components/DocumentoProveedorSelector';
import { useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/tabs';
import { Badge } from '@/components/ui/badge';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useActivo, useActivos, useGuardarActivo, useClasesActivo } from '../api/hooks';
import { ActivoForm, CustodiaFields } from '../components/ActivoForm';
import { dinero, estadoActivo, type Custodia } from '../api/types';
export function ActivoDetallePage() {
  const { id } = useParams({ from: '/_app/activos-fijos/activos/$id' });
  const query = useActivo(id);
  const lista = useActivos({});
  const clases = useClasesActivo();
  const guardar = useGuardarActivo();
  const [editar, setEditar] = useState(false);
  const [destino, setDestino] = useState<Custodia | null>(null);
  const [documento, setDocumento] = useState('');
  const [ajuste, setAjuste] = useState<'mejora' | 'nota-credito' | null>(null);
  const gestionar = useHasPermission(P.ActivosFijosActivosGestionar);
  const capitalizar = useHasPermission(P.ActivosFijosCapitalizar);
  const trasladar = useHasPermission(P.ActivosFijosTrasladosGestionar);
  const a = query.data?.data;
  if (!a)
    return (
      <p role={query.error ? 'alert' : undefined}>{query.error?.message ?? 'Cargando activo…'}</p>
    );
  const accion = (nombre: string, body: unknown = {}) =>
    guardar.mutate(
      { ruta: `activos/${a.id}/${nombre}`, body, version: a.version },
      {
        onSuccess: () => {
          setDestino(null);
          setAjuste(null);
        },
      },
    );
  return (
    <main className="flex h-full min-h-0 gap-5 text-sm">
      <aside
        className="hidden w-80 shrink-0 overflow-auto rounded-lg bg-surface-card p-4 shadow-card-flat lg:block"
        data-print="hidden"
      >
        <Link to="/activos-fijos/activos" className="text-brand">
          Todos los activos
        </Link>
        {lista.data?.items.map((x) => (
          <Link
            key={x.id}
            to="/activos-fijos/activos/$id"
            params={{ id: x.id }}
            className="block border-b border-line-row py-3"
          >
            <span className="font-mono">{x.numero}</span>
            <p className="text-ink-muted">{x.descripcion}</p>
          </Link>
        ))}
      </aside>
      <section className="min-w-0 flex-1 space-y-5 overflow-auto">
        <div className="flex flex-wrap items-center gap-3" data-print="hidden">
          <h1 className="font-mono text-xl font-semibold">{a.numero}</h1>
          <Badge variant={a.estado === 1 ? 'success' : 'warning'}>{estadoActivo[a.estado]}</Badge>
          {a.enTraslado && <Badge variant="info">En traslado</Badge>}
          {gestionar && a.estado === 0 && (
            <Button variant="outline" onClick={() => setEditar(true)}>
              Completar datos
            </Button>
          )}
          {capitalizar && a.estado === 0 && (
            <Button onClick={() => accion('capitalizar')}>Capitalizar</Button>
          )}
          <Link to="/activos-fijos/activos" className="text-brand">
            Cerrar
          </Link>
        </div>
        <h2 className="text-lg font-semibold">{a.descripcion}</h2>
        {guardar.error && (
          <p role="alert" className="text-danger-fg">
            {guardar.error.message}
          </p>
        )}
        <Tabs defaultValue="datos">
          <TabsList>
            <TabsTrigger value="datos">Datos</TabsTrigger>
            <TabsTrigger value="valores">Valores e historial</TabsTrigger>
            <TabsTrigger value="traslados">Traslados</TabsTrigger>
            <TabsTrigger value="origen">Origen</TabsTrigger>
          </TabsList>
          <TabsContent value="datos">
            <dl className="grid grid-cols-2 gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat">
              {Object.entries({
                Clase: clases.data?.find((c) => c.id === a.claseId)?.nombre,
                Responsable: a.custodiaDescripcion?.responsable,
                Sucursal: a.custodiaDescripcion?.sucursal,
                'Centro de costo': a.custodiaDescripcion?.centroCosto,
                Equipo: a.custodiaDescripcion?.equipo,
                Marca: a.marca,
                Modelo: a.modelo,
                Serie: a.numeroSerie,
                Adquisición: a.fechaAdquisicion,
                'Inicio de uso': a.inicioUso,
                Ubicación: a.ubicacion,
                Importación: ['No', 'Temporal', 'Definitiva'][a.importacion],
                Pedimento: a.pedimento,
                Arrendado: a.arrendado ? 'Sí' : 'No',
              }).map(([k, v]) => (
                <div key={k}>
                  <dt className="text-xs text-ink-muted">{k}</dt>
                  <dd>{v || 'Sin registrar'}</dd>
                </div>
              ))}
            </dl>
          </TabsContent>
          <TabsContent value="valores">
            <div className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
              <p className="tabular-nums">
                Original: {dinero(a.valorOriginal)} · Actual: {dinero(a.valorActual)} ·
                Depreciación: {dinero(a.depreciacionAcumulada)} · En libros:{' '}
                {dinero(a.valorEnLibros)}
              </p>
              <p className="text-xs text-ink-muted">
                Moneda de adquisición: {a.moneda} · Importe original:{' '}
                {a.valorMonedaOriginal.toLocaleString('es-MX')} · Tipo de cambio: {a.tipoCambio}
              </p>
              {a.historial.map((h) => (
                <p key={h.id} className="border-b border-line-row py-2 tabular-nums">
                  {h.fecha} · {h.descripcion} · {dinero(h.importe)}
                </p>
              ))}
              {capitalizar && a.estado === 1 && (
                <div className="flex gap-2">
                  <Button
                    variant="outline"
                    onClick={() => {
                      setDocumento('');
                      setAjuste('mejora');
                    }}
                  >
                    Registrar mejora
                  </Button>
                  <Button
                    variant="outline"
                    disabled={!a.facturaProveedorId}
                    title={
                      !a.facturaProveedorId
                        ? 'El activo requiere una factura de proveedor origen'
                        : undefined
                    }
                    onClick={() => {
                      setDocumento('');
                      setAjuste('nota-credito');
                    }}
                  >
                    Nota de crédito
                  </Button>
                </div>
              )}
              {ajuste && (
                <form
                  className="grid grid-cols-2 gap-3"
                  onSubmit={(e) => {
                    e.preventDefault();
                    const f = new FormData(e.currentTarget);
                    accion(ajuste, {
                      ajuste: {
                        documentoId: documento,
                        facturaId: ajuste === 'nota-credito' ? a.facturaProveedorId : documento,
                        fecha: f.get('fecha'),
                        importe: Number(f.get('importe')),
                        descripcion: f.get('descripcion'),
                      },
                    });
                  }}
                >
                  <DocumentoProveedorSelector
                    activoId={a.id}
                    credito={ajuste === 'nota-credito'}
                    facturaId={a.facturaProveedorId}
                    value={documento}
                    onChange={setDocumento}
                  />
                  {[
                    ['fecha', 'Fecha', 'date'],
                    ['importe', 'Importe', 'number'],
                    ['descripcion', 'Descripción', 'text'],
                  ].map(([name, label, type]) => (
                    <label key={name} className="text-xs">
                      {label} *
                      <Input
                        name={name}
                        required
                        type={type}
                        step={type === 'number' ? '0.01' : undefined}
                        min={type === 'number' ? '0.01' : undefined}
                        defaultValue={name === 'factura' ? (a.facturaProveedorId ?? '') : undefined}
                      />
                    </label>
                  ))}
                  <Button type="submit">Guardar ajuste</Button>
                  <Button type="button" variant="outline" onClick={() => setAjuste(null)}>
                    Cancelar
                  </Button>
                </form>
              )}
            </div>
          </TabsContent>
          <TabsContent value="traslados">
            <div className="space-y-4 rounded-lg bg-surface-card p-4 shadow-card-flat">
              {a.traslados.map((t) => (
                <div key={t.id} className="border-b border-line-row py-3">
                  <p>
                    {t.registradoEn} ·{' '}
                    {t.aceptadoEn ? `Aceptado: ${t.aceptadoEn}` : 'Pendiente de aceptación'}
                  </p>
                  <p>
                    Origen: {t.origen.ubicacion} · Sucursal:{' '}
                    {t.origenDescripcion?.sucursal ?? 'Sin registrar'} · Equipo:{' '}
                    {t.origenDescripcion?.equipo ?? 'Sin equipo'}
                  </p>
                  <p>
                    Destino: {t.destino.ubicacion} · Sucursal:{' '}
                    {t.destinoDescripcion?.sucursal ?? 'Sin registrar'} · Equipo:{' '}
                    {t.destinoDescripcion?.equipo ?? 'Sin equipo'}
                  </p>
                  {trasladar && !t.aceptadoEn && a.puedeAceptarTraslado && (
                    <Button onClick={() => accion('aceptar', { trasladoId: t.id })}>
                      Aceptar custodia
                    </Button>
                  )}
                </div>
              ))}
              {trasladar && a.estado === 1 && (
                <Button
                  variant="outline"
                  disabled={a.enTraslado}
                  title={a.enTraslado ? 'Debe aceptarse el traslado pendiente' : undefined}
                  onClick={() =>
                    setDestino({
                      responsableId: a.responsableId,
                      sucursalId: a.sucursalId,
                      ubicacion: a.ubicacion,
                      centroCostoId: a.centroCostoId,
                      equipoId: a.equipoId,
                    })
                  }
                >
                  Registrar traslado
                </Button>
              )}
              {destino && (
                <form
                  className="space-y-3"
                  onSubmit={(e) => {
                    e.preventDefault();
                    accion('trasladar', { destino });
                  }}
                >
                  <CustodiaFields value={destino} onChange={setDestino} />
                  <Button type="submit">Solicitar aceptación</Button>
                </form>
              )}
            </div>
          </TabsContent>
          <TabsContent value="origen">
            <dl className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
              {Object.entries({
                'Orden de compra': a.ordenCompraId,
                Recepción: a.recepcionId,
                'Factura de proveedor': a.facturaProveedorId,
                'Documento de soporte': a.documentoSoporte,
                'Póliza de alta': a.polizaAltaId,
              }).map(([k, v]) => (
                <div key={k}>
                  <dt className="text-xs text-ink-muted">{k}</dt>
                  <dd className="font-mono">{v ?? 'Sin vincular'}</dd>
                </div>
              ))}
            </dl>
          </TabsContent>
        </Tabs>
        {editar && <ActivoForm activo={a} onClose={() => setEditar(false)} />}
      </section>
    </main>
  );
}
