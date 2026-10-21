import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { EmptyState } from '@/components/erp/feedback/EmptyState';

export default function Ejemplo() {
  return (
    <ListadoModulo tabs="Todas · 0" filtros="Sin filtros activos" pie="0 de 0">
      <EmptyState
        title="Sin documentos de ejemplo"
        description="Cambia los filtros para consultar otra vista."
      />
    </ListadoModulo>
  );
}
