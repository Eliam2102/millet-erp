import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { ArrowLeft, Pencil } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useAncestros, useCuenta, useCuentas } from '../api/hooks';
import { AvisoNota } from '../components/AvisoNota';
import { ConfirmarEstatusCuenta } from '../components/ConfirmarEstatusCuenta';
import { CuentaForm, MOTIVO_BLOQUEO } from '../components/CuentaForm';
import { InsigniasCuenta } from '../components/InsigniasCuenta';
import { ProbarMovimiento } from '../components/ProbarMovimiento';
import { ETIQUETA_COLECTIVA, ETIQUETA_TIPO } from '../lib/textos';

const Dato = ({ k, children }: { k: string; children: React.ReactNode }) => (
  <div>
    <dt className="text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">{k}</dt>
    <dd className="text-sm">{children}</dd>
  </div>
);
const PENDIENTE = <span className="font-medium text-warning-fg">Pendiente de validación</span>;

/** Detalle de una cuenta (P3): datos, ancestros, hijos, orígenes, candado "usada" y edición inline con If-Match. */
export function CuentaDetallePage({ id }: { id: string }) {
  const puedeAdministrar = useHasPermission(PermisosCanonicos.ContabilidadCatalogoAdministrar);
  const detalle = useCuenta(id);
  const cuenta = detalle.data?.data;
  const ancestros = useAncestros(cuenta);
  const hijos = useCuentas({ padreId: id, limit: 200 }, cuenta !== undefined);
  const esRubro = cuenta?.clase === 'Rubro';
  // P24: cuentas de nivel 1 que agrupa el rubro, y el rubro de una cuenta raíz.
  const delRubro = useCuentas({ rubroId: id, limit: 200 }, esRubro);
  const rubro = useCuenta(cuenta?.rubroId ?? null);
  const [editando, setEditando] = useState(false);
  const [confirmar, setConfirmar] = useState<'desactivar' | 'reactivar' | null>(null);

  const volver = (
    <Link to="/contabilidad/catalogo" className="inline-flex items-center gap-1 text-sm text-ink-muted hover:underline">
      <ArrowLeft className="size-4" aria-hidden="true" />Catálogo de cuentas
    </Link>
  );

  if (detalle.isLoading) {
    return (
      <div className="space-y-3 px-6 py-5" data-testid="detalle-cargando" aria-label="Cargando cuenta">
        <Skeleton className="h-8 w-1/2" /><Skeleton className="h-32 w-full" />
      </div>
    );
  }
  if (detalle.isError || !cuenta) {
    const noExiste = esApiError(detalle.error) && detalle.error.status === 404;
    return (
      <div className="space-y-3 px-6 py-5">
        {volver}
        <div role="alert" className="flex items-center gap-2 rounded-lg border border-danger p-4 text-danger-fg">
          {noExiste ? 'La cuenta no existe.' : 'No se pudo cargar la cuenta.'}
          {!noExiste && <Button variant="ghost" size="sm" onClick={() => void detalle.refetch()}>Reintentar</Button>}
        </div>
      </div>
    );
  }

  const padre = ancestros.data?.at(-1) ?? null;
  return (
    <div className="space-y-5 px-6 py-5">
      <div data-print="hidden">{volver}</div>
      <header className="space-y-2">
        <h1 className="flex flex-wrap items-center gap-2 text-3xl font-semibold">
          <span className="font-mono">{cuenta.codigo}</span>
          <span>{cuenta.nombre}</span>
        </h1>
        <InsigniasCuenta
          tipo={cuenta.tipo}
          naturaleza={cuenta.naturaleza}
          activa={cuenta.activa}
          pendienteValidacion={cuenta.pendienteValidacion}
          control={cuenta.cuentaControl}
          usada={cuenta.usada}
          clase={cuenta.clase}
        />
        {puedeAdministrar && !editando && (
          <div className="flex gap-2 pt-1" data-print="hidden">
            <Button variant="outline" size="sm" onClick={() => setEditando(true)}>
              <Pencil className="mr-1 size-4" aria-hidden="true" />Editar
            </Button>
            {cuenta.activa ? (
              <Button variant="secondary-danger" size="sm" onClick={() => setConfirmar('desactivar')}>Desactivar</Button>
            ) : (
              <Button variant="outline" size="sm" onClick={() => setConfirmar('reactivar')}>Reactivar</Button>
            )}
          </div>
        )}
      </header>

      {cuenta.usada && (
        <AvisoNota data-testid="aviso-usada">Esta cuenta tiene movimientos. {MOTIVO_BLOQUEO}</AvisoNota>
      )}

      {editando && puedeAdministrar && (
        <CuentaForm
          // El borrador sobrevive a un refetch (409 → recargar): la `key` solo cambia al cambiar de cuenta.
          key={cuenta.id}
          cuenta={cuenta}
          padreActual={padre}
          onGuardada={() => setEditando(false)}
          onCancelar={() => setEditando(false)}
        />
      )}

      <section aria-label="Datos de la cuenta">
        <dl className="grid grid-cols-1 gap-4 rounded-lg bg-surface-card shadow-card-flat p-4 sm:grid-cols-3">
          <Dato k="Nivel">{cuenta.nivel}</Dato>
          <Dato k="Naturaleza">{cuenta.naturaleza ?? (esRubro ? '—' : PENDIENTE)}</Dato>
          <Dato k="Tipo">{esRubro ? 'Rubro de reporte (no recibe movimientos)' : cuenta.tipo === null ? PENDIENTE : ETIQUETA_TIPO[cuenta.tipo]}</Dato>
          <Dato k="Estatus">{cuenta.estatus}</Dato>
          <Dato k="No afectable por asiento manual">{cuenta.noAfectableManual ? 'Sí' : 'No'}</Dato>
          <Dato k="Cuenta colectiva">{ETIQUETA_COLECTIVA[cuenta.cuentaControl] ?? cuenta.cuentaControl}</Dato>
          {!esRubro && cuenta.padreId === null && (
            <Dato k="Rubro de reporte">
              {rubro.data ? (
                <Link to="/contabilidad/catalogo/$id" params={{ id: rubro.data.data.id }} className="hover:underline">
                  <span className="font-mono text-xs">{rubro.data.data.codigo}</span> {rubro.data.data.nombre}
                </Link>
              ) : cuenta.rubroId ? '…' : 'Sin rubro'}
            </Dato>
          )}
          <Dato k="Código agrupador">{cuenta.codigoAgrupador ?? '—'}</Dato>
          <Dato k="Grupo de reporte">{cuenta.grupoReporte ?? '—'}</Dato>
          <Dato k="Versión">{cuenta.version}</Dato>
        </dl>
      </section>

      <ProbarMovimiento key={cuenta.id} cuentaId={cuenta.id} />

      {esRubro && (
        <section aria-label="Cuentas del rubro" className="space-y-1">
          <h2 className="text-sm font-semibold">Cuentas de nivel 1 del rubro</h2>
          <p className="text-xs text-ink-muted">El rubro suma el saldo de estas cuentas una sola vez (sin volver a sumar sus cuentas de debajo).</p>
          {delRubro.isLoading ? (
            <Skeleton className="h-5 w-1/2" />
          ) : (delRubro.data?.items ?? []).length === 0 ? (
            <p className="text-sm text-ink-muted">El rubro aún no agrupa cuentas.</p>
          ) : (
            <ul className="space-y-1 text-sm">
              {delRubro.data!.items.map((h) => (
                <li key={h.id}>
                  <Link to="/contabilidad/catalogo/$id" params={{ id: h.id }} className="hover:underline">
                    <span className="font-mono text-xs">{h.codigo}</span> {h.nombre}
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}

      <section aria-label="Ancestros" className="space-y-1">
        <h2 className="text-sm font-semibold">Ruta de ancestros</h2>
        {ancestros.isLoading ? (
          <Skeleton className="h-5 w-1/2" />
        ) : (ancestros.data ?? []).length === 0 ? (
          <p className="text-sm text-ink-muted">Cuenta raíz.</p>
        ) : (
          <ol className="flex flex-wrap items-center gap-1 text-sm">
            {ancestros.data!.map((a, i) => (
              <li key={a.id} className="flex items-center gap-1">
                {i > 0 && <span aria-hidden="true">›</span>}
                <Link to="/contabilidad/catalogo/$id" params={{ id: a.id }} className="hover:underline">
                  <span className="font-mono text-xs">{a.codigo}</span> {a.nombre}
                </Link>
              </li>
            ))}
          </ol>
        )}
      </section>

      <section aria-label="Hijas" className="space-y-1">
        <h2 className="text-sm font-semibold">Cuentas hijas</h2>
        {hijos.isLoading ? (
          <Skeleton className="h-5 w-1/2" />
        ) : (hijos.data?.items ?? []).length === 0 ? (
          <p className="text-sm text-ink-muted">Sin cuentas hijas.</p>
        ) : (
          <ul className="space-y-1 text-sm">
            {hijos.data!.items.map((h) => (
              <li key={h.id} className="flex items-center gap-2">
                <Link to="/contabilidad/catalogo/$id" params={{ id: h.id }} className="hover:underline">
                  <span className="font-mono text-xs">{h.codigo}</span> {h.nombre}
                </Link>
                <InsigniasCuenta tipo={h.tipo} activa={h.activa} pendienteValidacion={h.pendienteValidacion} />
              </li>
            ))}
          </ul>
        )}
      </section>

      <section aria-label="Orígenes" className="space-y-1">
        <h2 className="text-sm font-semibold">Orígenes (correspondencia con otros sistemas)</h2>
        {(cuenta.origenes ?? []).length === 0 ? (
          <p className="text-sm text-ink-muted">Sin orígenes registrados.</p>
        ) : (
          <ul className="text-sm">
            {cuenta.origenes!.map((o) => (
              <li key={`${o.fuente}:${o.codigoOrigen}`}><span className="font-medium">{o.fuente}</span> · <span className="font-mono">{o.codigoOrigen}</span></li>
            ))}
          </ul>
        )}
      </section>

      {confirmar && <ConfirmarEstatusCuenta cuenta={cuenta} accion={confirmar} onClose={() => setConfirmar(null)} />}
    </div>
  );
}
