import { MasterDetail, ListaCompacta } from '@/components/erp/MasterDetail';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { EstadoAlmacenBadge } from '@/features/almacen/components/EstadoAlmacenBadge';
import { useRecepciones } from '@/features/almacen/api/useRecepciones';
import { useSalidas } from '@/features/almacen/api/useSalidas';
import { useConteos } from '@/features/almacen/api/useConteos';
import { useDevolucionesProveedor } from '@/features/almacen/api/useDevolucionesProveedor';

/** Master-detail de Almacén: alias del componente compartido `components/erp/MasterDetail`. */
export const MasterDetailAlmacen = MasterDetail;

const LIMITE = { limit: 50 } as const;

export function ListaRecepcionesCompacta({ idActivo }: { idActivo: string }) {
  const query = useRecepciones(LIMITE);
  return (
    <ListaCompacta
      query={query}
      idActivo={idActivo}
      to="/almacen/recepciones/$id"
      getId={(r) => r.id}
      vacio="No hay recepciones."
      renderItem={(r) => ({
        principal: r.folio,
        badge: <EstadoAlmacenBadge tipo="movimiento" estado={r.estado} />,
        secundario: (
          <>
            <span className="truncate">{r.ordenCompraFolio ?? 'Sin orden de compra'}</span>
            <MoneyDisplay amount={r.montoTotalMxn} currency="MXN" />
          </>
        ),
      })}
    />
  );
}

export function ListaSalidasCompacta({ idActivo }: { idActivo: string }) {
  const query = useSalidas(LIMITE);
  return (
    <ListaCompacta
      query={query}
      idActivo={idActivo}
      to="/almacen/salidas/$id"
      getId={(s) => s.id}
      vacio="No hay salidas."
      renderItem={(s) => ({
        principal: s.folio,
        badge: <EstadoAlmacenBadge tipo="movimiento" estado={s.estado} />,
        secundario: (
          <>
            <span className="truncate">{s.esPorVale ? 'Por vale' : 'Salida normal'}</span>
            <MoneyDisplay amount={s.montoTotalMxn} currency="MXN" />
          </>
        ),
      })}
    />
  );
}

export function ListaConteosCompacta({ idActivo }: { idActivo: string }) {
  const query = useConteos(LIMITE);
  return (
    <ListaCompacta
      query={query}
      idActivo={idActivo}
      to="/almacen/inventarios/$id"
      getId={(c) => c.id}
      vacio="No hay conteos."
      renderItem={(c) => ({
        principal: <DateTimeDisplay value={c.fechaPlanificada} />,
        badge: <EstadoAlmacenBadge tipo="conteo" estado={c.estado} />,
        secundario: (
          <span>
            {c.cantidadCapturadas}/{c.cantidadLineas} líneas capturadas
          </span>
        ),
      })}
    />
  );
}

export function ListaDevolucionesCompacta({ idActivo }: { idActivo: string }) {
  const query = useDevolucionesProveedor(LIMITE);
  return (
    <ListaCompacta
      query={query}
      idActivo={idActivo}
      to="/almacen/devoluciones/proveedor/$id"
      getId={(d) => d.id}
      vacio="No hay devoluciones a proveedor."
      renderItem={(d) => ({
        principal: d.folioMovimientoSalida ?? d.proveedorNombre ?? `${d.id.slice(0, 8)}…`,
        badge: <EstadoAlmacenBadge tipo="devolucion-proveedor" estado={d.estado} />,
        secundario: (
          <>
            <span className="truncate">{d.proveedorNombre ?? '—'}</span>
            <MoneyDisplay amount={d.montoTotalMxn} currency="MXN" />
          </>
        ),
      })}
    />
  );
}
