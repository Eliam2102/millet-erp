import { Link, useLocation } from '@tanstack/react-router';
import { navSidebarItems, type NavModulo } from '@/lib/nav';
import { useAuthStore } from '@/lib/auth/auth-store';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;SidebarNav/&gt;</c> — contenido reutilizable del sidebar
 * (header con logo + nav de módulos + footer). Sin posición fija ni
 * media query: el wrapper decide si vive en un <c>&lt;aside&gt;</c>
 * fijo (desktop) o dentro de un Dialog slide-from-left (mobile).
 *
 * <para>UF7-PR3 — extraído de <c>&lt;Sidebar/&gt;</c> para soportar
 * <c>&lt;MobileSidebar/&gt;</c>. El state del <c>App Launcher Modal</c>
 * vive en <c>&lt;AppShell/&gt;</c> (no aquí) para que sobreviva al
 * desmontaje del mobile drawer cuando se elige un módulo.</para>
 */
export interface SidebarNavProps {
  /** Callback al clickear un link directo (Inicio). El wrapper mobile
   * lo usa para cerrar el drawer al navegar. */
  onLinkSelect?: () => void;
  /** Callback al clickear un módulo. El parent decide qué hacer
   * (típicamente abrir el AppLauncher modal). */
  onModuloOpen: (modulo: NavModulo) => void;
  className?: string;
}

export function SidebarNav({
  onLinkSelect,
  onModuloOpen,
  className,
}: SidebarNavProps) {
  const { pathname } = useLocation();
  const permisos = useAuthStore((s) => s.permisos);

  const isLinkActive = (to: string) =>
    to === '/' ? pathname === '/' : pathname === to || pathname.startsWith(`${to}/`);

  function isModuloActive(modulo: NavModulo): boolean {
    return modulo.secciones.some((s) =>
      s.cards.some((c) => pathname === c.to || pathname.startsWith(`${c.to}/`)),
    );
  }

  void permisos;

  return (
    <div
      className={cn(
        'flex h-full w-60 flex-col bg-sidebar text-sidebar-foreground',
        className,
      )}
    >
      <div className="flex h-14 shrink-0 items-center gap-2 border-b border-sidebar-border px-4">
        <div className="flex h-7 w-7 items-center justify-center rounded bg-sidebar-accent text-xs font-bold text-white">
          M
        </div>
        <span className="text-sm font-semibold tracking-wide text-white">
          Millet ERP
        </span>
      </div>

      <nav className="flex-1 overflow-y-auto py-3">
        <ul className="space-y-0.5 px-2">
          {navSidebarItems.map((item) => {
            const Icon = item.icon;

            if (item.kind === 'link') {
              const active = isLinkActive(item.to);
              return (
                <li key={item.label}>
                  <Link
                    to={item.to}
                    onClick={onLinkSelect}
                    className={cn(
                      'flex items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors',
                      active
                        ? 'bg-sidebar-active text-sidebar-active-foreground'
                        : 'text-sidebar-foreground hover:bg-sidebar-active/50 hover:text-white',
                    )}
                  >
                    <Icon className="h-4 w-4 shrink-0" />
                    <span className="truncate">{item.label}</span>
                  </Link>
                </li>
              );
            }

            if (item.disabled) {
              return (
                <li key={item.moduloId}>
                  <span
                    title="Próximamente"
                    className="flex cursor-not-allowed items-center gap-3 rounded-md px-3 py-2 text-sm text-sidebar-muted/60"
                  >
                    <Icon className="h-4 w-4 shrink-0" />
                    <span className="truncate">{item.label}</span>
                  </span>
                </li>
              );
            }

            const active = isModuloActive(item);
            return (
              <li key={item.moduloId}>
                <button
                  type="button"
                  onClick={() => onModuloOpen(item)}
                  className={cn(
                    'flex w-full items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors',
                    active
                      ? 'bg-sidebar-active text-sidebar-active-foreground'
                      : 'text-sidebar-foreground hover:bg-sidebar-active/50 hover:text-white',
                  )}
                  aria-haspopup="dialog"
                >
                  <Icon className="h-4 w-4 shrink-0" />
                  <span className="truncate">{item.label}</span>
                </button>
              </li>
            );
          })}
        </ul>
      </nav>

      <div className="shrink-0 border-t border-sidebar-border px-4 py-3 text-xs text-sidebar-muted">
        v0.1 · dev
      </div>
    </div>
  );
}
