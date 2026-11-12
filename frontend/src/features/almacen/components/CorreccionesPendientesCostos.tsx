import { Link } from '@tanstack/react-router';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { formatCantidad } from '@/lib/format-cantidad';
import { useCorreccionesConsumo } from '@/features/almacen/api/useCorreccionesConsumo';

/** M-334: bandeja de Costos con las correcciones de consumo pendientes; cada una abre su salida para autorizar o rechazar. */
export function CorreccionesPendientesCostos() {
  const puedeAutorizar = useHasPermission(PermisosCanonicos.AlmacenCorreccionesConsumoAutorizar);
  const query = useCorreccionesConsumo('PENDIENTE', puedeAutorizar);
  const filas = query.data ?? [];
  if (!puedeAutorizar || filas.length === 0) return null;
  return (
    <section aria-labelledby="correcciones-pendientes" className="space-y-2 rounded-md border border-warning bg-warning-bg p-3">
      <h2 id="correcciones-pendientes" className="text-sm font-semibold">
        Correcciones de consumo por autorizar ({filas.length})
      </h2>
      <ul className="space-y-1 text-sm">
        {filas.map((c) => (
          <li key={c.id} className="flex flex-wrap items-center gap-2">
            <Link to="/almacen/salidas/$id" params={{ id: c.salidaOrigenId }} className="font-mono text-brand underline">
              {c.folioSalida}
            </Link>
            <span>{c.tipoTexto}</span>
            <span className="tabular-nums text-ink-muted">· {formatCantidad(c.cantidad, 4)}</span>
            <span className="text-ink-muted">· {c.motivo}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}
