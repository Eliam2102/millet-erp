import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { useArticulo } from '@/features/catalogos/api/hooks';
import { useTamboresLinea } from '@/features/almacen/api/useTamboresLinea';

/**
 * M-172 (AC-030): línea de producción destino de un artículo que se consume por tambor a línea (silicón, Ködispace).
 * Si el artículo no lleva la marca no se muestra nada. La cantidad son piezas enteras; al registrar, el tambor anterior
 * del mismo artículo en esa línea queda gastado.
 */
export function LineaTamborSelect({
  articuloId,
  value,
  onChange,
  disabled,
  error,
  ariaLabel = 'Línea de producción destino',
}: {
  articuloId: string | null | undefined;
  value: string | null | undefined;
  onChange: (lineaId: string | null) => void;
  disabled?: boolean;
  error?: string;
  ariaLabel?: string;
}) {
  const articulo = useArticulo(articuloId || null);
  const porTambor = articulo.data?.consumoPorTamborALinea === true;
  const lineas = useTamboresLinea({ enabled: porTambor });
  if (!porTambor) return null;
  const opciones = lineas.data ?? [];

  return (
    <div className="space-y-1.5">
      <label className="text-sm font-medium">
        Línea de producción destino <span aria-hidden="true" className="text-danger-fg">*</span>
      </label>
      <Select value={value ?? ''} onValueChange={(v) => onChange(v || null)} disabled={disabled || opciones.length === 0}>
        <SelectTrigger aria-label={ariaLabel} aria-required="true" aria-invalid={error != null}>
          <SelectValue placeholder={lineas.isLoading ? 'Cargando líneas…' : opciones.length === 0 ? 'Sin líneas de producción' : 'Elige la línea'} />
        </SelectTrigger>
        <SelectContent>
          {opciones.map((l) => (
            <SelectItem key={l.id} value={l.id}>
              {l.nombre} · {l.clave}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      {error != null ? (
        <p role="alert" className="text-xs text-danger-fg">
          {error}
        </p>
      ) : (
        <p className="text-2xs text-ink-muted">
          Se surte por tambor o cubeta en piezas enteras; el tambor anterior de esta línea queda gastado.
        </p>
      )}
    </div>
  );
}
