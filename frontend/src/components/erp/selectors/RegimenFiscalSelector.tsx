import { useMemo } from 'react';
import { useRegimenesFiscales } from '@/features/catalogos/api';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';

/**
 * <c>&lt;RegimenFiscalSelector/&gt;</c> — selector de regímenes fiscales
 * SAT (F9-PR1). Cross-módulo: emisión CFDI (régimen emisor/receptor),
 * Datos Maestros (clientes), CxP, Contabilidad.
 *
 * <para><b>value/onChange operan sobre el CÓDIGO SAT</b> (<c>'601'</c>,
 * <c>'616'</c>, …), no sobre el id — mismo molde que
 * <c>MonedaSelector</c>/<c>UsoCfdiSelector</c>: todos los consumidores
 * guardan la clave string de 3 dígitos que exige el CFDI. Para encajar
 * con <c>CatalogoEagerCombobox</c> —que keyea por <c>item.id</c>— se
 * adapta cada régimen a <c>id = codigo</c>. El cold value lo resuelve el
 * combobox (muestra el código crudo hasta que carga la lista).</para>
 *
 * <para>Si <c>aplicaPersonaFisica</c> está definido, filtra el catálogo
 * para mostrar solo regímenes compatibles con el tipo de persona del
 * sujeto.</para>
 */
interface RegimenFiscalOpcion {
  /** = código SAT; el combobox keyea por este campo. */
  id: string;
  codigo: string;
  nombre: string;
  aplicaPersonaFisica: boolean;
}

export interface RegimenFiscalSelectorProps {
  /** Código SAT seleccionado (601, 612, 616, …) o null. */
  value: string | null | undefined;
  onChange: (codigo: string | null) => void;
  /** Si <c>true</c>, solo regímenes para persona física; <c>false</c>
   * solo persona moral; <c>undefined</c> muestra todos. */
  aplicaPersonaFisica?: boolean;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function RegimenFiscalSelector({
  value,
  onChange,
  aplicaPersonaFisica,
  placeholder = 'Selecciona régimen fiscal',
  disabled,
  className,
}: RegimenFiscalSelectorProps) {
  const query = useRegimenesFiscales();
  const items = useMemo<RegimenFiscalOpcion[]>(
    () =>
      (query.data ?? [])
        .filter((r) =>
          aplicaPersonaFisica == null
            ? true
            : r.aplicaPersonaFisica === aplicaPersonaFisica,
        )
        .map((r) => ({
          id: r.codigo,
          codigo: r.codigo,
          nombre: r.nombre,
          aplicaPersonaFisica: r.aplicaPersonaFisica,
        })),
    [query.data, aplicaPersonaFisica],
  );

  return (
    <CatalogoEagerCombobox<RegimenFiscalOpcion>
      items={items}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={onChange}
      itemToLabel={(r) => `${r.codigo} ${r.nombre}`}
      renderTrigger={(r) => `${r.codigo} · ${r.nombre}`}
      renderItem={(r) => (
        <div className="min-w-0 flex-1">
          <span className="font-mono text-xs">{r.codigo}</span>
          <p className="truncate text-sm">{r.nombre}</p>
          <p className="text-xs text-muted-foreground">
            {r.aplicaPersonaFisica ? 'Persona física' : 'Persona moral'}
          </p>
        </div>
      )}
      placeholder={placeholder}
      searchPlaceholder="Buscar régimen…"
      emptyListText={
        aplicaPersonaFisica == null
          ? 'No hay regímenes fiscales en el catálogo.'
          : 'Sin regímenes para este tipo de persona.'
      }
      ariaLabel="Seleccionar régimen fiscal"
      disabled={disabled}
      className={className}
    />
  );
}
