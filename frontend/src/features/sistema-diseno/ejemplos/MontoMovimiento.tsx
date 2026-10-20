import { MontoMovimiento } from '@/components/erp/display/MontoMovimiento';

export default function Ejemplo() {
  return (
    <div className="flex gap-4">
      <MontoMovimiento monto={120} moneda="MXN" tipo="entrada" />
      <MontoMovimiento monto={120} moneda="MXN" tipo="salida" />
    </div>
  );
}
