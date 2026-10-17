import type { ReactNode } from 'react';
import { Link } from '@tanstack/react-router';
import { ArrowLeft } from 'lucide-react';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { EstadoAlmacenBadge } from '@/features/almacen/components/EstadoAlmacenBadge';
import { useRecepciones } from '@/features/almacen/api/useRecepciones';
import { useSalidas } from '@/features/almacen/api/useSalidas';
import { useConteos } from '@/features/almacen/api/useConteos';
import { useDevolucionesProveedor } from '@/features/almacen/api/useDevolucionesProveedor';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

/**
 * Master-detail de los detalles de Almacén (patrón P3 de
 * `frontend/docs/patrones-compras.md`): lista compacta de 320 px a la
 * izquierda con el registro activo resaltado + detalle a la derecha. En
 * móvil solo se ve el detalle (drill-down) con un enlace para volver a la
 * bandeja; el aside no se imprime.
 */
interface MasterDetailAlmacenProps {
  /** Nombre plural del recurso: "Recepciones", "Salidas"… */
  titulo: string;
  /** Ruta de la bandeja a la que vuelve el enlace móvil. */
  bandejaTo: string;
  lista: ReactNode;
  children: ReactNode;
}

export function MasterDetailAlmacen({
  titulo,
  bandejaTo,
  lista,
  children,
}: MasterDetailAlmacenProps) {
  return (
    <div className="flex flex-col gap-4 md:h-[calc(100vh-3.5rem-3rem)] md:flex-row">
      <aside
        className="hidden flex-col gap-3 md:flex md:w-80 md:shrink-0 md:overflow-hidden"
        aria-label={`Lista de ${titulo.toLowerCase()}`}
        data-print="hidden"
      >
        <div className="flex items-center justify-between gap-2">
          <Link
            to={bandejaTo as never}
            search={{} as never}
            className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
          >
            <ArrowLeft className="size-3.5" aria-hidden="true" />
            {titulo}
          </Link>
        </div>
        <div className="flex-1 overflow-y-auto rounded-md border bg-card">
          {lista}
        </div>
      </aside>
      <section
        className="min-w-0 flex-1 md:overflow-y-auto"
        aria-label={`Detalle de ${titulo.toLowerCase()}`}
      >
        {children}
      </section>
    </div>
  );
}

interface ListaCompactaProps<T> {
  query: {
    isLoading: boolean;
    isError: boolean;
    error: unknown;
    refetch: () => unknown;
    data?: { items: T[] };
  };
  idActivo: string;
  to: string;
  getId: (item: T) => string;
  vacio: string;
  renderItem: (item: T) => { principal: ReactNode; badge: ReactNode; secundario: ReactNode };
}

function ListaCompacta<T>({
  query,
  idActivo,
  to,
  getId,
  vacio,
  renderItem,
}: ListaCompactaProps<T>) {
  if (query.isLoading) {
    return (
      <div className="p-3">
        <TableSkeleton rows={8} columns={[{ width: 'w-full' }, { width: 'w-full' }]} />
      </div>
    );
  }
  if (query.isError) {
    return (
      <ErrorState
        problem={esApiError(query.error) ? query.error.problem : undefined}
        onRetry={() => query.refetch()}
      />
    );
  }
  const items = query.data?.items ?? [];
  if (items.length === 0) return <EmptyState title={vacio} />;
  return (
    <ul className="divide-y">
      {items.map((item) => {
        const id = getId(item);
        const activo = id === idActivo;
        const { principal, badge, secundario } = renderItem(item);
        return (
          <li key={id}>
            <Link
              to={to as never}
              params={{ id } as never}
              className={cn(
                'block px-3 py-2 transition-colors',
                activo ? 'bg-primary/10 hover:bg-primary/15' : 'hover:bg-muted/40',
              )}
              aria-current={activo ? 'page' : undefined}
            >
              <div className="flex items-start justify-between gap-2">
                <span className={cn('truncate font-mono text-sm', activo && 'font-semibold')}>
                  {principal}
                </span>
                <span className="shrink-0">{badge}</span>
              </div>
              <div className="mt-1 flex items-center justify-between gap-2 text-xs text-muted-foreground">
                {secundario}
              </div>
            </Link>
          </li>
        );
      })}
    </ul>
  );
}

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
