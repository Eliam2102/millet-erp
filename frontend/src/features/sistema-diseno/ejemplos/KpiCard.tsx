import { KpiCard } from '@/components/erp/display/KpiCard';

export default function Ejemplo() {
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      {(['success', 'warning', 'danger', 'info', 'neutral'] as const).map((tono) => (
        <KpiCard
          key={tono}
          etiqueta="Órdenes de compra"
          valor="3"
          tono={tono}
          href="#plantillas"
          prioridad={tono === 'danger' ? 'critico' : tono === 'warning' ? 'atencion' : undefined}
          subdato="Documentos ficticios que requieren revisión"
        />
      ))}
    </div>
  );
}
