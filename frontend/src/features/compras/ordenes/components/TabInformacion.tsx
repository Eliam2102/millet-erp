import { useMemo, useState } from 'react';
import { CabeceraOrdenCompra } from '@/features/compras/ordenes/components/CabeceraOrdenCompra';
import { InformacionLogisticaForm } from '@/features/compras/ordenes/components/InformacionLogisticaForm';
import { InformacionImportacionForm } from '@/features/compras/ordenes/components/InformacionImportacionForm';
import { TotalesFinancierosForm } from '@/features/compras/ordenes/components/TotalesFinancierosForm';
import type { OrdenCompraDetalleResponse } from '@/features/compras/ordenes/api/types';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;TabInformacion/&gt;</c> — contenido del Tab "Información" del
 * detalle de OC con sub-tabs anidados (UF3-PR1):
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

type SubTabId = 'cabecera' | 'logistica' | 'importacion' | 'financiera';

const SUB_TAB_LABEL: Record<SubTabId, string> = {
  cabecera: 'Cabecera',
  logistica: 'Logística',
  importacion: 'Importación',
  financiera: 'Financiera',
};

export function TabInformacion({ oc, resolverNombre }: TabInformacionProps) {
  const [activo, setActivo] = useState<SubTabId>('cabecera');

  const subTabs = useMemo<SubTabId[]>(
    () =>
      oc.esImportacion
        ? ['cabecera', 'logistica', 'importacion', 'financiera']
        : ['cabecera', 'logistica', 'financiera'],
    [oc.esImportacion],
  );

  // Si el sub-tab activo era "importacion" y oc.esImportacion cambia a
  // false (poco común post-creación pero defensivo), reset a cabecera.
  // Pattern derived state (setState durante el render — React permite
  // si el targetKey cambió desde la última observación).
  const [trackedEsImport, setTrackedEsImport] = useState(oc.esImportacion);
  if (oc.esImportacion !== trackedEsImport) {
    setTrackedEsImport(oc.esImportacion);
    if (!subTabs.includes(activo)) {
      setActivo('cabecera');
    }
  }

  return (
    <div className="space-y-3" data-component="tab-informacion">
      <nav
        role="tablist"
        aria-label="Sub-secciones de información"
        className="flex flex-wrap gap-1 border-b"
      >
        {subTabs.map((id) => (
          <button
            key={id}
            type="button"
            role="tab"
            aria-selected={activo === id}
            id={`subtab-${id}`}
            aria-controls={`subtab-panel-${id}`}
            onClick={() => setActivo(id)}
            data-subtab={id}
            data-activo={activo === id || undefined}
            className={cn(
              '-mb-px border-b-2 px-3 py-1.5 text-sm font-medium transition-colors',
              activo === id
                ? 'border-primary text-primary'
                : 'border-transparent text-muted-foreground hover:text-foreground',
            )}
          >
            {SUB_TAB_LABEL[id]}
          </button>
        ))}
      </nav>

      <div
        role="tabpanel"
        id={`subtab-panel-${activo}`}
        aria-labelledby={`subtab-${activo}`}
      >
        {activo === 'cabecera' && (
          <CabeceraOrdenCompra oc={oc} resolverNombre={resolverNombre} />
        )}
        {activo === 'logistica' && <InformacionLogisticaForm oc={oc} />}
        {activo === 'importacion' && <InformacionImportacionForm oc={oc} />}
        {activo === 'financiera' && <TotalesFinancierosForm oc={oc} />}
      </div>
    </div>
  );
}
