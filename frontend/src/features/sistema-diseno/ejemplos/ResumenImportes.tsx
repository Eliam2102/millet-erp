import { ResumenImportes } from '@/components/erp/display/ResumenImportes';

export default function Ejemplo() {
  return (
    <ResumenImportes
      moneda="MXN"
      subtotal={1000}
      descuento={100}
      ivaTrasladado={144}
      ivaRetenido={0}
      total={1044}
      estadoPago={[{ etiqueta: 'Saldo ficticio', monto: 1044 }]}
    />
  );
}
