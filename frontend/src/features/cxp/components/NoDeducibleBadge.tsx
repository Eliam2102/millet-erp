import { Badge } from '@/components/ui/badge';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { useClasificacionesNoDeducibles } from '@/features/cxp/api/useClasificacionesNoDeducibles';

/** Marca «No deducible · <clasificación>» (+ IVA no acreditable) de un gasto (P8.6). Sin clasificación no pinta nada. */
export function NoDeducibleBadge({
  clasificacion,
  ivaNoAcreditable = 0,
  moneda = 'MXN',
}: {
  clasificacion?: string | null;
  ivaNoAcreditable?: number;
  moneda?: string;
}) {
  const { data } = useClasificacionesNoDeducibles();
  if (!clasificacion) return null;
  const nombre = data?.find((c) => c.clave === clasificacion)?.descripcion ?? clasificacion;
  return (
    <span className="inline-flex flex-wrap items-center gap-1">
      <Badge variant="warning">No deducible · {nombre}</Badge>
      {ivaNoAcreditable > 0 && (
        <span className="text-xs text-muted-foreground">
          IVA no acreditable <MoneyDisplay amount={ivaNoAcreditable} currency={moneda} />
        </span>
      )}
    </span>
  );
}
