import { formatDate } from '@/lib/datetime';
import { useReaperturasPeriodo, type ReaperturaPeriodo } from '../../api/periodos';
import { SELECT_CLASS } from '../../lib/estilos';

/** Etiqueta legible de una reapertura: cuándo se abrió, hasta cuándo y su evidencia. */
export function etiquetaReapertura(r: ReaperturaPeriodo): string {
  const estado = r.vigente ? `vence ${formatDate(r.venceEn)}` : 'concluida';
  return `${formatDate(r.creadaEn)} · ${estado} · ${r.evidenciaReferencia}`;
}

/** H-U29: lista desplegable de las reaperturas del periodo de la incidencia (sustituye a pegar el identificador). */
export function ReaperturaSelector({
  id,
  anio,
  numero,
  value,
  onChange,
}: {
  id?: string;
  anio: number;
  numero: number;
  value: string | null;
  onChange: (id: string | null) => void;
}) {
  const query = useReaperturasPeriodo(anio, numero);
  const items = query.data ?? [];
  const vacio = !query.isLoading && !query.isError && items.length === 0;

  return (
    <>
      <select
        id={id}
        className={`${SELECT_CLASS} w-full`}
        value={value ?? ''}
        disabled={query.isLoading || vacio}
        onChange={(e) => onChange(e.target.value || null)}
      >
        <option value="">
          {query.isLoading ? 'Cargando reaperturas…' : vacio ? 'El periodo no tiene reaperturas' : 'Elige la reapertura…'}
        </option>
        {items.map((r) => (
          <option key={r.id} value={r.id}>
            {etiquetaReapertura(r)}
          </option>
        ))}
      </select>
      {query.isError && (
        <p role="alert" className="mt-1 text-xs text-danger-fg">
          No se pudieron cargar las reaperturas del periodo.
        </p>
      )}
    </>
  );
}
