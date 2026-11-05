import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogBody,
  DialogFooter,
} from '@/components/ui/dialog';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useGuardarActivo } from '../api/hooks';
import { useLibroQuery, type Operacion } from '../api/libro';
import type { Activo } from '../api/types';
export function ReversaActivo({ activo: a }: { activo: Activo }) {
  const permitido = useHasPermission(P.ActivosFijosReversasSolicitar);
  const usuario = useAuthStore((s) => s.user?.id);
  const guardar = useGuardarActivo();
  const libro = useLibroQuery<{ operaciones: Operacion[] }>(`activos/${a.id}/libro`);
  const [tipo, setTipo] = useState('');
  const [motivo, setMotivo] = useState('');
  const origen = libro.data?.operaciones.find(
    (o) =>
      !o.anulada &&
      !!o.polizaId &&
      (a.estado === 3 ? o.tipo === 'Venta' && o.ventaCancelada : o.tipo === 'Baja'),
  );
  if (!permitido) return null;
  return (
    <div className="space-y-2">
      {a.estado === 1 && a.polizaAltaId && a.capitalizadoPor === usuario && (
        <Button variant="outline" onClick={() => setTipo('ReversaCapitalizacion')}>
          Revertir capitalización
        </Button>
      )}
      {(a.estado === 2 || a.estado === 3) && origen && (
        <Button
          variant="outline"
          onClick={() => setTipo(a.estado === 3 ? 'ReversaVenta' : 'ReversaBaja')}
        >
          Solicitar reversa de {a.estado === 3 ? 'venta' : 'baja'}
        </Button>
      )}
      {libro.error && <p role="alert">{libro.error.message}</p>}
      <Dialog
        open={!!tipo}
        onOpenChange={(open) => {
          if (!open) setTipo('');
        }}
      >
        <DialogContent size="sm">
          <DialogHeader>
            <DialogTitle>Solicitar reversa</DialogTitle>
          </DialogHeader>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              const f = new FormData(e.currentTarget);
              guardar.mutate(
                {
                  ruta: `activos/${a.id}/operaciones`,
                  version: a.version,
                  body: {
                    tipo,
                    fecha: f.get('fecha'),
                    importe: 0,
                    motivo,
                    revierteA: tipo === 'ReversaCapitalizacion' ? null : origen?.id,
                  },
                },
                {
                  onSuccess: () => {
                    setTipo('');
                    setMotivo('');
                  },
                },
              );
            }}
          >
            <DialogBody className="space-y-3">
              <p>
                El Director de Administración y Finanzas debe autorizar la reversa. Debe ser otra
                persona.
              </p>
              <label>
                Fecha contable *<Input name="fecha" type="date" required />
              </label>
              <label>
                Motivo *
                <Input
                  value={motivo}
                  maxLength={2000}
                  required
                  onChange={(e) => setMotivo(e.target.value)}
                />
              </label>
              {guardar.error && <p role="alert">{guardar.error.message}</p>}
            </DialogBody>
            <DialogFooter>
              <Button type="button" variant="ghost" onClick={() => setTipo('')}>
                Cancelar
              </Button>
              <Button type="submit" disabled={!motivo.trim() || guardar.isPending}>
                Solicitar autorización
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  );
}
