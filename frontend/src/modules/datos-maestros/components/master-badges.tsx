import { Badge } from '@/components/ui/badge';
import {
  EstatusCatalogo,
  OrigenMaster,
  type ResultadoSincronizacion,
} from '@/modules/datos-maestros/api/types';
import { cn } from '@/lib/utils';

/**
 * Badges compartidos de los masters auto-provisionables (Clientes y
 * Productos A+W, ADR-0048): estatus del catálogo, origen del registro
 * (A+W vs Manual) y el chip ámbar "Fiscales incompletos" que marca los
 * registros que aún no pueden timbrar.
 */

export function EstatusCatalogoBadge({ estatus }: { estatus: number }) {
  if (estatus === EstatusCatalogo.Activo) {
    return <Badge variant="secondary">Activo</Badge>;
  }
  if (estatus === EstatusCatalogo.EnRevision) {
    return <Badge variant="outline">En revisión</Badge>;
  }
  return (
    <Badge variant="outline" className="text-muted-foreground">
      Inactivo
    </Badge>
  );
}

export function OrigenBadge({
  origen,
  className,
}: {
  origen: OrigenMaster;
  className?: string;
}) {
  if (origen === OrigenMaster.Aw) {
    return (
      <Badge
        variant="outline"
        className={cn(
          'border-sky-300 text-sky-700 dark:text-sky-300',
          className,
        )}
      >
        A+W
      </Badge>
    );
  }
  return (
    <Badge variant="outline" className={className}>
      Manual
    </Badge>
  );
}

/**
 * Chip ámbar visible solo cuando <c>datosFiscalesCompletos === false</c>.
 * Es el indicador de la bandeja de trabajo pre-timbrado.
 */
export function FiscalesIncompletosBadge({
  completos,
  className,
}: {
  completos: boolean;
  className?: string;
}) {
  if (completos) return null;
  return (
    <Badge
      variant="outline"
      className={cn(
        'border-amber-300 bg-amber-500/10 text-amber-700 dark:text-amber-300',
        className,
      )}
    >
      Fiscales incompletos
    </Badge>
  );
}

/**
 * Resultado de la última lectura A+W. "Sincronizado" solo para
 * Aplicado/SinCambios y, si se informa, con una aplicación real
 * (<c>aplicado</c>); Pendiente/Conflicto/Error NUNCA se muestran como
 * sincronizados.
 */
export function ResultadoSincronizacionBadge({
  resultado,
  aplicado = true,
  className,
}: {
  resultado: ResultadoSincronizacion;
  aplicado?: boolean;
  className?: string;
}) {
  if (resultado === 'Aplicado' || resultado === 'SinCambios') {
    if (!aplicado) {
      return (
        <Badge variant="outline" className={className}>
          Sin aplicar
        </Badge>
      );
    }
    return (
      <Badge variant="secondary" className={className}>
        Sincronizado
      </Badge>
    );
  }
  const [texto, tono] =
    resultado === 'Pendiente'
      ? ['Pendiente de validación', 'border-amber-300 bg-amber-500/10 text-amber-700 dark:text-amber-300']
      : resultado === 'Conflicto'
        ? ['Conflicto', 'border-orange-300 bg-orange-500/10 text-orange-700 dark:text-orange-300']
        : ['Error de sincronización', 'border-destructive/40 bg-destructive/10 text-destructive'];
  return (
    <Badge variant="outline" className={cn(tono, className)}>
      {texto}
    </Badge>
  );
}
