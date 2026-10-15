import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { CLASIFICACIONES, useF1AQuery, type ClasificacionCliente } from '../api/f1a';
export function ClasificacionClienteSelector({ value, onChange, disabled }: { value: string | null | undefined; onChange: (value: string) => void; disabled?: boolean }) {
  const query = useF1AQuery<ClasificacionCliente[]>(CLASIFICACIONES);
  const seleccionada = query.data?.find(c => c.id === value);
  return <div className="space-y-1.5">
    <label htmlFor="clasificacion-cliente" className="text-xs font-medium text-ink-strong">Clasificación</label>
    <Select value={value ?? ''} onValueChange={onChange} disabled={disabled || query.isLoading || query.isError}>
      <SelectTrigger id="clasificacion-cliente"><SelectValue placeholder="Elige una clasificación" /></SelectTrigger>
      <SelectContent>{query.data?.filter(c => c.activa || c.id === value).map(c => <SelectItem key={c.id} value={c.id}>{c.nombre}{c.activa ? '' : ' (inactiva)'}</SelectItem>)}</SelectContent>
    </Select>
    {seleccionada && !seleccionada.activa && <p className="text-xs text-ink-muted">Clasificación inactiva: puedes conservarla, pero no asignarla a otros clientes.</p>}
    {query.isError && <p role="alert" className="text-xs text-danger-fg">No se pudo cargar el catálogo. Recarga para elegir la clasificación.</p>}
  </div>;
}
