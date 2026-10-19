import { X } from 'lucide-react';

/**
 * Chip de filtro activo con × (DS 4.4): para un filtro que llega por la URL
 * (p. ej. desde una OC o RQ) o que el usuario aplicó y debe poder quitar.
 */
export function FiltroActivoChip({
  label,
  onQuitar,
}: {
  label: string;
  onQuitar: () => void;
}) {
  return (
    <span className="inline-flex h-ctl-md items-center gap-1 rounded-[7px] border border-brand-soft-border bg-brand-soft pl-2.5 pr-1 text-xs text-brand-hover">
      {label}
      <button
        type="button"
        onClick={onQuitar}
        aria-label={`Quitar filtro: ${label}`}
        className="inline-flex size-5 items-center justify-center rounded-sm hover:bg-brand-soft-border"
      >
        <X className="size-3.5" aria-hidden="true" />
      </button>
    </span>
  );
}
