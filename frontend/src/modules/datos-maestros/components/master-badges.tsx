import { Badge } from '@/components/ui/badge';
import {
  EstatusCatalogo,
  OrigenMaster,
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
