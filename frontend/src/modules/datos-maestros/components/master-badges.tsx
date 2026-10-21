import { Badge } from '@/components/ui/badge';
import { EstatusCatalogo, OrigenMaster, type ResultadoSincronizacion } from '../api/types';
import { estadoUi } from '../lib/estados-ui';
export function EstatusCatalogoBadge({ estatus }: { estatus: number }) {
  const ui = estadoUi(
    'catalogo',
    estatus === EstatusCatalogo.Activo
      ? 'Activo'
      : estatus === EstatusCatalogo.EnRevision
        ? 'EnRevision'
        : 'Inactivo',
  );
  return <Badge variant={ui.tono}>{ui.texto}</Badge>;
}
export function OrigenBadge({
  origen,
  className,
  comoTexto = false,
}: {
  origen: OrigenMaster;
  className?: string;
  comoTexto?: boolean;
}) {
  const ui = estadoUi('origen', origen === OrigenMaster.Aw ? 'Aw' : 'Manual');
  if (comoTexto) return <span className={className}>{ui.texto}</span>;
  return (
    <Badge variant={ui.tono} className={className}>
      {ui.texto}
    </Badge>
  );
}
export function FiscalesIncompletosBadge({
  completos,
  className,
}: {
  completos: boolean;
  className?: string;
}) {
  const ui = estadoUi('fiscal', 'Incompleto');
  return completos ? null : (
    <Badge variant={ui.tono} className={className}>
      {ui.texto}
    </Badge>
  );
}
export function ResultadoSincronizacionBadge({
  resultado,
  aplicado = true,
  className,
  comoTexto = false,
}: {
  resultado: ResultadoSincronizacion;
  aplicado?: boolean;
  comoTexto?: boolean;
  className?: string;
}) {
  const ui = estadoUi(
    'sincronizacion',
    !aplicado && (resultado === 'Aplicado' || resultado === 'SinCambios')
      ? 'SinAplicar'
      : resultado,
  );
  if (comoTexto) return <span className={className}>{ui.texto}</span>;
  return (
    <Badge variant={ui.tono} className={className}>
      {ui.texto}
    </Badge>
  );
}
