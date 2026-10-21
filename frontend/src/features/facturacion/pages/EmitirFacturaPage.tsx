import { PageHeader } from '@/components/erp/PageHeader';
import { EmitirFacturaForm } from '@/features/facturacion/components/emitir-factura/EmitirFacturaForm';
import { useNavigate } from '@tanstack/react-router';

/**
 * Página de factura manual — <c>/facturacion/facturas/nueva</c>
 * (FAC-UX-PR2). Reemplaza al Sheet "Emitir factura": el mismo
 * <c>&lt;EmitirFacturaForm/&gt;</c> con pestañas que usa la emisión
 * in-place desde el pedido, aquí sin prefill. Quick Create y el botón
 * "Nueva factura" de la bandeja navegan a esta ruta.
 */
export function EmitirFacturaPage() {
  const navigate = useNavigate();

  return (
    <div className="space-y-5">
      <div>
        <PageHeader
          titulo={<>Emitir factura</>}
          descripcion={
            <>
              Captura directa del CFDI 4.0: emisor, receptor, pago y conceptos. Al emitir se sella y
              timbra.
            </>
          }
        />
      </div>

      <EmitirFacturaForm
        onSuccess={(res) => navigate({ to: '/facturacion/facturas/$id', params: { id: res.id } })}
        onCancel={() => navigate({ to: '/facturacion/facturas' })}
      />
    </div>
  );
}
