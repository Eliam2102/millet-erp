import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { TipoCfdi, type NotaCreditoAdjunta } from '@/features/cxp/api/types';
import { CfdiPorProcesarPicker } from './CfdiPorProcesarPicker';
import { useCfdiParseado } from '@/features/cxp/api/useCfdis';
import { lineasParaCompensar } from '@/features/cxp/lib/conciliacion-p3';

interface Props {
  value: NotaCreditoAdjunta[];
  onChange: (notas: NotaCreditoAdjunta[]) => void;
  lineas: { lineaOcId: string | null; descripcion: string }[];
  facturaLigada: boolean;
}

export function NotasCreditoAdjuntas({ value, onChange, lineas, facturaLigada }: Props) {
  const opciones = lineasParaCompensar(lineas);
  return <section className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
    <h3 className="text-sm font-semibold text-ink">Notas de crédito que compensan el precio</h3>
    <p className="text-xs text-ink-muted">Adjunta CFDI de egreso del mismo proveedor con relación 01 a esta factura. Distribuye su base sin impuestos entre las líneas que compensa.</p>
    {value.map((nota, index) => <NotaAdjunta key={nota.cfdiRecibidoId} nota={nota} opciones={opciones} index={index}
      onChange={nueva => onChange(value.map((n, i) => i === index ? nueva : n))}
      onRemove={() => onChange(value.filter((_, i) => i !== index))} />)}
    <CfdiPorProcesarPicker value={null} tipo={TipoCfdi.Egreso} disabled={!facturaLigada || opciones.length === 0}
      placeholder="Adjuntar nota de crédito…" onSelect={cfdi => {
        if (cfdi && !value.some(n => n.cfdiRecibidoId === cfdi.id))
          onChange([...value, { cfdiRecibidoId: cfdi.id, lineas: [] }]);
      }} />
    {(!facturaLigada || opciones.length === 0) && <p className="text-xs text-ink-muted">Liga el CFDI de la factura y selecciona sus líneas de OC para adjuntar una NC.</p>}
  </section>;
}

function NotaAdjunta({ nota, opciones, index, onChange, onRemove }: {
  nota: NotaCreditoAdjunta; opciones: { id: string; etiqueta: string }[]; index: number;
  onChange: (nota: NotaCreditoAdjunta) => void; onRemove: () => void;
}) {
  const xml = useCfdiParseado(nota.cfdiRecibidoId);
  const base = xml.data ? xml.data.subtotal - (xml.data.descuentos ?? 0) : null;
  return <div className="space-y-2 border-t border-line-divider pt-3">
    <div className="flex items-center justify-between gap-2">
      <span className="text-xs text-ink">NC {index + 1} · {xml.isPending ? 'Leyendo XML…' : base === null ? 'No se pudo leer el XML' : `Base ${base.toFixed(2)} ${xml.data?.moneda}`}</span>
      <Button type="button" size="sm" variant="ghost" onClick={onRemove}>Quitar NC {index + 1}</Button>
    </div>
    {opciones.map(opcion => <div key={opcion.id} className="grid grid-cols-2 items-center gap-3">
      <Label htmlFor={`nc-${nota.cfdiRecibidoId}-${opcion.id}`} className="text-xs">{opcion.etiqueta}</Label>
      <Input id={`nc-${nota.cfdiRecibidoId}-${opcion.id}`} type="number" min="0" step="0.01"
        value={nota.lineas.find(l => l.lineaOcId === opcion.id)?.base ?? 0}
        onChange={e => {
          const importe = Number(e.target.value);
          onChange({ ...nota, lineas: [...nota.lineas.filter(l => l.lineaOcId !== opcion.id),
            ...(importe > 0 ? [{ lineaOcId: opcion.id, base: importe }] : [])] });
        }} />
    </div>)}
    <p className="text-xs text-ink-muted">Base asignada: {nota.lineas.reduce((s, l) => s + l.base, 0).toFixed(2)}. El servidor valida importes y relación fiscal antes de aplicar ambas.</p>
  </div>;
}
