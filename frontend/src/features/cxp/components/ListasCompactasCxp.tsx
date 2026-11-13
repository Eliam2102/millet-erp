import { usePaginacionRuta } from '@/components/erp/data/usePaginacionRuta';
import { ListaCompacta } from '@/components/erp/MasterDetail';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import {} from '@/components/erp/data/paginacion';
import { useFacturas } from '@/features/cxp/api/useFacturas';
import { useNotasCargo, useNotasCredito } from '@/features/cxp/api/useNotasYAnticipos';
import { useViaticos } from '@/features/cxp/api/useViaticos';
import { useComprobaciones } from '@/features/cxp/api/useComprobaciones';
import {
  EstadoComprobacionChip,
  EstadoNotaCargoChip,
  EstadoNotaCreditoChip,
  EstadoSolicitudViaticosChip,
} from '@/features/cxp/components/EstadoChips';
import { folioFactura } from '@/features/cxp/lib/formato';
import { EstadoPasivoChip } from '@/features/cxp/components/EstadoPasivoChip';
import { TipoComprobacionGastosLabels } from '@/features/cxp/api/types';
import type { FacturasSearch } from '@/features/cxp/lib/facturas-search-schema';
import type { ComprobacionesSearch } from '@/features/cxp/lib/comprobaciones-search-schema';
import type { ViaticosSearch } from '@/features/cxp/lib/viaticos-search-schema';
import type {
  NotasCargoSearch,
  NotasCreditoSearch,
} from '@/features/cxp/lib/notas-y-anticipos-search-schema';

/**
 * Listas compactas del master-detail de CxP (P3). Reciben el search de la
 * bandeja: usan los mismos filtros y la misma página, y lo arrastran a cada
 * enlace para que «Volver» y la lista respeten lo que el usuario filtró.
 */

function abreviar(uuid: string): string {
  return uuid.length > 12 ? `${uuid.slice(0, 8)}…${uuid.slice(-4)}` : uuid;
}

interface ListaProps<S> {
  idActivo: string;
  search: S;
}

export function ListaFacturasCompacta({ idActivo, search }: ListaProps<FacturasSearch>) {
  const paginaTabla = usePaginacionRuta(search);
  const query = useFacturas({
    estado: search.estado,
    proveedorId: search.proveedorId,
    sucursalId: search.sucursalId,
    q: search.q,
    ...paginaTabla,
  });
  return (
    <ListaCompacta
      query={query}
      idActivo={idActivo}
      to="/cxp/facturas/$id"
      search={search}
      getId={(f) => f.id}
      vacio="No hay facturas."
      renderItem={(f) => ({
        principal: folioFactura(f),
        badge: <EstadoPasivoChip estado={f.estado} />,
        secundario: (
          <>
            <span className="truncate">
              {f.proveedorNombre?.trim() || <span className="text-ink-muted">Sin proveedor</span>}
            </span>
            <MoneyDisplay amount={f.total} currency={f.moneda} />
          </>
        ),
      })}
    />
  );
}

export function ListaNotasCargoCompacta({ idActivo, search }: ListaProps<NotasCargoSearch>) {
  const paginaTabla = usePaginacionRuta(search);
  const query = useNotasCargo({
    estado: search.estado,
    proveedorId: search.proveedorId,
    q: search.q,
    ...paginaTabla,
  });
  return (
    <ListaCompacta
      query={query}
      idActivo={idActivo}
      to="/cxp/notas-cargo/$id"
      search={search}
      getId={(n) => n.id}
      vacio="No hay notas de cargo."
      renderItem={(n) => ({
        principal: n.folio,
        badge: <EstadoNotaCargoChip estado={n.estado} />,
        secundario: (
          <>
            <span className="truncate">
              {n.proveedorNombre?.trim() || <span className="text-ink-muted">Sin proveedor</span>}
            </span>
            <MoneyDisplay amount={n.monto} currency={n.moneda} />
          </>
        ),
      })}
    />
  );
}

export function ListaNotasCreditoCompacta({ idActivo, search }: ListaProps<NotasCreditoSearch>) {
  const paginaTabla = usePaginacionRuta(search);
  const query = useNotasCredito({
    estado: search.estado,
    proveedorId: search.proveedorId,
    q: search.q,
    ...paginaTabla,
  });
  return (
    <ListaCompacta
      query={query}
      idActivo={idActivo}
      to="/cxp/notas-credito/$id"
      search={search}
      getId={(n) => n.id}
      vacio="No hay notas de crédito."
      renderItem={(n) => ({
        principal: n.serieProveedor
          ? `${n.serieProveedor}-${n.folioProveedor ?? ''}`
          : (n.folioProveedor ?? abreviar(n.uuidCfdi)),
        badge: <EstadoNotaCreditoChip estado={n.estado} />,
        secundario: (
          <>
            <span className="truncate">
              {n.proveedorNombre?.trim() || <span className="text-ink-muted">Sin proveedor</span>}
            </span>
            <MoneyDisplay amount={n.total} currency={n.moneda} />
          </>
        ),
      })}
    />
  );
}

export function ListaViaticosCompacta({ idActivo, search }: ListaProps<ViaticosSearch>) {
  const paginaTabla = usePaginacionRuta(search);
  const query = useViaticos({
    estado: search.estado,
    empleadoId: search.empleadoId,
    jefeDirectoId: search.jefeDirectoId,
    q: search.q,
    ...paginaTabla,
  });
  return (
    <ListaCompacta
      query={query}
      idActivo={idActivo}
      to="/cxp/viaticos/$id"
      search={search}
      getId={(s) => s.id}
      vacio="No hay solicitudes de viáticos."
      renderItem={(s) => ({
        principal: s.empleadoNombre?.trim() || s.destino,
        badge: <EstadoSolicitudViaticosChip estado={s.estado} />,
        secundario: (
          <>
            <span className="truncate">{s.destino}</span>
            <MoneyDisplay amount={s.montoSolicitado} currency={s.moneda ?? 'MXN'} />
          </>
        ),
      })}
    />
  );
}

export function ListaComprobacionesCompacta({
  idActivo,
  search,
}: ListaProps<ComprobacionesSearch>) {
  const paginaTabla = usePaginacionRuta(search);
  const query = useComprobaciones({
    tipo: search.tipo,
    estado: search.estado,
    sucursalId: search.sucursalId,
    q: search.q,
    ...paginaTabla,
  });
  return (
    <ListaCompacta
      query={query}
      idActivo={idActivo}
      to="/cxp/comprobaciones/$id"
      search={search}
      getId={(c) => c.id}
      vacio="No hay comprobaciones."
      renderItem={(c) => ({
        principal: c.responsableNombre?.trim() || TipoComprobacionGastosLabels[c.tipo],
        badge: <EstadoComprobacionChip estado={c.estado} />,
        secundario: (
          <>
            <span className="truncate">{TipoComprobacionGastosLabels[c.tipo]}</span>
            <MoneyDisplay amount={c.montoTotal} currency={c.moneda} />
          </>
        ),
      })}
    />
  );
}
