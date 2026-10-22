import { useClasesActivo } from '../api/hooks';
export function LineaActivoFields({
  error,
  activo,
  clase,
  onChange,
}: {
  error?: string;
  activo: boolean;
  clase?: string | null;
  onChange: (activo: boolean, clase: string | null) => void;
}) {
  const clases = useClasesActivo(activo);
  return (
    <div className="flex flex-wrap items-center gap-3">
      <label className="flex items-center gap-2 text-xs">
        <input
          type="checkbox"
          checked={activo}
          onChange={(e) => onChange(e.target.checked, e.target.checked ? (clase ?? null) : null)}
        />
        Activo fijo
      </label>
      {activo && (
        <label className="text-xs">
          Clase sugerida *
          <select
            required
            aria-label="Clase de activo sugerida"
            className="ml-2 h-ctl-xl rounded-md border border-line-control bg-surface-card px-3"
            value={clase ?? ''}
            onChange={(e) => onChange(true, e.target.value || null)}
          >
            <option value="">Seleccione clase</option>
            {clases.data
              ?.filter((c) => c.activa)
              .map((c) => (
                <option key={c.id} value={c.id}>
                  {c.nombre}
                </option>
              ))}
          </select>
        </label>
      )}
      {error && (
        <p role="alert" className="text-xs text-danger-fg">
          {error}
        </p>
      )}
      {clases.error && (
        <p role="alert" className="text-xs text-danger-fg">
          {clases.error.message}
        </p>
      )}
      {activo && (
        <p className="text-xs text-ink-muted">
          Capture unidades enteras. Se recibe sin generar existencias.
        </p>
      )}
    </div>
  );
}
