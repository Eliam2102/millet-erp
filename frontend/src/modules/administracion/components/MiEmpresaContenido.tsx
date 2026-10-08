import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { ErrorState } from '@/components/erp';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useConfiguracionPac } from '@/features/integraciones-fiscal/api/useIntegracionesFiscal';
import { ProveedorPac } from '@/features/integraciones-fiscal/api/types';
import type { EmpresaDetalleResponse } from '../api/types';
import { EmpresaDatosForm } from './EmpresaDatosForm';

const estadosCsd = {
  Vigente: { texto: 'Vigente', variante: 'success' },
  ProximoAVencer: { texto: 'Próximo a vencer', variante: 'warning' },
  Vencido: { texto: 'Vencido', variante: 'danger' },
} as const;

function fecha(valor: string | null | undefined) {
  return valor
    ? new Intl.DateTimeFormat('es-MX', { dateStyle: 'medium', timeStyle: 'short' }).format(
        new Date(valor),
      )
    : 'Por confirmar';
}

export function MiEmpresaContenido({ detalle }: { detalle: EmpresaDetalleResponse }) {
  const { empresa, sucursales } = detalle;
  const [pagina, setPagina] = useState(0);
  const paginaActual = Math.min(pagina, Math.max(0, Math.ceil(sucursales.length / 10) - 1));
  const canFiscal = useHasPermission(P.IntegracionesFiscalLeer);
  const canSeries = useHasPermission(P.AdminSeriesGestionar);
  const canSucursales = useHasPermission(P.AdminEmpresasSucursalesGestionar);
  const pac = useConfiguracionPac(canFiscal ? empresa.id : null, ProveedorPac.FiscalApi);
  const config = pac.data;
  const csd = config?.csdEstado ? estadosCsd[config.csdEstado] : null;

  return (
    <div className="flex flex-col gap-5 p-4 text-sm text-ink md:px-7 md:py-6">
      <header className="space-y-2">
        <nav aria-label="Ubicación" className="text-xs text-ink-muted">
          <Link to="/admin">Administración</Link> /{' '}
          <span aria-current="page" className="font-medium text-ink">
            Mi empresa
          </span>
        </nav>
        <div className="flex items-center gap-3">
          <h1 className="text-3xl font-semibold">Mi empresa</h1>
          <Badge variant={empresa.activa ? 'success' : 'neutral'}>
            {empresa.activa ? 'Activa' : 'Inactiva'}
          </Badge>
        </div>
        <p>
          {empresa.razonSocial} · <span className="font-mono">{empresa.rfc}</span>
        </p>
        <p className="text-ink-muted">
          Consulta y actualiza los datos fiscales y revisa la configuración de facturación de
          Millet.
        </p>
      </header>

      <section aria-labelledby="datos-fiscales" className="space-y-3">
        <h2 id="datos-fiscales" className="text-md font-semibold">
          Datos fiscales
        </h2>
        {!empresa.codigoPostal && (
          <p role="note" className="rounded-md bg-warning-note-bg px-3 py-2.5 text-warning-note-fg">
            Falta el código postal fiscal. Captúralo para habilitar la emisión de CFDI.
          </p>
        )}
        <EmpresaDatosForm key={empresa.id} empresa={empresa} />
      </section>

      <section
        aria-labelledby="facturacion"
        className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card"
      >
        <h2 id="facturacion" className="text-md font-semibold">
          Facturación
        </h2>
        {!canFiscal ? (
          <p className="text-ink-muted">
            Por confirmar: se requiere permiso para consultar Integraciones fiscales.
          </p>
        ) : pac.isError ? (
          <ErrorState onRetry={() => pac.refetch()} />
        ) : pac.isPending ? (
          <p role="status">Cargando configuración fiscal…</p>
        ) : (
          <dl className="grid gap-4 md:grid-cols-4">
            <div>
              <dt className="text-xs text-ink-muted">PAC</dt>
              <dd className="mt-1 font-medium">
                <Badge variant={config?.apiKeyConfigured ? 'success' : 'warning'}>
                  {config?.apiKeyConfigured ? 'Configurado' : 'Sin configurar'}
                </Badge>
                {config && (
                  <p className="mt-1">
                    {config.proveedorNombre} · {config.activo ? 'Activo' : 'Inactivo'}
                  </p>
                )}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-ink-muted">Última prueba de conexión</dt>
              <dd className="mt-1 font-medium">
                {config?.ultimaTestConexionAt
                  ? fecha(config.ultimaTestConexionAt)
                  : 'Sin prueba registrada'}
                {config?.ultimaTestConexionAt && (
                  <p>
                    {config.ultimaTestConexionExitosa === true
                      ? 'Exitosa'
                      : config.ultimaTestConexionExitosa === false
                        ? 'Fallida'
                        : 'Resultado por confirmar'}
                  </p>
                )}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-ink-muted">CSD</dt>
              <dd className="mt-1 font-medium">
                <Badge variant={config?.csdConfigurado ? (csd?.variante ?? 'warning') : 'warning'}>
                  {!config?.csdConfigurado
                    ? 'Sin configurar'
                    : (csd?.texto ?? 'Vigencia por confirmar')}
                </Badge>
              </dd>
            </div>
            <div>
              <dt className="text-xs text-ink-muted">Vigencia del CSD</dt>
              <dd className="mt-1 font-medium">
                Desde: {fecha(config?.csdNotBefore)}
                <br />
                Hasta: {fecha(config?.csdNotAfter)}
              </dd>
            </div>
          </dl>
        )}
        <div className="flex flex-wrap gap-2">
          {canFiscal && (
            <Button variant="secondary" asChild>
              <Link to="/admin/integraciones/fiscal">Integraciones fiscales</Link>
            </Button>
          )}
          {canSeries && (
            <Button variant="secondary" asChild>
              <Link to="/admin/series">Series y folios</Link>
            </Button>
          )}
        </div>
      </section>

      <section
        aria-labelledby="sucursales"
        className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card"
      >
        <h2 id="sucursales" className="text-md font-semibold">
          Sucursales
        </h2>
        {sucursales.length === 0 ? (
          <p className="text-ink-muted">No hay sucursales registradas.</p>
        ) : (
          <table className="w-full text-left text-sm">
            <thead className="bg-surface-subtle text-xs text-ink-muted">
              <tr>
                <th className="px-3 py-2 font-semibold">Sucursal</th>
                <th className="px-3 py-2 font-semibold">Clave A+W</th>
                <th className="px-3 py-2 font-semibold">Configuración</th>
              </tr>
            </thead>
            <tbody>
              {sucursales.slice(paginaActual * 10, (paginaActual + 1) * 10).map((sucursal) => (
                <tr className="border-b border-line-row" key={sucursal.id}>
                  <td className="px-3 py-3">
                    {sucursal.nombre}{' '}
                    <span className="font-mono text-xs text-ink-muted">{sucursal.clave}</span>
                  </td>
                  <td className="px-3 py-3 font-mono">{sucursal.claveAw ?? 'Sin asignar'}</td>
                  <td className="px-3 py-3">
                    {canSucursales ? (
                      <Link
                        to="/admin/sucursales/$id"
                        params={{ id: sucursal.id }}
                        className="text-brand"
                      >
                        Configurar {sucursal.nombre}
                      </Link>
                    ) : (
                      'Sin permiso de configuración'
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {sucursales.length > 10 && (
          <div className="flex items-center justify-between gap-2 border-t border-line-divider pt-3 text-xs text-ink-muted">
            <span>
              {paginaActual * 10 + 1}–{Math.min((paginaActual + 1) * 10, sucursales.length)} de{' '}
              {sucursales.length}
            </span>
            <div className="flex gap-2">
              <Button
                variant="secondary"
                size="sm"
                disabled={paginaActual === 0}
                title={paginaActual === 0 ? 'Estás en la primera página.' : undefined}
                onClick={() => setPagina(paginaActual - 1)}
              >
                Anterior
              </Button>
              <Button
                variant="secondary"
                size="sm"
                disabled={(paginaActual + 1) * 10 >= sucursales.length}
                title={
                  (paginaActual + 1) * 10 >= sucursales.length
                    ? 'Estás en la última página.'
                    : undefined
                }
                onClick={() => setPagina(paginaActual + 1)}
              >
                Siguiente
              </Button>
            </div>
          </div>
        )}
      </section>
    </div>
  );
}
