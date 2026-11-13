import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { PaginacionTabla } from '@/components/erp/data/PaginacionTabla';
import { ErrorState } from '@/components/erp/feedback/ErrorState';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { usePolizasPorDocumento, type DocumentoPolizas } from '../../api/polizas-por-documento';
import { fechaCorta } from '../../lib/dimensiones';
import { EstadoPolizaBadge } from './EstadoPolizaBadge';

export function PolizasDocumento(documento: DocumentoPolizas) {
  const puedeLeer = useHasPermission(PermisosCanonicos.ContabilidadPolizasLeer);
  return (
    <section
      aria-label="Pólizas del documento"
      className="rounded-lg bg-surface-card p-4 shadow-card-flat"
    >
      <h2 className="mb-3 text-md font-semibold">Pólizas contables</h2>
      {puedeLeer ? (
        <Consulta
          key={`${documento.modulo}/${documento.tipo}/${documento.documentoId}`}
          documento={documento}
        />
      ) : (
        <p className="text-sm text-ink-muted">
          No tienes permiso para consultar las pólizas contables.
        </p>
      )}
    </section>
  );
}

function Consulta({ documento }: { documento: DocumentoPolizas }) {
  const [offset, setOffset] = useState(0);
  const query = usePolizasPorDocumento(documento, offset);
  if (query.isPending)
    return (
      <p role="status" className="text-sm text-ink-muted">
        Consultando pólizas…
      </p>
    );
  if (query.isError) {
    if (esApiError(query.error) && query.error.status === 403) {
      return (
        <p role="alert" className="text-sm text-ink-muted">
          No tienes acceso a estas pólizas contables.
        </p>
      );
    }
    return (
      <ErrorState
        title="No se pudieron consultar las pólizas."
        problem={esApiError(query.error) ? query.error.problem : undefined}
        onRetry={() => void query.refetch()}
      />
    );
  }
  const pagina = query.data;
  if (pagina.total === 0)
    return (
      <p className="text-sm text-ink-muted">
        No hay pólizas relacionadas visibles para este documento.
      </p>
    );
  return (
    <>
      <ul className="divide-y divide-line-divider">
        {pagina.items.map((poliza) => (
          <li key={poliza.id} className="flex flex-wrap items-center gap-3 py-2 text-sm">
            <Link
              to="/contabilidad/polizas/$id"
              params={{ id: poliza.id }}
              className="font-mono text-xs text-brand hover:underline"
            >
              {poliza.folio ?? 'Borrador'}
            </Link>
            <time dateTime={poliza.fecha} className="text-xs text-ink-muted">
              {fechaCorta(poliza.fecha)}
            </time>
            <EstadoPolizaBadge
              estado={poliza.estado}
              esAnulacion={poliza.polizaAnuladaId !== null}
              relacion={poliza.relacion}
            />
          </li>
        ))}
      </ul>
      <PaginacionTabla
        total={pagina.total}
        offset={pagina.offset}
        limit={pagina.limit}
        tamanos={[]}
        onChange={(siguiente) => setOffset(siguiente)}
      />
    </>
  );
}
