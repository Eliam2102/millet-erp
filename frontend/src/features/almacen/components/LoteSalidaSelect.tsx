import { useEffect, useMemo } from 'react';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { useSaldosPorUbicacion } from '@/features/almacen/api/useSaldosCierreReportes';
import { hojasDe } from '@/features/almacen/lib/medida-hoja';

/** Valor del <Select> para la existencia sin lote («apertura»): Radix no admite un valor vacío. */
const SIN_LOTE = '__sin_lote__';

/**
 * Lote de una salida o movimiento de hojas (F1-MP-01 S4b). Ofrece los lotes con existencia del artículo en esa medida (y en ese
 * rack, si ya se eligió), cada uno con sus hojas disponibles; la existencia sin lote se muestra como «Sin lote (apertura)».
 * Solo aparece si la medida tiene existencia con lote: si no, la línea sigue sin lote como siempre.
 */
export function LoteSalidaSelect({
  articuloId,
  subAlmacenId,
  ubicacionId,
  varianteId,
  value,
  onChange,
  disabled,
  ariaLabel = 'Lote',
}: {
  articuloId: string | null | undefined;
  /** Acota la existencia a un sub-almacén (p. ej. MAT-REV en la baja por daño). */
  subAlmacenId?: string | null;
  /** Rack ya elegido; sin él se suman los racks del artículo. */
  ubicacionId?: string | null;
  varianteId: string | null | undefined;
  value: string | null | undefined;
  onChange: (lote: string | null) => void;
  disabled?: boolean;
  ariaLabel?: string;
}) {
  const saldos = useSaldosPorUbicacion(articuloId, subAlmacenId ?? null, varianteId ?? null);
  const opciones = useMemo(() => {
    if (!varianteId) return [];
    const porLote = new Map<string, { lote: string | null; cantidad: number; area?: number | null }>();
    for (const s of saldos.data ?? []) {
      if (s.productoAwVarianteId !== varianteId || (ubicacionId && s.ubicacionId !== ubicacionId)) continue;
      const clave = s.lote ?? SIN_LOTE;
      const previa = porLote.get(clave);
      porLote.set(clave, { lote: s.lote ?? null, cantidad: (previa?.cantidad ?? 0) + s.cantidad, area: s.areaHojaM2 });
    }
    return [...porLote.entries()]
      .map(([clave, o]) => {
        const hojas = hojasDe(o.cantidad, o.area);
        const disponible = hojas == null ? `${o.cantidad} m²` : `${hojas} ${hojas === 1 ? 'hoja' : 'hojas'}`;
        return { clave, lote: o.lote, texto: `${o.lote ?? 'Sin lote (apertura)'} · ${disponible} disponibles` };
      })
      .sort((a, b) => a.clave.localeCompare(b.clave));
  }, [saldos.data, varianteId, ubicacionId]);

  const hayLotes = opciones.some((o) => o.lote != null);
  const claveActual = value ?? SIN_LOTE;
  // Si cambió el rack o la medida y el lote elegido ya no existe, se limpia para no enviar uno ajeno.
  useEffect(() => {
    if (value && saldos.data && !opciones.some((o) => o.clave === value)) onChange(null);
  }, [value, saldos.data, opciones, onChange]);

  if (!hayLotes) return null;
  return (
    <div className="space-y-1.5">
      <label className="text-sm font-medium">
        Lote <span aria-hidden="true" className="text-danger-fg">*</span>
      </label>
      <Select
        value={opciones.some((o) => o.clave === claveActual) ? claveActual : ''}
        onValueChange={(v) => onChange(v === SIN_LOTE ? null : v)}
        disabled={disabled}
      >
        <SelectTrigger aria-label={ariaLabel} aria-required="true">
          <SelectValue placeholder="Elige el lote" />
        </SelectTrigger>
        <SelectContent>
          {opciones.map((o) => (
            <SelectItem key={o.clave} value={o.clave}>
              {o.texto}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}
