import { DateTimeDisplay, EstadoBadge } from '@/components/erp';
import type { OrdenCompraDetalleResponse } from '@/features/compras/ordenes/api/types';

/**
 * <c>&lt;CabeceraOrdenCompra/&gt;</c> — bloque de información de la
 * cabecera de la OC en el tab "Información" del detalle P3. Mismo
 * patrón que <c>&lt;CabeceraRequisicion/&gt;</c>: <c>dl</c> de pares
 * label/valor agrupados, banner para campos especiales (motivo
 * sin RQ, motivo cancelación, motivo rechazo).
 *
 * <para>Read-only en UF1-PR2. La edición inline de la cabecera y de
 * la información logística/importación/financiera llega en UF3-PR1.</para>
 */
export interface CabeceraOrdenCompraProps {
  oc: OrdenCompraDetalleResponse;
  /** Mapa de id → nombre para resolver proveedor, sucursal, almacén,
   * comprador, condiciones de pago, etc. Si el id no se resuelve,
   * fallback al id raw para no esconder data. */
  resolverNombre: (id: string | null | undefined) => string;
}

export function CabeceraOrdenCompra({
  oc,
  resolverNombre,
}: CabeceraOrdenCompraProps) {
  return (
    <section
      className="space-y-4 rounded-md border bg-card p-4"
      data-component="cabecera-orden-compra"
    >
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="font-mono text-2xl font-semibold tracking-tight">
            {oc.folio}
          </h1>
          <p className="text-sm text-muted-foreground">
            Emitida{' '}
            <DateTimeDisplay value={oc.fechaDocumento} variant="long" />
          </p>
        </div>
        <EstadoBadge
          tipo="orden-compra"
          estado={oc.estado}
          className="text-sm"
        />
      </header>

      <dl className="grid grid-cols-1 gap-x-6 gap-y-2 text-sm md:grid-cols-2 lg:grid-cols-3">
        <Item label="Proveedor">
          {oc.proveedorRazonSocial ??
            oc.proveedorClave ??
            resolverNombre(oc.proveedorId)}
        </Item>
        <Item label="Comprador titular">
          {resolverNombre(oc.compradorTitularId)}
        </Item>
        <Item label="Encargado de compras">
          {resolverNombre(oc.encargadoComprasId)}
        </Item>
        <Item label="Sucursal destino">
          {resolverNombre(oc.sucursalDestinoId)}
        </Item>
        <Item label="Condiciones de pago">
          {resolverNombre(oc.condicionesPagoId)}
        </Item>
        <Item label="Uso principal">
          {resolverNombre(oc.usoPrincipalId)}
        </Item>
        <Item label="Moneda" mono>
          {oc.moneda}
          {oc.tipoCambio != null ? ` @ ${oc.tipoCambio}` : ''}
        </Item>
        <Item label="Fecha contabilización">
          <DateTimeDisplay value={oc.fechaContabilizacion} />
        </Item>
        <Item label="Fecha entrega esperada">
          <DateTimeDisplay value={oc.fechaEntregaEsperada} />
        </Item>
        <Item label="Versión" mono>
          v{oc.version}
        </Item>
      </dl>

      {/* Banners de naturalezas especiales — visibles solo cuando aplican. */}
      <div className="flex flex-wrap gap-1.5 text-xs">
        {oc.esImportacion && (
          <Badge tono="azul" data-flag="es-importacion">
            Importación
          </Badge>
        )}
        {oc.sinRequisicionPrevia && (
          <Badge tono="ambar" data-flag="sin-rq-previa">
            Sin RQ previa
          </Badge>
        )}
        {oc.cotizacionExcepcionada && (
          <Badge tono="ambar" data-flag="cotizacion-excepcionada">
            Cotización excepcionada
          </Badge>
        )}
        {oc.ocOrigenId != null && (
          <Badge tono="violeta" data-flag="duplicada">
            Duplicada de OC origen
          </Badge>
        )}
      </div>

      {oc.observaciones && (
        <div className="rounded-md border bg-muted/30 p-3 text-sm">
          <p className="mb-1 text-xs font-medium uppercase tracking-wide text-muted-foreground">
            Observaciones
          </p>
          <p className="whitespace-pre-wrap">{oc.observaciones}</p>
        </div>
      )}

      {oc.motivoSinRequisicion && (
        <div className="rounded-md border border-amber-200 bg-amber-50 p-3 text-sm">
          <p className="mb-1 text-xs font-medium uppercase tracking-wide text-amber-800">
            Motivo sin requisición previa
          </p>
          <p className="whitespace-pre-wrap text-amber-900">
            {oc.motivoSinRequisicion}
          </p>
        </div>
      )}

      {(oc.motivoRechazoTexto != null || oc.motivoRechazoId != null) && (
        <div className="rounded-md border border-rose-200 bg-rose-50 p-3 text-sm">
          <p className="mb-1 text-xs font-medium uppercase tracking-wide text-rose-700">
            Motivo de rechazo
          </p>
          <p className="whitespace-pre-wrap text-rose-900">
            {oc.motivoRechazoTexto ?? `(motivo id: ${oc.motivoRechazoId})`}
          </p>
        </div>
      )}

      {oc.motivoCancelacion && (
        <div className="rounded-md border border-rose-200 bg-rose-50 p-3 text-sm">
          <p className="mb-1 text-xs font-medium uppercase tracking-wide text-rose-700">
            Motivo de cancelación
          </p>
          <p className="whitespace-pre-wrap text-rose-900">
            {oc.motivoCancelacion}
          </p>
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

function Badge({
  tono,
  children,
  ...rest
}: {
  tono: 'azul' | 'ambar' | 'violeta';
  children: React.ReactNode;
} & React.HTMLAttributes<HTMLSpanElement>) {
  const cls =
    tono === 'azul'
      ? 'bg-blue-100 text-blue-900 ring-blue-200'
      : tono === 'ambar'
        ? 'bg-amber-100 text-amber-900 ring-amber-200'
        : 'bg-violet-100 text-violet-900 ring-violet-200';
  return (
    <span
      className={`inline-flex items-center rounded-full px-2 py-0.5 font-medium ring-1 ring-inset ${cls}`}
      {...rest}
    >
      {children}
    </span>
  );
}
