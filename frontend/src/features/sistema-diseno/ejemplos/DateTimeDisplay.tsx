import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';

export default function Ejemplo() {
  return (
    <div className="flex flex-wrap gap-4">
      {(['date', 'datetime', 'long', 'corta', 'media'] as const).map((variant) => (
        <DateTimeDisplay key={variant} value="2026-09-21" variant={variant} />
      ))}
      <DateTimeDisplay value="2026-09-21" variant="corta" vencida />
      <DateTimeDisplay value={null} />
    </div>
  );
}
