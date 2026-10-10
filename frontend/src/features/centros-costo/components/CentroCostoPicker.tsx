import { useQuery } from '@tanstack/react-query';
import { Input } from '@/components/ui/input';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';
import { apiRequest } from '@/lib/api';
import type { CentroCostoOpcion } from '../api/captura';
import { etiquetaCentroCosto, etiquetaNivelCentroCosto } from '../lib/captura';
export interface CentroCostoPickerProps {
  value: string | null | undefined; onChange: (id: string | null) => void;
  endpoint: string; initialLabel?: string; soloLectura?: boolean; disabled?: boolean;
}
export function CentroCostoPicker({ value, onChange, endpoint, initialLabel, soloLectura, disabled }: CentroCostoPickerProps) {
  const query = useQuery({
    queryKey: ['centros-costo', 'opciones', endpoint], enabled: !soloLectura && !disabled,
    queryFn: async ({ signal }) => (await apiRequest<CentroCostoOpcion[]>(endpoint, { signal })).data,
  });
  if (soloLectura) return <div className="space-y-1.5">
    <Input readOnly aria-label="Centro de costo (heredado del departamento, solo lectura)"
      value={initialLabel ?? 'Sin centro de costo asignado'} className="bg-surface-muted" />
    <p className="text-xs text-ink-muted">Heredado del departamento. Solo lectura; máquina opcional.</p>
  </div>;
  return <CatalogoEagerCombobox items={query.data ?? []} loading={query.isLoading} error={query.error}
    value={value} onChange={onChange} itemToLabel={n => `${etiquetaCentroCosto(n)} ${etiquetaNivelCentroCosto(n.nivel)}`}
    renderTrigger={etiquetaCentroCosto} initialLabel={initialLabel}
    renderItem={n => <div><p>{etiquetaCentroCosto(n)}</p><p className="text-xs text-ink-muted">{etiquetaNivelCentroCosto(n.nivel)}</p></div>}
    ariaLabel="Seleccionar centro de costo" placeholder="Selecciona centro de costo o máquina"
    searchPlaceholder="Buscar centro de costo o máquina…" emptyListText="No hay centros de costo vigentes en tu alcance."
    disabled={disabled} />;
}
