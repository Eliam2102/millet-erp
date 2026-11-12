import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { useTramos } from '@/features/tramos/api';

export const TRAMO_PT_CAPTURA = 'pt.captura';

/** M-303: mientras `pt.captura` está detenido, las escrituras de PT responden 409 (TRAMO_DETENIDO); se avisa antes de que el usuario capture. */
export function TramoDetenidoBanner() {
  const q = useTramos({ refetchInterval: 60_000 });
  const t = q.data?.find((x) => x.clave === TRAMO_PT_CAPTURA && x.detenido);
  if (!t) return null;
  return (
    <AvisoEnLinea tono="advertencia" titulo="La captura de producto terminado está detenida">
      {t.motivo ?? 'Sin motivo registrado.'}
      {t.detenidoAt && <> Detenida <DateTimeDisplay value={t.detenidoAt} variant="datetime" />.</>}{' '}
      Puedes consultar, pero no registrar cambios hasta que otra persona la reanude en Administración, Tramos.
    </AvisoEnLinea>
  );
}
