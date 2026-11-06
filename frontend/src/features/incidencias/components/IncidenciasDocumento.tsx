import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Button } from '@/components/ui/button';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { esApiError } from '@/lib/api';
import { formatDate } from '@/lib/datetime';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useIncidenciasDocumento } from '@/features/incidencias/api/useIncidencias';
import { ESTADOS_PENDIENTES, type ContextoIncidencia } from '@/features/incidencias/api/types';
import { EstadoIncidenciaBadge } from '@/features/incidencias/lib/estado';
import { folioIncidencia } from '@/features/incidencias/lib/formato';
import { RegistrarIncidenciaDialog } from './RegistrarIncidencia';

export interface IncidenciasDocumentoProps {
  modulo: string;
  tipo: string;
  id: string;
  /** Texto del documento para el diálogo de registro (folio y referencia). */
  etiqueta?: string;
  sucursalId?: string | null;
  /** Si se pasa, aparece «Registrar incidencia» para este paso del documento. */
  paso?: string;
  pasoReanudacion?: string | null;
}

/**
 * Sección «Incidencias» para el detalle de cualquier documento: lista las del documento (pendientes
 * primero) con su estado, responsable y enlace al detalle. Sin `compartido.incidencias.ver` no se muestra;
 * el responsable las ve en «Mis incidencias». El servidor filtra por sucursal.
 */
export function IncidenciasDocumento({
  modulo,
  tipo,
  id,
  etiqueta,
  sucursalId,
  paso,
  pasoReanudacion,
}: IncidenciasDocumentoProps) {
  const puedeVer = useHasPermission(PermisosCanonicos.CompartidoIncidenciasVer);
  const puedeRegistrar = useHasPermission(PermisosCanonicos.CompartidoIncidenciasRegistrar);
  const usuarioId = useAuthStore((s) => s.user?.id);
  const [registrar, setRegistrar] = useState(false);
  const query = useIncidenciasDocumento({ modulo, tipoDocumento: tipo, documentoId: id }, puedeVer);

  if (!puedeVer) return null;

  const items = [...(query.data?.items ?? [])].sort(
    (a, b) => Number(ESTADOS_PENDIENTES.includes(b.estado)) - Number(ESTADOS_PENDIENTES.includes(a.estado)),
  );
  const contexto: ContextoIncidencia | null = paso
    ? {
        modulo,
        tipoDocumento: tipo,
        documentoId: id,
        documentoEtiqueta: etiqueta ?? '',
        sucursalId: sucursalId ?? null,
        paso,
        pasoReanudacion: pasoReanudacion ?? null,
      }
    : null;

  return (
    <section className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat" aria-label="Incidencias del documento">
      <div className="flex items-center justify-between gap-2">
        <h2 className="text-md font-semibold text-ink">Incidencias</h2>
        {contexto && puedeRegistrar && (
          <Button variant="secondary" size="sm" onClick={() => setRegistrar(true)}>
            Registrar incidencia
          </Button>
        )}
      </div>
      {query.isLoading ? (
        <TableSkeleton rows={2} columns={[{ width: 'w-full' }]} />
      ) : query.isError ? (
        <ErrorState
          title="No se pudieron cargar las incidencias"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : items.length === 0 ? (
        <p className="text-sm text-ink-muted">Este documento no tiene incidencias.</p>
      ) : (
        <ul className="divide-y divide-line-row">
          {items.map((i) => (
            <li key={i.id} className="flex flex-wrap items-start justify-between gap-3 py-2.5">
              <div className="min-w-0 space-y-0.5">
                <div className="flex flex-wrap items-center gap-2">
                  <Link
                    to="/incidencias/$id"
                    params={{ id: i.id }}
                    className="font-mono text-xs font-medium text-brand hover:text-brand-hover"
                  >
                    {folioIncidencia(i.numero)}
                  </Link>
                  <span className="text-sm text-ink">{i.pasoNombre}</span>
                </div>
                <p className="text-xs text-ink-muted">{i.motivo}</p>
              </div>
              <div className="flex shrink-0 flex-col items-end gap-1">
                <EstadoIncidenciaBadge estado={i.estado} />
                <span className="text-xs text-ink-muted">
                  {i.responsableUsuarioId === usuarioId ? 'Asignada a ti' : i.responsableNombre || 'Sin responsable'}
                  {' · '}
                  {formatDate(i.abiertaEn)}
                </span>
              </div>
            </li>
          ))}
        </ul>
      )}
      {contexto && (
        <RegistrarIncidenciaDialog contexto={contexto} open={registrar} onOpenChange={setRegistrar} />
      )}
    </section>
  );
}
