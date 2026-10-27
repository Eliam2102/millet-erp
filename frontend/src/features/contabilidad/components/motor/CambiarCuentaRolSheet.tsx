import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Textarea } from '@/components/ui/textarea';
import { useBodyScopedIdempotencyKey } from '@/lib/api';
import { useCambiarCuentaRol } from '../../api/motor';
import type { CambiarCuentaRolRequest, RolCuenta } from '../../api/motor-types';
import { MOTIVO_DESCARTE_MAXIMO, mensajeMotor } from '../../lib/motor';
import { CuentaSelector, type CuentaOpcion } from '../dimensiones/CuentaSelector';

interface Props {
  rol: RolCuenta | null;
  onClose: () => void;
}

/**
 * Cambiar la cuenta de un rol (C1.4-c). Manda If-Match con la versión del rol; el cambio queda en su bitácora y aplica
 * a las pólizas que el motor genere desde ahora (las ya generadas no se tocan).
 */
export function CambiarCuentaRolSheet({ rol, onClose }: Props) {
  return (
    <Sheet open={!!rol} onOpenChange={(o) => !o && onClose()}>
      <SheetContent size="form" side="right" data-print="hidden">
        {rol && <Formulario key={rol.id} rol={rol} onClose={onClose} />}
      </SheetContent>
    </Sheet>
  );
}

function Formulario({ rol, onClose }: { rol: RolCuenta; onClose: () => void }) {
  const cambiar = useCambiarCuentaRol();
  const keyFor = useBodyScopedIdempotencyKey();
  const [cuenta, setCuenta] = useState<CuentaOpcion | null>(null);
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState('');

  const misma = !!cuenta && cuenta.id === rol.cuentaId;
  const deshabilitado = !cuenta
    ? 'Elige la cuenta nueva'
    : misma
      ? 'El rol ya usa esa cuenta'
      : undefined;

  function guardar() {
    if (!cuenta || deshabilitado) return;
    setError('');
    const body: CambiarCuentaRolRequest = { cuentaId: cuenta.id, motivo: motivo.trim() || null };
    cambiar.mutate(
      { rol, body, idempotencyKey: keyFor({ rol: rol.id, ...body }) },
      {
        onSuccess: (r) => {
          toast.success(`El rol ${r.codigo} ahora usa la cuenta ${r.cuentaCodigo ?? cuenta.codigo}.`);
          onClose();
        },
        onError: (e) => setError(mensajeMotor(e)),
      },
    );
  }

  return (
    <>
      <SheetHeader>
        <SheetTitle>Cambiar cuenta del rol</SheetTitle>
        <SheetDescription className="text-xs text-ink-muted">
          <span className="font-mono">{rol.codigo}</span> · {rol.descripcion}. Aplica a las pólizas
          que se generen desde ahora; las ya generadas no cambian.
        </SheetDescription>
      </SheetHeader>
      <SheetBody className="flex flex-col gap-4">
        <div className="flex flex-col gap-1.5">
          <span className="text-xs text-ink-muted">Cuenta actual</span>
          <span className="text-sm">
            {rol.cuentaCodigo ? (
              <>
                <span className="font-mono text-xs">{rol.cuentaCodigo}</span> {rol.cuentaNombre}
              </>
            ) : (
              'Sin cuenta'
            )}
          </span>
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="rol-cuenta-nueva">
            Cuenta nueva<span aria-hidden="true"> *</span>
          </Label>
          <CuentaSelector id="rol-cuenta-nueva" value={cuenta} onChange={setCuenta} soloAfectables />
          <span className="text-xs text-ink-muted">Solo cuentas activas que reciben movimientos.</span>
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="rol-cuenta-motivo">Motivo</Label>
          <Textarea
            id="rol-cuenta-motivo"
            rows={3}
            className="resize-none"
            maxLength={MOTIVO_DESCARTE_MAXIMO}
            value={motivo}
            onChange={(e) => setMotivo(e.target.value)}
          />
          <span className="text-xs text-ink-muted">Opcional; queda en la bitácora del rol.</span>
        </div>
        {error && (
          <p role="alert" className="rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">
            {error}
          </p>
        )}
      </SheetBody>
      <SheetFooter>
        <Button variant="ghost" onClick={onClose} disabled={cambiar.isPending}>
          Cancelar
        </Button>
        <Button onClick={guardar} disabled={!!deshabilitado || cambiar.isPending} title={deshabilitado}>
          {cambiar.isPending ? 'Guardando…' : 'Cambiar cuenta'}
        </Button>
      </SheetFooter>
    </>
  );
}
