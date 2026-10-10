import { useState } from 'react';
import { Link, useLocation } from '@tanstack/react-router';
import { PanelLeftClose, Search } from 'lucide-react';
import type { NavModulo } from '@/lib/nav';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { cn } from '@/lib/utils';

export function ModulePanel({ modulo, onCollapse }: { modulo: NavModulo; onCollapse: () => void }) {
  const [filter, setFilter] = useState('');
  const { pathname } = useLocation();
  const normalize = (value: string) =>
    value
      .normalize('NFD')
      .replace(/\p{Diacritic}/gu, '')
      .toLocaleLowerCase('es-MX');
  const query = normalize(filter.trim());
  const secciones = modulo.secciones
    .map((seccion) => ({
      ...seccion,
      cards: seccion.cards.filter((card) =>
        normalize(`${seccion.label} ${card.label}`).includes(query),
      ),
    }))
    .filter((seccion) => seccion.cards.length > 0);
  const activeTo = modulo.secciones
    .flatMap((seccion) => seccion.cards)
    .filter((card) => pathname === card.to || pathname.startsWith(`${card.to}/`))
    .sort((a, b) => b.to.length - a.to.length)[0]?.to;

  return (
    <aside
      aria-label={`Navegación de ${modulo.label}`}
      className="sticky top-0 hidden h-screen w-module-panel shrink-0 flex-col border-r border-line bg-surface-card md:flex"
    >
      <div className="flex h-topbar shrink-0 items-center justify-between border-b border-line-divider px-4">
        <div>
          <p className="text-2xs uppercase tracking-wide text-ink-muted">Módulo</p>
          <h2 className="text-lg font-semibold text-ink">{modulo.label}</h2>
        </div>
        <Button
          variant="ghost"
          size="sm"
          onClick={onCollapse}
          aria-label="Contraer panel"
          className="w-7 px-0"
        >
          <PanelLeftClose size={16} strokeWidth={1.6} />
        </Button>
      </div>
      <div className="relative m-3">
        <Search
          size={14}
          strokeWidth={1.8}
          className="pointer-events-none absolute top-1/2 left-2.5 -translate-y-1/2 text-ink-muted"
          aria-hidden="true"
        />
        <Input
          aria-label="Filtrar menú del módulo"
          placeholder="Filtrar menú"
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
          className="h-8 border-line bg-surface-subtle pl-8"
        />
      </div>
      <nav className="min-h-0 flex-1 overflow-y-auto px-3 pb-4">
        {secciones.map((seccion) => (
          // Secciones siempre abiertas: todo el menú del módulo a la vista (Eliam, 10-oct).
          <section key={seccion.label} aria-label={seccion.label} className="mb-4">
            <h2 className="px-1 py-2 text-2xs font-semibold uppercase tracking-[0.05em] text-ink-muted">
              {seccion.label}
            </h2>
            <ul className="space-y-1">
              {seccion.cards.map((card) => (
                <li key={card.to}>
                  <Link
                    to={card.to}
                    aria-current={card.to === activeTo ? 'page' : undefined}
                    className={cn(
                      'flex min-h-8 items-center rounded-sm px-2.5 py-1 text-sm focus-visible:outline-2 focus-visible:outline-brand',
                      card.to === activeTo
                        ? 'bg-surface-selected font-medium text-brand'
                        : 'text-ink-strong hover:bg-surface-subtle',
                    )}
                  >
                    {card.label}
                  </Link>
                </li>
              ))}
            </ul>
          </section>
        ))}
        {secciones.length === 0 && (
          <p role="status" className="px-2.5 py-3 text-xs text-ink-muted">
            No hay opciones para esta búsqueda.
          </p>
        )}
      </nav>
    </aside>
  );
}
