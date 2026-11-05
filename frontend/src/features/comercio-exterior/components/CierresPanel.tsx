import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { useAccionExpediente } from '../api/hooks';
import type { ExpedienteDetalle } from '../api/types';

const nombres: Record<string, string> = {
  fisico: 'Cierre físico',
  documental: 'Cierre documental',
  financiero: 'Cierre financiero',
};
export function CierresPanel({ data }: { data: ExpedienteDetalle }) {
  const accion = useAccionExpediente(data.expediente.id);
  const gestionar =
    useHasPermission(P.ComercioExteriorExpedientesCerrar) && data.expediente.estado === 1;
  return (
    <section className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <h2 className="font-semibold">Cierres del expediente</h2>
      {(data.cierres ?? []).map((c) => (
        <article key={c.dimension} className="space-y-2 border-b border-line-divider py-3">
          <h3>
            {nombres[c.dimension] ?? c.dimension}{' '}
            <Badge variant={c.fecha && c.faltantes.length === 0 ? 'success' : 'warning'}>
              {c.fecha && c.faltantes.length === 0
                ? 'Registrado'
                : c.faltantes.length
                  ? 'Pendiente'
                  : 'Listo para registrar'}
            </Badge>
          </h3>
          {c.fecha && (
            <p className="text-xs text-ink-muted">
              Registrado por {c.usuarioNombre ?? 'Sin nombre'}:{' '}
              {new Date(c.fecha).toLocaleString('es-MX')}
            </p>
          )}
          {c.faltantes.length > 0 && (
            <ul className="list-disc pl-5">
              {c.faltantes.map((f) => (
                <li key={f}>{f}</li>
              ))}
            </ul>
          )}
          {gestionar && (
            <Button
              variant="outline"
              disabled={accion.isPending || c.faltantes.length > 0}
              title={
                c.faltantes.length
                  ? c.faltantes.join(' ')
                  : accion.isPending
                    ? 'Registrando cierre.'
                    : undefined
              }
              onClick={() => {
                void accion.mutateAsync({ accion: `cerrar-${c.dimension}` }).catch(() => {});
              }}
            >
              Registrar {nombres[c.dimension]?.toLowerCase()}
            </Button>
          )}
        </article>
      ))}
      <p className="text-xs text-ink-muted">
        {data.cierreAprobadoEn
          ? `Gerencia aprobó el cierre: ${new Date(data.cierreAprobadoEn).toLocaleString('es-MX')}`
          : 'El expediente se cierra cuando Gerencia aprueba los tres cierres registrados.'}
      </p>
      {accion.isError && (
        <p role="alert" className="text-danger-fg">
          {esApiError(accion.error)
            ? (accion.error.problem.detail ?? accion.error.problem.title)
            : 'No se pudo registrar el cierre.'}
        </p>
      )}
    </section>
  );
}
export function FacturasPanel({ data }: { data: ExpedienteDetalle }) {
  if (!data.facturas?.length) return null;
  return (
    <section className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <h2 className="font-semibold">Facturas de exportación</h2>
      {data.facturas.map((f) => (
        <article key={f.id} className="space-y-1 border-b border-line-divider py-2">
          <p className="font-mono text-xs">
            {f.folio} · {f.uuid}
          </p>
          <p>
            Versión del embarque: {f.versionEmbarque} · {f.cancelada ? 'Cancelada' : 'Timbrada'}
          </p>
          {f.fechaTimbrado && <p>Timbrado: {new Date(f.fechaTimbrado).toLocaleString('es-MX')}</p>}
          {f.uuidOriginal && (
            <p>
              Factura original{' '}
              {data.facturas?.find((o) => o.uuid === f.uuidOriginal)?.folio ?? f.uuidOriginal} →
              Sustituta {f.folio}
            </p>
          )}
        </article>
      ))}
    </section>
  );
}
