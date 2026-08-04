import { History } from 'lucide-react';

/**
 * <c>&lt;HistorialComprasProveedor/&gt;</c> — panel complementario al
 * <c>&lt;ProveedorSelector/&gt;</c> en el Sheet "Nueva OC". Cuando el
 * comprador selecciona un proveedor, este panel debe mostrar las
 * últimas compras a ese proveedor (ayuda a decidir condiciones,
 * detectar precios atípicos, evitar duplicar OC).
 *
 * <para><b>Stub UF2-PR1</b>: el endpoint backend
 * <c>GET /api/v1/compras/ordenes/ultimas-100-compras</c> ya existe
 * (F7-PR3) pero el hook frontend <c>useUltimas100ComprasMaterial</c>
 * llega en UF7-PR3. Por ahora el panel es informativo. Cuando el hook
 * exista, este stub se reemplaza por la tabla real (folio / fecha /
 * cantidad / precio / moneda) sin tocar el call-site del Sheet.</para>
 */
export interface HistorialComprasProveedorProps {
  /** Proveedor seleccionado en el form. <c>null</c> = sin selección. */
  proveedorId: string | null;
}

export function HistorialComprasProveedor({
  proveedorId,
}: HistorialComprasProveedorProps) {
  if (proveedorId == null) return null;

  return (
    <div
      className="rounded-md border border-dashed bg-muted/20 p-3 text-xs text-muted-foreground"
      data-component="historial-compras-proveedor-stub"
    >
      <div className="mb-1 flex items-center gap-1.5 font-medium">
        <History className="h-3.5 w-3.5" aria-hidden="true" />
        Historial de compras a este proveedor
      </div>
      <p>
        Próximamente — UF7-PR3 cableará{' '}
        <code className="font-mono">useUltimas100ComprasMaterial</code> y la
        tabla con folio, fecha, cantidad y precio histórico aparecerá aquí
        para apoyar la decisión de condiciones de compra.
      </p>
    </div>
  );
}
