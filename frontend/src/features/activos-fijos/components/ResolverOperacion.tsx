import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogBody,
  DialogFooter,
} from '@/components/ui/dialog';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useGuardarActivo } from '../api/hooks';
import type { Operacion } from '../api/libro';
export function ResolverOperacion({ operacion: o }: { operacion: Operacion }) {
  const guardar = useGuardarActivo();
  const usuario = useAuthStore((s) => s.user?.id);
  const permitido = useHasPermission(
    o.tipo.startsWith('Reversa')
      ? P.ActivosFijosReversasAutorizar
      : o.tipo === 'Baja'
        ? P.ActivosFijosBajasAutorizar
        : P.ActivosFijosAjustesAutorizar,
  );
  const [rechazar, setRechazar] = useState(false);
  const [motivo, setMotivo] = useState('');
  if (o.anulada)
    return (
      <div>
        <Badge variant="neutral">{o.motivoRechazo ? 'Rechazada' : 'Anulada'}</Badge>
        {o.motivoRechazo && <p>{o.motivoRechazo}</p>}
      </div>
    );
  if (o.polizaId) return <Badge variant="success">Autorizada</Badge>;
  if (!permitido) return <Badge variant="warning">Pendiente de autorización</Badge>;
  const mismaPersona = o.registradoPor === usuario;
  return (
    <div className="flex gap-2">
      <Button
        variant="outline"
        disabled={mismaPersona || guardar.isPending}
        title={mismaPersona ? 'Debe resolver otra persona' : undefined}
        onClick={() =>
          guardar.mutate({ ruta: `operaciones/${o.id}/autorizar`, version: o.version, body: {} })
        }
      >
        Autorizar
      </Button>
      <Button
        variant="outline"
        disabled={mismaPersona || guardar.isPending}
        title={mismaPersona ? 'Debe resolver otra persona' : undefined}
        onClick={() => setRechazar(true)}
      >
        Rechazar
      </Button>
      {guardar.error && <p role="alert">{guardar.error.message}</p>}
      <Dialog open={rechazar} onOpenChange={setRechazar}>
        <DialogContent size="sm">
          <DialogHeader>
            <DialogTitle>Rechazar solicitud</DialogTitle>
          </DialogHeader>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              guardar.mutate(
                { ruta: `operaciones/${o.id}/rechazar`, version: o.version, body: { motivo } },
                { onSuccess: () => setRechazar(false) },
              );
            }}
          >
            <DialogBody>
              <label>
                Motivo *
                <Input
                  required
                  maxLength={2000}
                  value={motivo}
                  onChange={(e) => setMotivo(e.target.value)}
                />
              </label>
              {guardar.error && <p role="alert">{guardar.error.message}</p>}
            </DialogBody>
            <DialogFooter>
              <Button type="button" variant="ghost" onClick={() => setRechazar(false)}>
                Cancelar
              </Button>
              <Button type="submit" disabled={!motivo.trim() || guardar.isPending}>
                Rechazar solicitud
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  );
}
