import { SidebarNav } from '@/components/layout/SidebarNav';

export function Sidebar({
  collapsed,
  onToggle,
  contadores,
}: {
  collapsed: boolean;
  onToggle: () => void;
  contadores: Readonly<Record<string, number>>;
}) {
  return (
    <aside data-print="hidden" className="hidden h-full shrink-0 md:flex">
      <SidebarNav collapsed={collapsed} onToggle={onToggle} contadores={contadores} />
    </aside>
  );
}
