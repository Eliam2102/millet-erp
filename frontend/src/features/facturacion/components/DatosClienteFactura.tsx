import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { esApiError } from '@/lib/api';
import { useEditarDatosCliente } from '../api/useEditarDatosCliente';
import { DatosClienteCampos } from './DatosClienteCampos';
import type { DatosCliente } from '../lib/datos-cliente';

export function DatosClienteFactura({ factura, puedeEditar }: {
  factura: DatosCliente & { id: string; version: number; estado: string; uuid: string | null };
  puedeEditar: boolean;
}) {
  const editable = puedeEditar && factura.uuid == null && ['Borrador', 'TimbradoFallido', 'PendientePedimento'].includes(factura.estado);
  return editable ? <Editor key={factura.id} factura={factura} />
    : <DatosClienteCampos datos={factura} readOnly />;
}

function Editor({ factura }: { factura: DatosCliente & { id: string; version: number } }) {
  const [datos, setDatos] = useState<DatosCliente>({
    ordenCompraCliente: factura.ordenCompraCliente, referenciaCliente: factura.referenciaCliente,
    condicionesPago: factura.condicionesPago, proforma: factura.proforma, observaciones: factura.observaciones,
  });
  const guardar = useEditarDatosCliente();
  return (
    <form className="space-y-3" onSubmit={(e) => { e.preventDefault(); guardar.mutate({ id: factura.id, version: factura.version, datos }); }}>
      <DatosClienteCampos datos={datos} readOnly={guardar.isPending} onChange={(campo, valor) => setDatos({ ...datos, [campo]: valor })} />
      {guardar.isError && <p role="alert" className="text-sm text-danger-fg">{esApiError(guardar.error) ? guardar.error.problem.detail ?? guardar.error.problem.title : 'No se pudieron guardar los datos; revisa la conexión y vuelve a guardar.'}</p>}
      {guardar.isSuccess && <p role="status" className="text-sm text-success-fg">Datos guardados.</p>}
      <Button type="submit" disabled={guardar.isPending} title={guardar.isPending ? 'Espera a que termine el guardado.' : undefined}>Guardar datos para el cliente</Button>
    </form>
  );
}
