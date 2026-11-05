import { useLibroQuery, nombreOperacion, type Operacion } from '../api/libro';
import { useConservacion } from '../api/conservacion';
import type { Activo } from '../api/types';
import { formatDateTime } from '@/lib/datetime';
interface AvisoHistorial {
  id: string;
  fechaAviso: string;
  resolucion: string | null;
  motivoResolucion: string | null;
  resueltoEn: string | null;
  resueltoPor: string | null;
}
export function HistorialActivo({ activo: a }: { activo: Activo }) {
  const libro = useLibroQuery<{
    operaciones: Operacion[];
    ventas?: {
      id: string;
      estado: string;
      motivoRechazo: string | null;
      fechaAutorizacion: string;
      rechazadoEn: string | null;
      autorizadoPor: string;
    }[];
  }>(`activos/${a.id}/libro`);
  const avisos = useConservacion<AvisoHistorial[]>(`activos/${a.id}/avisos`);
  const filas = [
    ...a.historial.map((h) => ({ id: h.id, fecha: h.fecha, texto: h.descripcion })),
    ...a.traslados.map((t) => ({
      id: t.id,
      fecha: t.registradoEn,
      texto: `${t.corrigeA ? 'Corrección de traslado' : 'Traslado'}${t.corregido ? ' · Corregido' : ''}: ${t.motivo} · ${t.origen.ubicacion} → ${t.destino.ubicacion} · ${t.registradoPorNombre ?? t.registradoPor}`,
    })),
    ...(a.impresiones ?? []).map((i) => ({
      id: i.id,
      fecha: i.fecha,
      texto: `Etiqueta: ${i.motivo} · ${i.impresoPor}`,
    })),
    ...(libro.data?.operaciones ?? []).map((o) => ({
      id: o.id,
      fecha: o.fecha,
      texto: `${nombreOperacion[o.tipo] ?? o.tipo}: ${o.motivo} · ${o.motivoRechazo ? `Rechazada: ${o.motivoRechazo}` : o.anulada ? 'Revertida' : o.polizaId ? 'Contabilizada' : 'Pendiente de autorización'}`,
    })),
    ...(libro.data?.ventas ?? []).map((v) => ({
      id: v.id,
      fecha: v.rechazadoEn ?? v.fechaAutorizacion,
      texto: `Venta: ${v.estado} · ${v.motivoRechazo ?? ''} · ${v.autorizadoPor}`,
    })),
    ...(avisos.data ?? []).map((v) => ({
      id: v.id,
      fecha: v.resueltoEn ?? v.fechaAviso,
      texto: `Aviso: ${v.resolucion ?? 'Pendiente'} · ${v.motivoResolucion ?? ''}${v.resueltoPor ? ` · ${v.resueltoPor}` : ''}`,
    })),
  ].sort((x, y) => y.fecha.localeCompare(x.fecha));
  return (
    <section className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <h2 className="text-lg font-semibold">Historial del activo</h2>
      {(libro.error || avisos.error) && (
        <p role="alert">{(libro.error || avisos.error)?.message}</p>
      )}
      {filas.length === 0 && <p>Sin movimientos registrados.</p>}
      {filas.map((f) => (
        <p key={f.id} className="border-b border-line-row py-2">
          {formatDateTime(f.fecha)} · {f.texto}
        </p>
      ))}
    </section>
  );
}
