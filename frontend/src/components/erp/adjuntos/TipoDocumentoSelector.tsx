import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;TipoDocumentoSelector/&gt;</c> — selector genérico de "tipos
 * de documento" para usarse en <c>&lt;AdjuntosManager/&gt;</c>
 * (UF3-PR2). Cross-módulo: el shape mínimo del item es
 * <c>{ id, clave, descripcion }</c> + flag opcional
 * <c>obligatorioSiImportacion</c>.
 *
 * <para>El componente es PARAMETRIZADO: NO carga el catálogo por sí
 * mismo. El caller (en OC: <c>useTiposDocumentoOc()</c>) pasa la lista
 * + estado de carga. CxP/Activos/Recepción harán lo mismo con su propio
 * catálogo del mismo shape.</para>
 *
 * <para>Visual: combobox simple del shadcn (no usa
 * <c>CatalogoEagerCombobox</c> porque el catálogo es chico — 7 items
 * en OC — y no necesita búsqueda).</para>
 */
export interface TipoDocumentoSelectorItem {
  id: string;
  clave: string;
  descripcion: string;
  obligatorioSiImportacion?: boolean;
}

export interface TipoDocumentoSelectorProps<TItem extends TipoDocumentoSelectorItem> {
  items: readonly TItem[];
  loading?: boolean;
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  /** Aria-label accesible. Default "Tipo de documento". */
  ariaLabel?: string;
}

export function TipoDocumentoSelector<TItem extends TipoDocumentoSelectorItem>({
  items,
  loading = false,
  value,
  onChange,
  placeholder = 'Selecciona tipo',
  disabled,
  className,
  ariaLabel = 'Tipo de documento',
}: TipoDocumentoSelectorProps<TItem>) {
  return (
    <Select
      value={value ?? ''}
      onValueChange={(v) => onChange(v === '' ? null : v)}
      disabled={disabled || loading}
    >
      <SelectTrigger
        aria-label={ariaLabel}
        className={cn(className)}
        data-component="tipo-documento-selector"
      >
        <SelectValue placeholder={loading ? 'Cargando…' : placeholder} />
      </SelectTrigger>
      <SelectContent>
        {items.map((item) => (
          <SelectItem key={item.id} value={item.id}>
            <span className="flex items-center gap-2">
              <span className="font-medium">{item.descripcion}</span>
              <span className="text-xs text-muted-foreground">
                ({item.clave})
              </span>
              {item.obligatorioSiImportacion && (
                <span className="text-xs text-amber-600">obligatorio si importación</span>
              )}
            </span>
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
