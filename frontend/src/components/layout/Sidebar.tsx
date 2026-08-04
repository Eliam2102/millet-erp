import type { NavModulo } from '@/lib/nav';
import { SidebarNav } from '@/components/layout/SidebarNav';

/**
 * Sidebar fijo (240px) — desktop only (visible <c>md:</c> y arriba).
 * Wrapper del <c>&lt;SidebarNav/&gt;</c> reutilizable; el comportamiento
 * de los módulos vive ahí. El state del <c>AppLauncherModal</c> vive
 * en <c>&lt;AppShell/&gt;</c> y se delega vía <c>onModuloOpen</c>.
 *
 * <para>En mobile (sub-<c>md</c>) este componente está oculto; el
 * usuario abre la nav vía hamburger en el topbar que dispara
 * <c>&lt;MobileSidebar/&gt;</c> (Dialog slide-from-left).</para>
 */
export interface SidebarProps {
  onModuloOpen: (modulo: NavModulo) => void;
}

export function Sidebar({ onModuloOpen }: SidebarProps) {
  return (
    <aside className="fixed inset-y-0 left-0 z-30 hidden md:flex">
      <SidebarNav onModuloOpen={onModuloOpen} />
    </aside>
  );
}
