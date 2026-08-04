import { Pencil } from 'lucide-react';
import { Avatar, AvatarFallback } from '@/components/ui/avatar';
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip';
import {
  useCollaboration,
  type PresenceUser,
} from '@/components/erp/collaboration/useCollaboration';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;CollaborationIndicator/&gt;</c> — avatares apilados de otros
 * usuarios con la misma entidad abierta, para que el actual sepa que
 * no está solo (doc 05 §9.2). UF8-PR1 — wired contra
 * <c>ComprasHub</c>.
 *
 * <para><b>Layout</b>: hasta 3 avatares apilados + badge "+N" si hay
 * más. Los que estén editando llevan un dot animado en la esquina
 * (icono <c>Pencil</c> sobre badge ámbar). Tooltip al hover en cada
 * avatar muestra el nombre y el modo.</para>
 *
 * <para><b>Accesibilidad</b>: <c>aria-live="polite"</c> + <c>role="status"</c>
 * para que screen readers anuncien cuando alguien entra/sale sin
 * interrumpir flujo. <c>aria-label</c> resume el conjunto.</para>
 *
 * <para>Si no hay nadie más viendo/editando la entidad (o no hay <c>id</c>
 * todavía), el componente devuelve <c>null</c> — sin ruido visual.</para>
 *
 * @example
 * ```tsx
 * <CollaborationIndicator entidad="requisicion" id={requisicion.id} />
 * ```
 */
export interface CollaborationIndicatorProps {
  entidad: string;
  id: string | null | undefined;
}

const MAX_AVATARES_VISIBLES = 3;

export function CollaborationIndicator({
  entidad,
  id,
}: CollaborationIndicatorProps) {
  const { viendo, editando } = useCollaboration(entidad, id);

  // Editando primero (más relevantes), luego viendo. Total ordenado.
  const todos: Array<PresenceUser & { modo: 'editando' | 'viendo' }> = [
    ...editando.map((u) => ({ ...u, modo: 'editando' as const })),
    ...viendo.map((u) => ({ ...u, modo: 'viendo' as const })),
  ];

  if (todos.length === 0) return null;

  const visibles = todos.slice(0, MAX_AVATARES_VISIBLES);
  const restantes = todos.length - visibles.length;

  const aria = construirAria(viendo, editando);

  return (
    <TooltipProvider delayDuration={250}>
      <div
        role="status"
        aria-live="polite"
        aria-label={aria}
        className="flex items-center -space-x-1.5"
        data-collaboration-indicator
      >
        {visibles.map((u) => (
          <AvatarPersona key={u.userId} user={u} />
        ))}
        {restantes > 0 && (
          <Tooltip>
            <TooltipTrigger asChild>
              <Avatar className="size-7 ring-2 ring-background">
                <AvatarFallback className="bg-slate-200 text-[10px] font-semibold">
                  +{restantes}
                </AvatarFallback>
              </Avatar>
            </TooltipTrigger>
            <TooltipContent>
              {restantes === 1
                ? '1 persona más en esta vista'
                : `${restantes} personas más en esta vista`}
            </TooltipContent>
          </Tooltip>
        )}
      </div>
    </TooltipProvider>
  );
}

interface AvatarPersonaProps {
  user: PresenceUser & { modo: 'editando' | 'viendo' };
}

function AvatarPersona({ user }: AvatarPersonaProps) {
  const iniciales = obtenerIniciales(user.nombre);
  const editando = user.modo === 'editando';
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span className="relative inline-flex">
          <Avatar
            className={cn(
              'size-7 ring-2 ring-background',
              editando && 'ring-amber-300',
            )}
          >
            <AvatarFallback
              className={cn(
                'text-[10px] font-semibold',
                editando ? 'bg-amber-100 text-amber-900' : 'bg-blue-100 text-blue-900',
              )}
            >
              {iniciales}
            </AvatarFallback>
          </Avatar>
          {editando && (
            <span
              aria-hidden="true"
              className="absolute -right-0.5 -top-0.5 flex size-3.5 items-center justify-center rounded-full bg-amber-500 ring-1 ring-background"
            >
              <Pencil className="size-2 text-white" />
            </span>
          )}
        </span>
      </TooltipTrigger>
      <TooltipContent>
        <span className="font-medium">{user.nombre}</span>{' '}
        <span className="text-muted-foreground">
          {editando ? 'está editando' : 'está mirando'}
        </span>
      </TooltipContent>
    </Tooltip>
  );
}

function obtenerIniciales(nombre: string): string {
  const partes = nombre.trim().split(/\s+/);
  if (partes.length === 0) return '?';
  if (partes.length === 1) return partes[0].slice(0, 2).toUpperCase();
  return (partes[0][0] + partes[partes.length - 1][0]).toUpperCase();
}

function construirAria(
  viendo: PresenceUser[],
  editando: PresenceUser[],
): string {
  const partes: string[] = [];
  if (editando.length > 0) {
    partes.push(
      `${editando.length} ${editando.length === 1 ? 'persona editando' : 'personas editando'}`,
    );
  }
  if (viendo.length > 0) {
    partes.push(
      `${viendo.length} ${viendo.length === 1 ? 'persona mirando' : 'personas mirando'}`,
    );
  }
  return partes.join(', ') + ' esta vista';
}
