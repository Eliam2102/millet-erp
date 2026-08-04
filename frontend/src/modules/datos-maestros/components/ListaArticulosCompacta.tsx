import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import {
  EstatusCatalogo,
  Naturaleza,
  type ArticuloItem,
} from '@/modules/datos-maestros/api/types';
import { cn } from '@/lib/utils';

/**
 * Lista compacta master 320px del módulo Datos Maestros → Artículos.
 * Item: clave (mono) + nombre + badge de naturaleza + badge de
 * estatus + ChevronRight. Mismo patrón que
 * <see cref="ListaProveedoresCompacta"/>.
 */
export interface ListaArticulosCompactaProps {
  items: readonly ArticuloItem[];
  idActivo: string | null;
}

export function ListaArticulosCompacta({
  items,
  idActivo,
}: ListaArticulosCompactaProps) {
  return (
    <ul className="divide-y" role="list" aria-label="Lista de artículos">
      {items.map((a) => (
        <ItemCompacto key={a.id} item={a} activo={a.id === idActivo} />
      ))}
    </ul>
  );
}

interface ItemCompactoProps {
  item: ArticuloItem;
  activo: boolean;
}

function ItemCompacto({ item, activo }: ItemCompactoProps) {
  return (
    <li>
      <Link
        to="/admin/datos-maestros/articulos/$id"
        params={{ id: item.id }}
        className={cn(
          'block px-3 py-2 transition-colors',
          activo
            ? 'bg-primary/10 hover:bg-primary/15'
            : 'hover:bg-muted/40',
        )}
        aria-current={activo ? 'page' : undefined}
      >
        <div className="flex items-start justify-between gap-2">
          <span
            className={cn(
              'truncate font-mono text-sm',
              activo ? 'font-semibold text-foreground' : 'text-foreground',
            )}
          >
            {item.clave}
          </span>
          <div className="flex shrink-0 items-center gap-1">
            <EstatusBadge estatus={item.estatus} />
            <ChevronRight className="h-4 w-4 text-muted-foreground shrink-0" />
          </div>
        </div>
        <div className="mt-1 flex items-center gap-1.5">
          <span className="truncate text-xs text-muted-foreground">
            {item.nombre}
          </span>
          <NaturalezaBadge naturaleza={item.naturaleza} />
        </div>
      </Link>
    </li>
  );
}

function EstatusBadge({ estatus }: { estatus: number }) {
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

function NaturalezaBadge({ naturaleza }: { naturaleza: number }) {
  const meta = NATURALEZA_META[naturaleza] ?? NATURALEZA_META[Naturaleza.Estandar];
  return (
    <Badge variant="outline" className={cn('text-[10px] py-0', meta.className)}>
      {meta.label}
    </Badge>
  );
}

const NATURALEZA_META: Record<number, { label: string; className: string }> = {
  [Naturaleza.Estandar]: { label: 'Estándar', className: '' },
  [Naturaleza.Servicio]: {
    label: 'Servicio',
    className: 'border-sky-300 text-sky-700 dark:text-sky-300',
  },
  [Naturaleza.Critico]: {
    label: 'Crítico',
    className: 'border-amber-300 text-amber-700 dark:text-amber-300',
  },
  [Naturaleza.Riesgo]: {
    label: 'Riesgo',
    className: 'border-rose-300 text-rose-700 dark:text-rose-300',
  },
};
