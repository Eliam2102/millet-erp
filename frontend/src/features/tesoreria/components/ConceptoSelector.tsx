import { useId } from 'react';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Label } from '@/components/ui/label';
import { useConceptos } from '../api/useTesoreria';
import { CLASIFICACION_FLUJO_LABELS } from '../api/types';

export function ConceptoSelector({ value, onChange }: { value: string | null; onChange: (id: string) => void }) {
  const id = useId();
  const query = useConceptos();
  return <div className="space-y-1">
    <Label htmlFor={id}>Concepto de flujo de efectivo</Label>
    <Select value={value ?? ''} onValueChange={onChange} disabled={query.isLoading}>
      <SelectTrigger id={id} className="w-full"><SelectValue placeholder="Selecciona un concepto" /></SelectTrigger>
      <SelectContent>{query.data?.map(c => <SelectItem key={c.id} value={c.id}>
        {CLASIFICACION_FLUJO_LABELS[c.clasificacionFlujo]} · {c.nombre}{c.esEjemplo ? ' (ejemplo por validar)' : ''}
      </SelectItem>)}</SelectContent>
    </Select>
    {query.isError && <p role="alert" className="text-xs text-danger-fg">No se pudieron cargar los conceptos.</p>}
  </div>;
}
