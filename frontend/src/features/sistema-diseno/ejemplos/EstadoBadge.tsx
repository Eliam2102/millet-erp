import { EstadoBadge } from '@/components/erp/display/EstadoBadge';
import { EstadoOrdenCompra } from '@/features/compras/ordenes/api/types';

export default function Ejemplo() {
  return (
    <div className="flex flex-wrap gap-2">
      {Object.values(EstadoOrdenCompra)
        .filter((e): e is EstadoOrdenCompra => typeof e === 'number')
        .map((estado) => (
          <EstadoBadge key={estado} tipo="orden-compra" estado={estado} />
        ))}
    </div>
  );
}
