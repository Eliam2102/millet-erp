import { useNavigate } from '@tanstack/react-router';
import { EmitirFacturaForm } from '@/features/facturacion/components/emitir-factura/EmitirFacturaForm';

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
    <div className="mx-auto max-w-5xl space-y-4 px-4 py-6">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight">Emitir factura</h1>
        <p className="text-sm text-muted-foreground">
          Captura directa del CFDI 4.0: emisor, receptor, pago y conceptos.
          Al emitir se sella y timbra.
        </p>
      </header>

      <EmitirFacturaForm
        onSuccess={(res) =>
          navigate({ to: '/facturacion/facturas/$id', params: { id: res.id } })
        }
        onCancel={() => navigate({ to: '/facturacion/facturas' })}
      />
    </div>
  );
}
