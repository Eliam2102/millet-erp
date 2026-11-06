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
  const [retirar, setRetirar] = useState(false);
  const [motivo, setMotivo] = useState('');
  if (o.anulada)
    return (
      <div>
        <Badge variant="neutral">
          {o.motivoRetiro ? 'Retirada' : o.motivoRechazo ? 'Rechazada' : 'Anulada'}
        </Badge>
        {o.motivoRetiro && <p>Retirada: {o.motivoRetiro}</p>}
        {o.motivoRechazo && <p>Rechazada: {o.motivoRechazo}</p>}
      </div>
    );
  if (o.polizaId) return <Badge variant="success">Autorizada</Badge>;
  const mismaPersona = o.registradoPor === usuario;
  const puedeRetirar = mismaPersona && (o.tipo === 'Baja' || o.tipo === 'Venta');
  if (!permitido && !puedeRetirar)
    return <Badge variant="warning">Pendiente de autorización</Badge>;
  return (
    <div className="flex gap-2">
      {permitido && (
        <>
          <Button
            variant="outline"
            disabled={mismaPersona || guardar.isPending}
            title={mismaPersona ? 'Debe resolver otra persona' : undefined}
            onClick={() =>
              guardar.mutate({
                ruta: `operaciones/${o.id}/autorizar`,
                version: o.version,
                body: {},
              })
            }
          >
            Autorizar
          </Button>
          <Button
            variant="outline"
            disabled={mismaPersona || guardar.isPending}
            title={mismaPersona ? 'Debe resolver otra persona' : undefined}
            onClick={() => {
              setRetirar(false);
              setRechazar(true);
            }}
          >
            Rechazar
          </Button>
        </>
      )}
      {puedeRetirar && (
        <Button
          variant="outline"
          disabled={guardar.isPending}
          onClick={() => {
            setRetirar(true);
            setRechazar(true);
          }}
        >
          Retirar
        </Button>
      )}
      {guardar.error && <p role="alert">{guardar.error.message}</p>}
      <Dialog open={rechazar} onOpenChange={setRechazar}>
        <DialogContent size="sm">
          <DialogHeader>
            <DialogTitle>{retirar ? 'Retirar solicitud' : 'Rechazar solicitud'}</DialogTitle>
          </DialogHeader>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              guardar.mutate(
                {
                  ruta: `operaciones/${o.id}/${retirar ? 'retirar' : 'rechazar'}`,
                  version: o.version,
                  body: { motivo },
                },
                { onSuccess: () => setRechazar(false) },
              );
            }}
          >
            <DialogBody>
              <label>
                Motivo *
                <Input
                  required
                  minLength={retirar ? 5 : 1}
                  maxLength={retirar ? 500 : 2000}
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
              <Button
                type="submit"
                disabled={motivo.trim().length < (retirar ? 5 : 1) || guardar.isPending}
              >
                {retirar ? 'Retirar solicitud' : 'Rechazar solicitud'}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  );
}
