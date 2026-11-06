import { Link, useParams } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { ArrowRight } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { PageHeader } from '@/components/erp/PageHeader';
import { DetalleSkeleton, ErrorState } from '@/components/erp';
import { DocumentoAdjuntosSection } from '@/components/erp/adjuntos/DocumentoAdjuntosSection';
import { ListaCompacta } from '@/components/erp/MasterDetail';
import { paginaAOffsetLimit } from '@/components/erp/data/paginacion';
import { esApiError } from '@/lib/api';
import { formatDateTime } from '@/lib/datetime';
import { problemaUsuario } from '@/lib/error-usuario';
import { useIncidencia, useMisIncidencias } from '@/features/incidencias/api/useIncidencias';
import type { IncidenciaDetalle } from '@/features/incidencias/api/types';
import { EstadoIncidenciaBadge } from '@/features/incidencias/lib/estado';
import { fechaCompromisoTexto, folioIncidencia, origenIncidencia, rutaInterna } from '@/features/incidencias/lib/formato';
import { estadosDeVista, type IncidenciasSearch } from '@/features/incidencias/lib/search-schema';
import { nombreModulo } from '@/features/incidencias/lib/modulos';
import { AccionesIncidencia } from '@/features/incidencias/components/AccionesIncidencia';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

const ACCION_HISTORIAL: Record<string, string> = {
  Abierta: 'Abrió la incidencia',
  Atendida: 'La pasó a atención',
  Resuelta: 'La resolvió',
  Cancelada: 'La canceló',
  Reasignada: 'La reasignó',
};

export function IncidenciaDetallePage() {
  const { id } = useParams({ strict: false }) as { id: string };
  const query = useIncidencia(id);
  if (query.isLoading) return <DetalleSkeleton />;
  if (query.isError || !query.data)
    return (
      <ErrorState
        title="No se pudo abrir la incidencia"
        problem={esApiError(query.error) && query.error.status === 404 ? query.error.problem : problemaUsuario(query.error)}
        onRetry={() => query.refetch()}
      />
    );
  return <Detalle incidencia={query.data} />;
}

function Detalle({ incidencia: i }: { incidencia: IncidenciaDetalle }) {
  const ruta = rutaInterna(i.rutaReanudacion);
  const pendiente = i.estado === 'Abierta' || i.estado === 'EnAtencion';
  const esResponsable = useAuthStore((s) => s.user?.id) === i.responsableUsuarioId;
  return (
    <div className="flex flex-col gap-5 pt-6">
      <PageHeader
        mono
        titulo={folioIncidencia(i.numero)}
        estado={<EstadoIncidenciaBadge estado={i.estado} />}
        descripcion={`${i.pasoNombre} · ${i.documentoEtiqueta}`}
        acciones={
          <>
            {ruta && pendiente && (
              <Button asChild>
                <Link to={ruta as never}>
                  Retomar en el paso
                  <ArrowRight className="ml-1.5 size-3.5" aria-hidden="true" />
                </Link>
              </Button>
            )}
            <AccionesIncidencia incidencia={i} />
          </>
        }
      />

      <dl className="grid grid-cols-2 gap-x-6 gap-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat @3xl:grid-cols-4">
        <Dato label="Responsable" valor={i.responsableNombre || 'Sin responsable'} />
        <Dato label="Origen" valor={origenIncidencia(i)} />
        <Dato label="Abierta" valor={formatDateTime(i.abiertaEn)} />
        <Dato label="Fecha compromiso" valor={fechaCompromisoTexto(i.fechaCompromiso)} />
        <Dato label="Módulo" valor={nombreModulo(i.modulo)} />
        <Dato label="Documento" valor={i.documentoEtiqueta} />
        <Dato label="Se retoma en" valor={i.pasoReanudacionNombre ?? '—'} />
        {i.codigoMotivo && <Dato label="Código" valor={<span className="font-mono text-xs">{i.codigoMotivo}</span>} />}
      </dl>

      <section className="space-y-2 rounded-lg bg-surface-card p-4 shadow-card-flat" aria-label="Motivo">
        <h2 className="text-md font-semibold text-ink">Motivo</h2>
        <p className="text-sm text-ink">{i.motivo}</p>
        {i.detalle && <p className="whitespace-pre-line text-sm text-ink-secondary">{i.detalle}</p>}
        {i.resolucion && (
          <div className="mt-3 border-t border-line-divider pt-3">
            <h3 className="text-sm font-semibold text-ink">Resolución</h3>
            <p className="text-sm text-ink">{i.resolucion}</p>
            {i.resueltaPorNombre && i.resueltaEn && (
              <p className="text-xs text-ink-muted">
                {i.resueltaPorNombre} · {formatDateTime(i.resueltaEn)}
              </p>
            )}
          </div>
        )}
      </section>

      <DocumentoAdjuntosSection
        base={`/api/v1/incidencias/${i.id}`}
        tipo="incidencia"
        tiposUrl="/api/v1/incidencias/tipos-documento"
        permisos={{ ver: PermisosCanonicos.CompartidoIncidenciasVer, subir: PermisosCanonicos.CompartidoIncidenciasRegistrar }}
        titulo="Evidencia"
        subirComoResponsable={esResponsable}
        soloLectura={!pendiente}
      />

      <section className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat" aria-label="Historial">
        <h2 className="text-md font-semibold text-ink">Historial</h2>
        <ol className="space-y-2.5">
          {[...i.historial].reverse().map((h, n) => (
            <li key={`${h.en}-${n}`} className="flex items-start gap-3">
              <span className="mt-1.5 size-2 shrink-0 rounded-full bg-line-control" aria-hidden="true" />
              <div className="min-w-0 flex-1">
                <p className="text-sm text-ink-strong">
                  {h.usuarioNombre} · {ACCION_HISTORIAL[h.accion] ?? h.accion}
                </p>
                {h.nota && <p className="text-xs text-ink-muted">{h.nota}</p>}
              </div>
              <span className="shrink-0 text-xs text-ink-muted">{formatDateTime(h.en)}</span>
            </li>
          ))}
        </ol>
      </section>
    </div>
  );
}

function Dato({ label, valor }: { label: string; valor: React.ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs text-ink-muted">{label}</dt>
      <dd className="truncate text-sm font-medium text-ink">{valor}</dd>
    </div>
  );
}

/** Lista compacta del master-detail: las mismas incidencias de la bandeja (vista y página actuales). */
export function ListaIncidenciasCompacta({ idActivo, search }: { idActivo: string; search: IncidenciasSearch }) {
  const query = useMisIncidencias({
    estado: estadosDeVista(search.vista),
    modulo: search.modulo,
    ...paginaAOffsetLimit(search),
  });
  return (
    <ListaCompacta
      query={query}
      idActivo={idActivo}
      to="/incidencias/$id"
      search={search}
      getId={(x) => x.id}
      vacio="No tienes incidencias en esta vista."
      renderItem={(x) => ({
        principal: folioIncidencia(x.numero),
        badge: <EstadoIncidenciaBadge estado={x.estado} />,
        secundario: (
          <>
            <span className="truncate">{x.pasoNombre}</span>
            <span className="shrink-0">{fechaCompromisoTexto(x.fechaCompromiso)}</span>
          </>
        ),
      })}
    />
  );
}

