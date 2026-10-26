import { useMemo } from 'react';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { useSaldosPorUbicacion } from '@/features/almacen/api/useSaldosCierreReportes';
import { textoMedida } from '@/features/almacen/lib/medida-hoja';

/**
 * Medida de hoja de una salida o movimiento (F1-MP-01). Las medidas ofrecidas salen de las filas de existencia del artículo
 * (una por ubicación y medida): si el artículo no tiene existencia por medida, no se muestra nada y la línea sigue como siempre.
 * La cantidad de la línea se captura en hojas (`unidadCapturada` = HOJA) y el servidor la convierte a m².
 */
export function MedidaSalidaSelect({
  articuloId,
  value,
  onChange,
  disabled,
  ariaLabel = 'Medida de la hoja',
}: {
  articuloId: string | null | undefined;
  value: string | null | undefined;
  onChange: (varianteId: string | null, areaHojaM2?: number | null) => void;
  disabled?: boolean;
  ariaLabel?: string;
}) {
  const saldos = useSaldosPorUbicacion(articuloId, null);
  const opciones = useMemo(() => {
    const porMedida = new Map<string, { id: string; ancho?: number | null; alto?: number | null; area?: number | null; m2: number }>();
    for (const s of saldos.data ?? []) {
      if (!s.productoAwVarianteId) continue;
      const previa = porMedida.get(s.productoAwVarianteId);
      porMedida.set(s.productoAwVarianteId, {
        id: s.productoAwVarianteId,
        ancho: s.anchoMm,
        alto: s.altoMm,
        area: s.areaHojaM2,
        m2: (previa?.m2 ?? 0) + s.cantidad,
      });
    }
    return [...porMedida.values()].map((m) => ({
      id: m.id,
      area: m.area,
      texto:
        textoMedida({ productoAwVarianteId: m.id, anchoMm: m.ancho, altoMm: m.alto, areaHojaM2: m.area }, m.m2) ??
        'Medida',
    }));
  }, [saldos.data]);

  if (opciones.length === 0) return null;
  return (
    <div className="space-y-1.5">
      <label className="text-sm font-medium">
        Medida de la hoja <span aria-hidden="true" className="text-danger-fg">*</span>
      </label>
      <Select value={value ?? ''} onValueChange={(v) => onChange(v || null, opciones.find((o) => o.id === v)?.area)} disabled={disabled}>
        <SelectTrigger aria-label={ariaLabel} aria-required="true">
          <SelectValue placeholder="Elige la medida" />
        </SelectTrigger>
        <SelectContent>
          {opciones.map((o) => (
            <SelectItem key={o.id} value={o.id}>
              {o.texto} disponibles
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      {value ? <p className="text-2xs text-ink-muted">La cantidad se captura en hojas.</p> : null}
    </div>
  );
}
