import { AlertTriangle, Building2 } from 'lucide-react';
import type { EmisorDefaultsResponse } from '@/features/facturacion/api/types';

/**
 * <c>&lt;EmisorInfoBar/&gt;</c> — franja discreta con los datos fiscales
 * del emisor (razón social, RFC y régimen). Son datos de la empresa
 * (Admin → Empresas) y siempre son fijos: los forms de emisión (factura,
 * anticipo, carta porte) los muestran como informativos, sin inputs; el
 * valor que viaja en el command sale de <c>useEmisorDefaults()</c>.
 */
export function EmisorInfoBar({ emisor }: { emisor: EmisorDefaultsResponse }) {
  const incompleto =
    emisor.rfcEmisor.trim() === '' ||
    emisor.regimenFiscalEmisor.trim() === '';
  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1 rounded-md border bg-muted/40 px-3 py-1.5 text-xs text-muted-foreground">
      <span className="flex items-center gap-1.5">
        <Building2 className="size-3.5 shrink-0" aria-hidden />
        <span className="text-[10px] font-medium uppercase tracking-wide">
          Emisor
        </span>
      </span>
      <span className="font-medium text-foreground/80">
        {emisor.razonSocialEmisor}
      </span>
      <span className="font-mono">{emisor.rfcEmisor}</span>
      <span>Régimen {emisor.regimenFiscalEmisor}</span>
      {emisor.codigoPostalEmisor != null && (
        <span>Exp. CP {emisor.codigoPostalEmisor}</span>
      )}
      {incompleto && (
        <span className="flex items-center gap-1 text-amber-600 dark:text-amber-500">
          <AlertTriangle className="size-3.5 shrink-0" aria-hidden />
          Datos del emisor incompletos — captúralos en Admin → Empresas.
        </span>
      )}
    </div>
  );
}
