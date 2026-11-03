import { Info } from 'lucide-react';

/**
 * 86e3x6cpw: la lista de dominios corporativos sólo protege la creación de
 * cuentas nuevas. Una cuenta que ya existe (directorio o ERP) se liga tal
 * cual; este aviso es informativo, no bloquea.
 */
export function AvisoCuentaFueraDeDominio() {
  return (
    <p className="flex items-start gap-2 text-2xs text-ink-muted">
      <Info className="h-3.5 w-3.5 mt-0.5 shrink-0" />
      <span>Cuenta existente fuera de los dominios corporativos; se ligará tal cual.</span>
    </p>
  );
}
