import { useEffect, useState, type ReactNode } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { Search } from 'lucide-react';
import { accesosNavegacion } from '@/lib/nav';
import { useAuthStore } from '@/lib/auth/auth-store';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog';
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from '@/components/ui/command';

const normalize = (value: string) =>
  value
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLocaleLowerCase('es-MX');

export function AccessSearch({ children }: { children?: ReactNode }) {
  const [open, setOpen] = useState(false);
  const navigate = useNavigate();
  const permisos = useAuthStore((state) => state.permisos);
  const switching = useAuthStore((state) => state.isSwitchingEmpresa);
  const accesos = accesosNavegacion(permisos);
  const grupos = [...new Set(accesos.map((acceso) => acceso.modulo))];

  useEffect(() => {
    function shortcut(event: KeyboardEvent) {
      if (
        (event.metaKey || event.ctrlKey) &&
        event.key.toLowerCase() === 'k' &&
        !event.altKey &&
        !event.repeat
      ) {
        event.preventDefault();
        if (!useAuthStore.getState().isSwitchingEmpresa) setOpen((value) => !value);
      }
    }
    document.addEventListener('keydown', shortcut);
    return () => document.removeEventListener('keydown', shortcut);
  }, []);

  function abrir(to: string) {
    const state = useAuthStore.getState();
    if (
      state.isSwitchingEmpresa ||
      !accesosNavegacion(state.permisos).some((acceso) => acceso.to === to)
    )
      return;
    setOpen(false);
    void navigate({ to });
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button
          variant="secondary"
          aria-label="Buscar o ir a"
          aria-keyshortcuts="Meta+K Control+K"
          disabled={switching}
          title={
            switching ? 'Espera a que termine el cambio de empresa' : 'Buscar accesos (⌘K o Ctrl+K)'
          }
          className="ml-auto w-11 shrink-0 border-line bg-surface-page px-0 text-ink-muted [&_svg]:size-3.75 md:w-full md:max-w-80 md:flex-1 md:justify-start md:px-3"
        >
          <Search size={15} strokeWidth={1.8} aria-hidden="true" />
          <span className="hidden md:inline">Buscar o ir a…</span>
          <kbd className="ml-auto hidden rounded-xs border border-line-control bg-surface-card px-1 font-mono text-2xs md:inline">
            ⌘K
          </kbd>
        </Button>
      </DialogTrigger>
      <DialogContent
        closeLabel="Cerrar buscador"
        overlayClassName="bg-(--mt-scrim-dialog)"
        className="flex h-[min(520px,calc(100dvh-2rem))] max-h-[calc(100dvh-2rem)] w-[calc(100%-2rem)] flex-col gap-0 overflow-hidden rounded-2xl border-0 p-0 shadow-dialog sm:top-[120px] sm:max-h-[calc(100dvh-136px)] sm:max-w-dialog sm:translate-y-0 sm:rounded-2xl [&>button]:flex [&>button]:size-11 [&>button]:items-center [&>button]:justify-center [&>button]:right-2 [&>button]:top-1.5 sm:[&>button]:size-8 sm:[&>button]:right-3 sm:[&>button]:top-3"
      >
        <DialogTitle className="sr-only">Buscar o ir a</DialogTitle>
        <DialogDescription className="sr-only">
          Busca pantallas por nombre o módulo. Usa las flechas y Enter para abrir; Escape para
          cerrar.
        </DialogDescription>
        <Command
          label="Buscar accesos"
          key={open ? 'open' : 'closed'}
          filter={(value, search, keywords) => {
            const text = normalize(`${value} ${keywords?.join(' ') ?? ''}`);
            const words = normalize(search).trim().split(/\s+/);
            return words.every((word) => text.includes(word)) ? 1 : 0;
          }}
          className="min-h-0 flex-1 rounded-none bg-surface-card text-ink"
        >
          <CommandInput
            placeholder="Buscar pantallas o módulos…"
            aria-label="Buscar accesos"
            className="h-14 pr-14 text-xl"
          />
          <CommandList className="min-h-0 max-h-none flex-1 p-2">
            <CommandEmpty className="px-4 py-8 text-sm text-ink-muted">
              No hay accesos que coincidan con tu búsqueda.
            </CommandEmpty>
            {grupos.map((grupo) => (
              <CommandGroup
                key={grupo}
                heading={grupo}
                className="[&_[cmdk-group-heading]]:text-2xs [&_[cmdk-group-heading]]:font-semibold [&_[cmdk-group-heading]]:uppercase [&_[cmdk-group-heading]]:tracking-wide"
              >
                {accesos
                  .filter((acceso) => acceso.modulo === grupo)
                  .map((acceso) => {
                    const Icon = acceso.icon;
                    return (
                      <CommandItem
                        key={acceso.to}
                        value={acceso.to}
                        keywords={[acceso.label, grupo]}
                        onSelect={() => abrir(acceso.to)}
                        className="min-h-11 cursor-pointer gap-3 rounded-md px-3 py-2 text-ink data-[selected=true]:bg-surface-selected data-[selected=true]:text-ink"
                      >
                        <span className="flex size-7 shrink-0 items-center justify-center rounded-sm bg-surface-muted text-ink-secondary">
                          <Icon size={16} strokeWidth={1.6} aria-hidden="true" />
                        </span>
                        <span className="min-w-0">
                          <span className="block text-sm font-medium">{acceso.label}</span>
                          <span className="block truncate text-xs text-ink-muted">
                            {acceso.modulo} / {acceso.label}
                          </span>
                        </span>
                      </CommandItem>
                    );
                  })}
              </CommandGroup>
            ))}
          </CommandList>
        </Command>
        {children && (
          <div className="border-t border-line-divider bg-surface-subtle px-4 py-3">
            <p className="mb-2 text-xs font-medium text-ink-muted">Buscar en esta pantalla</p>
            {children}
          </div>
        )}
        <div className="flex items-center justify-between border-t border-line-divider bg-surface-subtle px-4 py-3 text-2xs text-ink-muted">
          <span>↑ ↓ navegar · Enter abrir</span>
          <span>Esc cerrar</span>
        </div>
      </DialogContent>
    </Dialog>
  );
}
