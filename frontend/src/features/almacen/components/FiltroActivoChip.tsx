import { X } from 'lucide-react';

/** Chip con × para un filtro activo que llega por la URL (p. ej. desde una OC o RQ). */
export function FiltroActivoChip({
  label,
  onQuitar,
}: {
  label: string;
  onQuitar: () => void;
}) {
  return (
    <span className="inline-flex h-9 items-center gap-1 rounded-md border border-line-row bg-surface-card pl-3 pr-1 text-sm">
      {label}
      <button
        type="button"
        onClick={onQuitar}
        aria-label={`Quitar filtro: ${label}`}
        className="inline-flex size-7 items-center justify-center rounded-sm text-ink-muted hover:bg-muted"
      >
        <X className="size-3.5" aria-hidden="true" />
      </button>
    </span>
  );
}
