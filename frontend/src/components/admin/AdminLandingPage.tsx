import { useEffect, useRef, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { ChevronRight, Search } from 'lucide-react';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { useAdminRegistry } from '@/lib/admin/use-admin-registry';
import { filtrarConfiguraciones, gruposAdmin, type AdminSection } from '@/lib/admin/registry';
import { useAdminPendientes } from '@/lib/admin/use-admin-pendientes';
import { cn } from '@/lib/utils';

function ConfiguracionFila({
  entry,
  estado,
}: {
  entry: AdminSection;
  estado?: 'Por validar' | 'Datos de ejemplo';
}) {
  const Icon = entry.icon;
  return (
    <Link
      to={entry.href}
      className="flex min-h-14 items-center gap-3 border-b border-line-row px-4 py-3 last:border-b-0 hover:bg-surface-subtle focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand"
    >
      <span className="flex size-7 shrink-0 items-center justify-center rounded-sm bg-surface-muted text-ink-secondary">
        <Icon size={16} strokeWidth={1.6} aria-hidden="true" />
      </span>
      <span className="min-w-0 flex-1">
        <span className="block text-sm font-medium text-ink">{entry.titulo}</span>
        <span className="block truncate text-xs text-ink-muted" title={entry.descripcion}>
          {entry.descripcion}
        </span>
      </span>
      {estado && <Badge variant="warning">{estado}</Badge>}
      <ChevronRight
        size={16}
        strokeWidth={1.6}
        className="shrink-0 text-ink-subtle"
        aria-hidden="true"
      />
    </Link>
  );
}

/** Todas las secciones en la misma página. Índice por anclas; sin pestañas. */
export function AdminLandingPage() {
  const entries = useAdminRegistry();
  const [query, setQuery] = useState('');
  const rows = filtrarConfiguraciones(entries, query);
  const groups = gruposAdmin.filter((group) => rows.some((row) => row.grupo === group.id));
  const [visible, setVisible] = useState<string>('organizacion');
  const sectionsRef = useRef<HTMLDivElement>(null);
  const { pendientes, cargando, error, requiereSucursal } = useAdminPendientes();
  const pendientesFiltrados = pendientes.filter(
    (item) => filtrarConfiguraciones([item.entry], query).length > 0,
  );
  const groupIds = groups.map((group) => group.id).join(',');
  useEffect(() => {
    const container = sectionsRef.current;
    const root = container?.closest('[data-app-content]') ?? null;
    if (!container || typeof IntersectionObserver === 'undefined') return;
    const observados = new Map<string, number>();
    const observer = new IntersectionObserver(
      (observations) => {
        for (const observation of observations) {
          if (observation.isIntersecting)
            observados.set(observation.target.id, observation.boundingClientRect.top);
          else observados.delete(observation.target.id);
        }
        const first = [...observados].sort((a, b) => a[1] - b[1])[0];
        if (first) setVisible(first[0].replace('admin-', ''));
      },
      { root, rootMargin: '0px 0px -50% 0px', threshold: 0 },
    );
    container.querySelectorAll('section[id]').forEach((section) => observer.observe(section));
    return () => observer.disconnect();
  }, [groupIds]);
  const activeGroup = groups.some((group) => group.id === visible) ? visible : groups[0]?.id;
  return (
    <div className="space-y-5 text-sm text-ink">
      <header className="space-y-2">
        <h1 className="text-3xl font-semibold">Administración</h1>
        <p className="text-sm text-ink-muted">
          Empresa, personas, folios y reglas para trabajar en Millet.
        </p>
        <div className="relative max-w-xl">
          <Search
            size={15}
            strokeWidth={1.6}
            aria-hidden="true"
            className="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-ink-muted"
          />
          <label htmlFor="admin-search" className="sr-only">
            Buscar una configuración
          </label>
          <Input
            id="admin-search"
            type="search"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Buscar una configuración (folios, tipo de cambio, roles…)"
            className="pl-8"
          />
        </div>
      </header>
      <div className="flex flex-col items-start gap-5 md:flex-row">
        <nav
          data-print="hidden"
          aria-label="Secciones de Administración"
          className="top-0 w-full shrink-0 space-y-1 rounded-lg bg-surface-card p-2 shadow-card-flat md:sticky md:w-admin-index"
        >
          {groups.map((group) => (
            <a
              key={group.id}
              href={`#admin-${group.id}`}
              aria-current={activeGroup === group.id ? 'location' : undefined}
              onClick={() => setVisible(group.id)}
              className={cn(
                'flex min-h-11 items-center rounded-sm px-2 text-sm focus-visible:outline-2 focus-visible:outline-brand md:min-h-8',
                activeGroup === group.id
                  ? 'bg-surface-selected font-medium text-brand'
                  : 'text-ink-strong hover:bg-surface-subtle',
              )}
            >
              {group.titulo}
            </a>
          ))}
        </nav>
        <div ref={sectionsRef} className="min-w-0 flex-1 space-y-4">
          <section
            aria-labelledby="admin-pendientes"
            className="rounded-lg bg-surface-card shadow-card-flat"
          >
            <h2
              id="admin-pendientes"
              className="border-b border-line-divider px-4 py-3 text-lg font-semibold"
            >
              Falta configurar o validar
            </h2>
            {pendientesFiltrados.map((item) => (
              <ConfiguracionFila key={item.entry.id} entry={item.entry} estado={item.estado} />
            ))}
            {cargando && (
              <p role="status" className="p-4 text-xs text-ink-muted">
                Consultando la configuración disponible…
              </p>
            )}
            {error && (
              <p role="status" className="p-4 text-xs text-ink-muted">
                Algunas comprobaciones no se pudieron consultar. Su estado está por confirmar.
              </p>
            )}
            {!cargando && !error && pendientesFiltrados.length === 0 && (
              <p className="p-4 text-xs text-ink-muted">
                No hay pendientes comprobados
                {query ? ' para esta búsqueda' : ' en los datos consultados'}. Esto no acredita la
                validación completa de la configuración.
              </p>
            )}
            {requiereSucursal && (
              <p className="px-4 pb-3 text-xs text-ink-muted">
                Selecciona una sucursal en el menú para comprobar sus series y equivalencias.
              </p>
            )}
          </section>
          {groups.length === 0 && (
            <p role="status" className="p-4 text-sm text-ink-muted">
              No hay configuraciones que coincidan con tu búsqueda.
            </p>
          )}
          {groups.map((group) => {
            const groupRows = rows
              .filter((row) => row.grupo === group.id)
              .sort((a, b) => a.orden - b.orden);
            const normales = groupRows.filter((row) => !row.soloConsulta);
            const sat = groupRows.filter((row) => row.soloConsulta);
            return (
              <section
                key={group.id}
                id={`admin-${group.id}`}
                aria-labelledby={`titulo-${group.id}`}
                className="scroll-mt-4 overflow-hidden rounded-lg bg-surface-card shadow-card-flat"
              >
                <h2
                  id={`titulo-${group.id}`}
                  className="border-b border-line-divider px-4 py-3 text-lg font-semibold"
                >
                  {group.titulo}
                </h2>
                {group.id === 'modulos'
                  ? [...new Set(normales.map((row) => row.nombreModulo ?? row.titulo))].map(
                      (name) => (
                        <div key={name}>
                          <h3 className="bg-surface-subtle px-4 py-2 text-xs font-semibold text-ink-secondary">
                            {name}
                          </h3>
                          {normales
                            .filter((row) => (row.nombreModulo ?? row.titulo) === name)
                            .map((row) => (
                              <ConfiguracionFila key={row.id} entry={row} />
                            ))}
                        </div>
                      ),
                    )
                  : normales.map((row) => <ConfiguracionFila key={row.id} entry={row} />)}
                {sat.length > 0 && (
                  <div>
                    <h3 className="bg-surface-subtle px-4 py-2 text-xs font-semibold text-ink-secondary">
                      Del SAT · solo consulta
                    </h3>
                    {sat.map((row) => (
                      <ConfiguracionFila key={row.id} entry={row} />
                    ))}
                  </div>
                )}
              </section>
            );
          })}
        </div>
      </div>
    </div>
  );
}
