import type { ReactNode } from 'react';
import { CircleCheck, TriangleAlert } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Label } from '@/components/ui/label';
import { cn } from '@/lib/utils';
import { SELECT_CLASS } from '../../lib/estilos';
import { etiquetaPeriodo } from '../../lib/polizas';
import { clavePeriodo, leerPeriodo, type PeriodoElegido } from '../../lib/periodos-reporte';

/** Selector de periodo contable (`2026 · 03 Marzo`) con su label asociado. */
export function PeriodoSelect({
  id,
  etiqueta,
  value,
  onChange,
  periodos,
  anio,
}: {
  id: string;
  etiqueta: string;
  value: PeriodoElegido | null;
  onChange: (p: PeriodoElegido | null) => void;
  periodos: readonly { anio: number; numero: number }[];
  /** Si se indica, solo los periodos de ese ejercicio (el rango del mayor no cruza ejercicios). */
  anio?: number;
}) {
  const opciones = anio ? periodos.filter((p) => p.anio === anio) : periodos;
  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={id}>{etiqueta}</Label>
      <select
        id={id}
        className={cn(SELECT_CLASS, 'w-52')}
        value={clavePeriodo(value)}
        onChange={(e) => onChange(leerPeriodo(e.target.value))}
      >
        {opciones.length === 0 && <option value="">Sin periodos</option>}
        {opciones.map((p) => (
          <option key={`${p.anio}-${p.numero}`} value={`${p.anio}-${p.numero}`}>
            {etiquetaPeriodo(p.anio, p.numero)}
          </option>
        ))}
      </select>
    </div>
  );
}

/** Tarjeta de cuadre: badge Cuadra / No cuadra con texto y los importes clave (DS §4.6, §4.11). */
export function TarjetaCuadreContable({
  idTitulo,
  titulo,
  subtitulo,
  cuadra,
  datos,
  mensajeOk,
  mensajeError,
}: {
  idTitulo: string;
  titulo: string;
  subtitulo?: string;
  cuadra: boolean;
  datos: { etiqueta: string; valor: string; alerta?: boolean }[];
  mensajeOk: string;
  mensajeError: ReactNode;
}) {
  return (
    <section
      aria-labelledby={idTitulo}
      className={cn(
        'flex flex-col gap-4 rounded-lg bg-surface-card px-5 py-4 shadow-card',
        !cuadra && 'ring-1 ring-danger-ring',
      )}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 id={idTitulo} className="text-lg font-semibold text-ink">
            {titulo}
          </h2>
          {subtitulo && <p className="text-xs text-ink-muted">{subtitulo}</p>}
        </div>
        {cuadra ? <Badge variant="success">Cuadra</Badge> : <Badge variant="danger">No cuadra</Badge>}
      </div>
      <dl className="grid grid-cols-2 gap-x-6 gap-y-3 sm:grid-cols-3 lg:grid-cols-6">
        {datos.map((d) => (
          <div key={d.etiqueta} className="flex flex-col gap-0.5">
            <dt className="text-xs text-ink-muted">{d.etiqueta}</dt>
            <dd
              className={cn(
                'text-md font-semibold tabular-nums',
                d.alerta ? 'text-danger-fg' : 'text-ink',
              )}
            >
              {d.valor}
            </dd>
          </div>
        ))}
      </dl>
      {cuadra ? (
        <p className="flex items-center gap-2 text-xs text-ink-muted">
          <CircleCheck className="size-4 text-success-fg" strokeWidth={1.6} aria-hidden="true" />
          {mensajeOk}
        </p>
      ) : (
        <p
          role="note"
          className="flex items-start gap-2 rounded-md bg-warning-note-bg px-3 py-2.5 text-sm text-warning-note-fg"
        >
          <TriangleAlert className="mt-0.5 size-4 shrink-0" strokeWidth={1.6} aria-hidden="true" />
          <span>{mensajeError}</span>
        </p>
      )}
    </section>
  );
}
