import { PageHeader } from '@/components/erp/PageHeader';
import { Button } from '@/components/ui/button';

export default function Ejemplo() {
  return (
    <PageHeader
      titulo="Órdenes de compra"
      descripcion="Consulta documentos ficticios y prueba sus acciones."
      acciones={
        <Button disabled title="Selecciona un documento para continuar">
          Autorizar
        </Button>
      }
    />
  );
}
