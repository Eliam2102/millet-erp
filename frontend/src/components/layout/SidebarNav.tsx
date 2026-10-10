import { Home, LogOut, PanelLeftClose, PanelLeftOpen, Search, Settings } from 'lucide-react';
import { Link, useLocation } from '@tanstack/react-router';
import { gruposMenu, menuModulos } from '@/lib/nav';
import { normalizarNombre } from '@/lib/admin/registry';
import { useAdminAccess } from '@/lib/admin/use-admin-registry';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useAuth } from '@/lib/auth/useAuth';
import { EmpresaSelector } from '@/components/auth/EmpresaSelector';
import { SucursalSelector } from '@/components/auth/SucursalSelector';
import { Avatar, AvatarFallback } from '@/components/ui/avatar';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip';
import { cn } from '@/lib/utils';
import { useId, useState } from 'react';

export interface SidebarNavProps {
  onLinkSelect?: () => void;
  collapsed?: boolean;
  onToggle?: () => void;
  contadores?: Readonly<Record<string, number>>;
}

/** Menú compartido por escritorio y móvil; no abre paneles ni launchers. */
export function SidebarNav({
  onLinkSelect,
  collapsed = false,
  onToggle,
  contadores = {},
}: SidebarNavProps) {
  const { pathname } = useLocation();
  const permisos = useAuthStore((s) => s.permisos);
  const { user, currentEmpresa, logout } = useAuth();
  const puedeVerAdmin = useAdminAccess();
  const [filtro, setFiltro] = useState('');
  const filterId = useId();
  const modulos = menuModulos(permisos, collapsed ? '' : filtro);
  const query = normalizarNombre(collapsed ? '' : filtro.trim());
  const activeTo = modulos
    .flatMap((module) => module.cards)
    .filter((card) => pathname === card.to || pathname.startsWith(`${card.to}/`))
    .sort((a, b) => b.to.length - a.to.length)[0]?.to;
  const active = (to: string) =>
    to === '/' ? pathname === '/' : pathname === to || pathname.startsWith(`${to}/`);
  const linkClass = (selected: boolean) =>
    cn(
      'flex min-h-11 items-center gap-2.5 rounded-sm px-2 text-sm focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand md:min-h-8',
      selected ? 'bg-rail-active text-white' : 'text-rail-fg hover:bg-rail-active hover:text-white',
      collapsed && 'justify-center',
    );
  function namedLink(
    to: string,
    label: string,
    Icon: typeof Home,
    selected: boolean,
    contador?: number,
  ) {
    const link = (
      <Link
        to={to}
        onClick={onLinkSelect}
        aria-label={label}
        aria-current={selected ? 'page' : undefined}
        className={linkClass(selected)}
      >
        <Icon size={18} strokeWidth={1.6} aria-hidden="true" />
        {!collapsed && <span>{label}</span>}
        {!collapsed && contador != null && contador > 0 && (
          <span
            aria-label={`${contador} pendientes en ${label}`}
            className="ml-auto rounded-full bg-rail-active px-1.5 text-2xs tabular-nums text-white"
          >
            {contador}
          </span>
        )}
      </Link>
    );
    return collapsed ? (
      <Tooltip>
        <TooltipTrigger asChild>{link}</TooltipTrigger>
        <TooltipContent side="right">{label}</TooltipContent>
      </Tooltip>
    ) : (
      link
    );
  }
  return (
    <TooltipProvider>
      <div
        data-print="hidden"
        className={cn(
          'flex h-full min-h-0 flex-col bg-rail text-rail-fg',
          collapsed ? 'w-menu-collapsed' : 'w-menu',
        )}
      >
        {!collapsed && (
          <div className="shrink-0 space-y-2 border-b border-rail-active p-3 pr-8 md:pr-3">
            <p className="text-sm font-semibold text-white">
              {currentEmpresa?.razonSocial ?? '[EMPRESA]'}
            </p>
            <EmpresaSelector />
            <SucursalSelector />
            <div className="relative">
              <Search
                size={15}
                strokeWidth={1.6}
                aria-hidden="true"
                className="pointer-events-none absolute top-1/2 left-2 -translate-y-1/2 text-rail-fg"
              />
              <label htmlFor={filterId} className="sr-only">
                Filtrar menú
              </label>
              <Input
                id={filterId}
                placeholder="Filtrar menú"
                value={filtro}
                onChange={(event) => setFiltro(event.target.value)}
                className="h-8 border-rail-active bg-rail-active pl-7 text-white placeholder:text-rail-fg"
              />
            </div>
          </div>
        )}
        <nav aria-label="Navegación principal" className="min-h-0 flex-1 overflow-y-auto px-2 py-3">
          {(!query || normalizarNombre('Inicio').includes(query)) &&
            namedLink('/', 'Inicio', Home, active('/'))}
          {gruposMenu.map((grupo) => {
            const items = grupo.modulos.flatMap((id) =>
              modulos.filter((module) => module.moduloId === id),
            );
            if (items.length === 0) return null;
            return (
              <section key={grupo.titulo} aria-label={grupo.titulo} className="mt-3">
                {!collapsed && (
                  <h2 className="px-2 pb-1 text-2xs font-semibold uppercase tracking-[0.05em] text-rail-fg">
                    {grupo.titulo}
                  </h2>
                )}
                {items.map((module) => (
                  <div key={module.moduloId}>
                    {namedLink(
                      module.to,
                      module.label,
                      module.icon,
                      module.cards.some((card) => active(card.to)) || active(`/${module.moduloId}`),
                      contadores[module.label],
                    )}
                    {!collapsed && module.cards.length > 0 && (
                      <ul className="my-1 ml-7 border-l border-rail-active">
                        {module.cards.map((card) => (
                          <li key={card.to}>
                            <Link
                              to={card.to}
                              onClick={onLinkSelect}
                              aria-current={card.to === activeTo ? 'page' : undefined}
                              className={cn(
                                'flex min-h-11 items-center border-l-2 px-2 text-xs focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand md:h-7 md:min-h-7',
                                card.to === activeTo
                                  ? 'border-brand font-medium text-white'
                                  : 'border-transparent text-rail-fg hover:text-white',
                              )}
                            >
                              <span className="truncate" title={card.label}>
                                {card.label}
                              </span>
                            </Link>
                          </li>
                        ))}
                      </ul>
                    )}
                  </div>
                ))}
              </section>
            );
          })}
          {query && modulos.length === 0 && !normalizarNombre('Inicio').includes(query) && (
            <p role="status" className="p-2 text-xs">
              No hay opciones para esta búsqueda.
            </p>
          )}
        </nav>
        <div className="shrink-0 space-y-2 border-t border-rail-active p-2">
          {puedeVerAdmin &&
            namedLink('/admin', 'Administración', Settings, active('/admin') && !activeTo)}
          <div className={cn('flex items-center gap-2 px-2', collapsed && 'justify-center')}>
            <Avatar className="size-7 shrink-0">
              <AvatarFallback className="bg-brand-avatar text-xs font-semibold text-brand">
                {user?.nombre
                  .split(' ')
                  .filter(Boolean)
                  .slice(0, 2)
                  .map((word) => word[0])
                  .join('') ?? '?'}
              </AvatarFallback>
            </Avatar>
            {!collapsed && (
              <div className="min-w-0">
                <p className="truncate text-xs font-medium text-white">
                  {user?.nombre ?? '[PERSONA]'}
                </p>
                <p className="text-2xs text-rail-fg">[ROL]</p>
              </div>
            )}
          </div>
          <Button
            variant="ghost"
            onClick={() => void logout()}
            aria-label="Cerrar sesión"
            title="Cerrar sesión"
            className={cn(
              'w-full justify-start text-rail-fg hover:bg-rail-active hover:text-white',
              collapsed && 'justify-center px-0',
            )}
          >
            <LogOut size={18} strokeWidth={1.6} aria-hidden="true" />
            {!collapsed && 'Cerrar sesión'}
          </Button>
          {onToggle && (
            <Button
              variant="ghost"
              onClick={onToggle}
              aria-label={collapsed ? 'Expandir menú' : 'Contraer menú'}
              title={collapsed ? 'Expandir menú' : 'Contraer menú'}
              className={cn(
                'w-full justify-start text-rail-fg hover:bg-rail-active hover:text-white',
                collapsed && 'justify-center px-0',
              )}
            >
              {collapsed ? (
                <PanelLeftOpen size={18} strokeWidth={1.6} aria-hidden="true" />
              ) : (
                <PanelLeftClose size={18} strokeWidth={1.6} aria-hidden="true" />
              )}
              {!collapsed && 'Contraer menú'}
            </Button>
          )}
        </div>
      </div>
    </TooltipProvider>
  );
}
