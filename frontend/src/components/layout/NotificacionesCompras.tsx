import { useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { Bell } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { apiRequest } from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { ListarOrdenesCompraResponse } from '@/features/compras/ordenes/api/types';

/** Avisos operativos derivados de pendientes reales; nunca se inventa una autorización. */
export function NotificacionesCompras() {
  const usuarioId = useAuthStore((s) => s.user?.id);
  const empresaId = useAuthStore((s) => s.currentEmpresaId);
  const permisos = useAuthStore((s) => s.permisos);
  const [open, setOpen] = useState(false);
  const puedeVer = permisos.includes(PermisosCanonicos.ComprasOrdenesLeer) &&
    (permisos.includes(PermisosCanonicos.ComprasOrdenesAutorizarNivel1) || permisos.includes(PermisosCanonicos.ComprasOrdenesAutorizarNivel2));
  const query = useQuery({
    queryKey: ['compras', 'ordenes', 'avisos-autorizacion', usuarioId, empresaId, permisos],
    enabled: Boolean(usuarioId && empresaId && puedeVer),
    queryFn: async ({ signal }) => (await apiRequest<ListarOrdenesCompraResponse>(
      '/api/v1/compras/ordenes/pendientes-autorizacion?soloAccionables=true&pageSize=10', { signal },
    )).data,
    refetchInterval: 15000,
    refetchOnWindowFocus: true,
  });
  const vistos = useRef<{ key: string; ids: Set<string> } | null>(null);
  const items = puedeVer ? query.data?.items ?? [] : [];
  const total = puedeVer ? query.data?.totalCount ?? 0 : 0;
  useEffect(() => {
    if (!query.data || !puedeVer) return;
    const key = `${usuarioId}/${empresaId}`;
    const ids = new Set(query.data.items.map((o) => o.id));
    if (vistos.current?.key === key) {
      const nuevos = query.data.items.filter((o) => !vistos.current!.ids.has(o.id));
      if (nuevos.length) toast.info('Órdenes de compra pendientes de tu autorización', {
        description: nuevos.map((o) => o.folio).join(', '),
      });
    }
    vistos.current = { key, ids };
  }, [query.data, usuarioId, empresaId, puedeVer]);
  return <Popover open={open} onOpenChange={setOpen}>
    <PopoverTrigger asChild>
      <Button variant="ghost" size="icon" className="relative" aria-label={total ? `Notificaciones: ${total} órdenes por autorizar` : 'Notificaciones'}>
        <Bell className="h-4 w-4" />
        {total > 0 && <span className="absolute right-0 top-0 rounded-full bg-danger-bg px-1 text-2xs text-danger-fg tabular-nums">{total}</span>}
      </Button>
    </PopoverTrigger>
    <PopoverContent align="end" className="w-80 space-y-3 bg-surface-card shadow-card">
      <h2 className="text-sm font-semibold">Notificaciones de Compras</h2>
      {puedeVer && query.isPending ? <p className="text-sm text-ink-muted">Consultando pendientes…</p>
        : puedeVer && query.isError ? <div role="alert" className="text-sm text-danger-fg">No se pudieron consultar los avisos. <Button variant="link" onClick={() => void query.refetch()}>Reintentar</Button></div>
        : items.length === 0 ? <p className="text-sm text-ink-muted">No tienes órdenes pendientes de autorización.</p>
        : <ul className="max-h-80 space-y-2 overflow-auto">{items.map((o) => <li key={o.id}>
          <Link to="/compras/ordenes/$id" params={{ id: o.id }} onClick={() => setOpen(false)} className="block space-y-1 rounded-md p-2 hover:bg-surface-selected">
            <span className="font-mono text-sm text-brand">{o.folio}</span>
            <Badge variant="warning">{o.estado === 1 ? 'Requiere firma de Compras' : 'Requiere firma de Dirección'}</Badge>
          </Link>
        </li>)}</ul>}
      {total > 0 && <Button asChild variant="link" size="sm"><Link to="/compras/ordenes/pendientes-autorizacion" onClick={() => setOpen(false)}>Ver todas ({total})</Link></Button>}
    </PopoverContent>
  </Popover>;
}
