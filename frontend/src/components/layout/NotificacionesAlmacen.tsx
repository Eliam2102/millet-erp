import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Warehouse } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useAvisosAlmacen, useMarcarAvisoLeido } from '@/features/almacen/api/useAvisosAlmacen';

/**
 * Avisos de Almacén dentro del ERP (vale urgente vencido, ajuste mayor, acta por revisar). Cada usuario ve solo los suyos;
 * el ícono aparece cuando ya recibió alguno.
 */
export function NotificacionesAlmacen() {
  const usuarioId = useAuthStore((s) => s.user?.id);
  const empresaId = useAuthStore((s) => s.currentEmpresaId);
  const [open, setOpen] = useState(false);
  const query = useAvisosAlmacen(Boolean(usuarioId && empresaId));
  const marcar = useMarcarAvisoLeido();
  const avisos = query.data ?? [];
  if (avisos.length === 0) return null;
  const sinLeer = avisos.filter((a) => !a.leido).length;
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button variant="ghost" size="icon" className="relative"
          aria-label={sinLeer ? `Avisos de Almacén: ${sinLeer} sin leer` : 'Avisos de Almacén'}>
          <Warehouse className="h-4 w-4" />
          {sinLeer > 0 && <span className="absolute right-0 top-0 rounded-full bg-danger-bg px-1 text-2xs text-danger-fg tabular-nums">{sinLeer}</span>}
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-96 space-y-3 bg-surface-card shadow-card">
        <h2 className="text-sm font-semibold">Avisos de Almacén</h2>
        <ul className="max-h-96 space-y-2 overflow-auto">
          {avisos.map((a) => (
            <li key={a.id} className={`space-y-1 rounded-md p-2 ${a.leido ? '' : 'bg-surface-selected'}`}>
              <p className="text-sm font-medium text-ink-strong">{a.titulo}</p>
              <p className="text-xs text-ink-muted">{a.mensaje}</p>
              <div className="flex items-center justify-between gap-2">
                <span className="text-xs text-ink-muted">{new Date(a.creadoAt).toLocaleString('es-MX')}</span>
                <span className="flex gap-1">
                  {a.ruta && (
                    <Button asChild variant="link" size="sm">
                      <Link to={a.ruta as '/almacen'} onClick={() => { setOpen(false); if (!a.leido) marcar.mutate(a.id); }}>Abrir</Link>
                    </Button>
                  )}
                  {!a.leido && <Button variant="ghost" size="sm" onClick={() => marcar.mutate(a.id)}>Marcar leído</Button>}
                </span>
              </div>
            </li>
          ))}
        </ul>
      </PopoverContent>
    </Popover>
  );
}
