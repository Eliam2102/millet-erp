const colores = [
  'bg-surface-page',
  'bg-surface-card',
  'bg-surface-subtle',
  'bg-surface-muted',
  'bg-surface-selected',
  'bg-brand',
  'bg-brand-hover',
  'bg-rail',
  'bg-success-bg',
  'bg-warning-bg',
  'bg-danger-bg',
  'bg-info-bg',
  'bg-neutral-bg',
];
const textos = [
  'text-ink',
  'text-ink-secondary',
  'text-ink-strong',
  'text-ink-muted',
  'text-ink-subtle',
  'text-brand',
  'text-success-fg',
  'text-warning-fg',
  'text-danger-fg',
  'text-info-fg',
  'text-neutral-fg',
];
const bordes = ['border-line', 'border-line-divider', 'border-line-row', 'border-line-control'];
const tamanos = [
  'text-2xs',
  'text-xs',
  'text-sm',
  'text-md',
  'text-lg',
  'text-xl',
  'text-2xl',
  'text-3xl',
  'text-4xl',
];
const espacios = [
  'p-0.5',
  'p-1',
  'p-1.5',
  'p-2',
  'p-2.5',
  'p-3',
  'p-3.5',
  'p-4',
  'p-5',
  'p-6',
  'p-7',
  'p-8',
];
const radios = [
  'rounded-xs',
  'rounded-sm',
  'rounded-md',
  'rounded-lg',
  'rounded-xl',
  'rounded-2xl',
  'rounded-full',
];
const sombras = ['shadow-card', 'shadow-card-flat', 'shadow-dialog', 'shadow-sheet'];
export function Fundamentos() {
  return (
    <div className="space-y-5">
      <h3 className="text-md font-semibold">Color · §2.1</h3>
      <div className="grid gap-3 sm:grid-cols-3">
        {colores.map((c) => (
          <div key={c}>
            <div className={`${c} h-10 rounded-md shadow-card-flat`} />
            <code className="text-xs">{c}</code>
          </div>
        ))}
        {textos.map((c) => (
          <p key={c} className={c}>
            {c}
          </p>
        ))}
        {bordes.map((c) => (
          <p key={c} className={`${c} rounded-md border p-3`}>
            {c}
          </p>
        ))}
      </div>
      <h3 className="text-md font-semibold">Tipografía · §2.2</h3>
      <p>Plex Sans en UI; Plex Mono en folios. Base 13 px. Pesos 400, 500 y 600.</p>
      {tamanos.map((c) => (
        <p key={c} className={c}>
          {c} · Texto de ejemplo
        </p>
      ))}
      <p className="font-mono">[FOLIO-001] · font-mono</p>
      <h3 className="text-md font-semibold">Espaciado · §2.3</h3>
      <div className="flex flex-wrap items-start gap-3">
        {espacios.map((c) => (
          <div key={c} className={`${c} bg-surface-selected`}>
            <code className="bg-surface-card text-xs">{c}</code>
          </div>
        ))}
      </div>
      <h3 className="text-md font-semibold">Radios y sombras · §2.3</h3>
      <div className="flex flex-wrap gap-4">
        {[...radios, ...sombras].map((c) => (
          <div key={c} className={`${c} bg-surface-subtle p-4`}>
            <code>{c}</code>
          </div>
        ))}
      </div>
    </div>
  );
}
