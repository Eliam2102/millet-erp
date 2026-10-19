import { CabeceraOrdenCompra } from '@/features/compras/ordenes/components/CabeceraOrdenCompra';
import { InformacionLogisticaForm } from '@/features/compras/ordenes/components/InformacionLogisticaForm';
import { InformacionImportacionForm } from '@/features/compras/ordenes/components/InformacionImportacionForm';
import { TotalesFinancierosForm } from '@/features/compras/ordenes/components/TotalesFinancierosForm';
import type { OrdenCompraDetalleResponse } from '@/features/compras/ordenes/api/types';

/**
 * <c>&lt;TabInformacion/&gt;</c> — contenido del Tab "Información" del
 * detalle de OC con secciones apiladas (UX-F1-09; antes sub-tabs anidados):
 *
 * <list>
 *   <item><b>Cabecera</b>: <c>&lt;CabeceraOrdenCompra/&gt;</c> (datos
 *   estructurales — proveedor, sucursal, almacén, condiciones, moneda).</item>
 *   <item><b>Logística</b>: <c>&lt;InformacionLogisticaForm/&gt;</c>
 *   con edición inline (border ámbar). Editable hasta Autorizada.</item>
 *   <item><b>Importación</b>: <c>&lt;InformacionImportacionForm/&gt;</c>
 *   solo visible si <c>oc.esImportacion=true</c>. Editable solo en
 *   Borrador/Rechazada (NumeroPedimento aparte en UF4 post-aut).</item>
 *   <item><b>Financiera</b>: <c>&lt;TotalesFinancierosForm/&gt;</c>
 *   con edición inline. Read mode muestra subtotal+IVA+descuento+gastos+
 *   redondeo+total a pagar. Edit mode permite ajustar los 4 modificadores
 *   de cabecera.</item>
 * </list>
 *
 * <para>El sub-tab "Importación" se oculta si la OC no es de
 * importación; los otros 3 están siempre disponibles.</para>
 */
export interface TabInformacionProps {
  oc: OrdenCompraDetalleResponse;
  resolverNombre: (id: string | null | undefined) => string;
}

export function TabInformacion({ oc, resolverNombre }: TabInformacionProps) {
  // Secciones apiladas (sin tabs anidadas): todo permanece montado, así que
  // lo editado en Logística/Financiera no se pierde.
  return (
    <div className="space-y-6" data-component="tab-informacion">
      <section data-subtab="cabecera" aria-labelledby="info-cabecera">
        <h3 id="info-cabecera" className="mb-2 text-sm font-semibold text-ink">
          Cabecera
        </h3>
        <CabeceraOrdenCompra oc={oc} resolverNombre={resolverNombre} />
      </section>
      <section data-subtab="logistica" aria-labelledby="info-logistica">
        <h3 id="info-logistica" className="mb-2 text-sm font-semibold text-ink">
          Logística
        </h3>
        <InformacionLogisticaForm oc={oc} />
      </section>
      {oc.esImportacion && (
        <section data-subtab="importacion" aria-labelledby="info-importacion">
          <h3 id="info-importacion" className="mb-2 text-sm font-semibold text-ink">
            Importación
          </h3>
          <InformacionImportacionForm oc={oc} />
        </section>
      )}
      <section data-subtab="financiera" aria-labelledby="info-financiera">
        <h3 id="info-financiera" className="mb-2 text-sm font-semibold text-ink">
          Financiera
        </h3>
        <TotalesFinancierosForm oc={oc} />
      </section>
    </div>
  );
}
