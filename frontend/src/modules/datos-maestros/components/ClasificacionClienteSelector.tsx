import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { CLASIFICACIONES, useF1AQuery, type ClasificacionCliente } from '../api/f1a';
export function ClasificacionClienteSelector({ value, onChange, disabled }: { value: string | null | undefined; onChange: (value: string) => void; disabled?: boolean }) {
  const query = useF1AQuery<ClasificacionCliente[]>(CLASIFICACIONES);
  return <div className="space-y-1.5">
    <label htmlFor="clasificacion-cliente" className="text-xs font-medium text-ink-strong">Clasificación</label>
    <Select value={value ?? ''} onValueChange={onChange} disabled={disabled || query.isLoading || query.isError}>
      <SelectTrigger id="clasificacion-cliente"><SelectValue placeholder="Elige una clasificación" /></SelectTrigger>
      <SelectContent>{query.data?.filter(c => c.activa).map(c => <SelectItem key={c.id} value={c.id}>{c.nombre}</SelectItem>)}</SelectContent>
    </Select>
    {query.isError && <p role="alert" className="text-xs text-danger-fg">No se pudo cargar el catálogo. Recarga para elegir la clasificación.</p>}
  </div>;
}
