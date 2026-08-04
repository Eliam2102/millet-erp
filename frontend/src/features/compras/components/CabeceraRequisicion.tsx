import {
  EstadoBadge,
  DateTimeDisplay,
} from '@/components/erp';
import {
  clasificacionToString,
  prioridadToString,
  type RequisicionResponse,
} from '@/features/compras/api/types';
import {
  departamentoLabel,
  requisitanteLabel,
} from '@/features/compras/lib/nombres';

/**
 * <c>&lt;CabeceraRequisicion/&gt;</c> — bloque superior del detalle
 * P3 con los datos de cabecera de la RQ. Read-only en UF1-PR2; la
 * edición vive en UF2 (P4 nueva) y futuras (PATCH cabecera).
 */
export interface CabeceraRequisicionProps {
  rq: RequisicionResponse;
  /**
   * Resuelve id → nombre para sucursal y almacén (catálogos chicos que el
   * backend aún no enriquece). Requisitante, departamento y proveedor
   * sugerido ya vienen resueltos en el DTO (ADR-0042 + addendum) y NO usan
   * este resolver (el proveedor cae a él solo como último fallback).
   */
  resolverNombre: (id: string | null | undefined) => string;
}

export function CabeceraRequisicion({
  rq,
  resolverNombre,
}: CabeceraRequisicionProps) {
  return (
    <section className="rounded-md border bg-card p-4">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="font-mono text-2xl font-semibold tracking-tight">
            {rq.folio}
          </h1>
          <p className="text-sm text-muted-foreground">
            Solicitada{' '}
            <DateTimeDisplay value={rq.fechaSolicitud} variant="long" />
          </p>
        </div>
        <EstadoBadge
          tipo="requisicion"
          estado={rq.estado}
          situacion={rq.situacionSurtido}
          className="text-sm"
        />
      </header>

      <dl className="mt-4 grid grid-cols-1 gap-x-6 gap-y-2 text-sm md:grid-cols-2 lg:grid-cols-3">
        <Item label="Requisitante">{requisitanteLabel(rq)}</Item>
        <Item label="Departamento">{departamentoLabel(rq)}</Item>
        <Item label="Sucursal">{resolverNombre(rq.sucursalId)}</Item>
        <Item label="Clasificación">
          {clasificacionToString(rq.clasificacion)}
        </Item>
        <Item label="Prioridad">{prioridadToString(rq.prioridad)}</Item>
        <Item label="Fecha entrega deseada">
          <DateTimeDisplay value={rq.fechaEntregaDeseada} />
        </Item>
        <Item label="Proveedor sugerido">
          {rq.proveedorSugeridoId
            ? (rq.proveedorSugeridoRazonSocial ??
              rq.proveedorSugeridoClave ??
              resolverNombre(rq.proveedorSugeridoId))
            : '—'}
        </Item>
        <Item label="Versión" mono>
          v{rq.version}
        </Item>
      </dl>

      {rq.descripcion && (
        <div className="mt-4 rounded-md border bg-muted/30 p-3 text-sm">
          <p className="mb-1 text-xs font-medium uppercase tracking-wide text-muted-foreground">
            Descripción
          </p>
          <p className="whitespace-pre-wrap">{rq.descripcion}</p>
        </div>
      )}

      {rq.motivoTerminacionTexto != null && (
        <div className="mt-3 rounded-md border border-rose-200 bg-rose-50 p-3 text-sm">
          <p className="mb-1 text-xs font-medium uppercase tracking-wide text-rose-700">
            Motivo de terminación
          </p>
          <p className="whitespace-pre-wrap text-rose-900">
            {rq.motivoTerminacionTexto}
          </p>
          {rq.fechaTerminacion && (
            <p className="mt-1 text-xs text-rose-700">
              <DateTimeDisplay
                value={rq.fechaTerminacion}
                variant="datetime"
              />
            </p>
          )}
        </div>
      )}
    </section>
  );
}

function Item({
  label,
  children,
  mono,
}: {
  label: string;
  children: React.ReactNode;
  mono?: boolean;
}) {
  return (
    <div className="grid min-w-0 grid-cols-[max-content_1fr] gap-x-2">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className={mono ? 'min-w-0 break-words font-mono' : 'min-w-0 break-words'}>
        {children}
      </dd>
    </div>
  );
}
