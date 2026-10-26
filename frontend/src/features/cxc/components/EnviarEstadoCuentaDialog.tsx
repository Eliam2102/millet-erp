import { useState } from 'react';
import { FormGrid, FormCampo } from '@/components/erp';
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { TextAreaField } from '@/components/erp/forms/TextAreaField';

const CORREO_VALIDO = /^[^@\s,;]+@[^@\s,;]+\.[^@\s,;]+$/;

/**
 * <c>&lt;EnviarEstadoCuentaDialog/&gt;</c> — F1-CXC-09 · C7.8: confirma el
 * destinatario (prellenado con el correo del maestro de clientes) y un
 * mensaje opcional. El caller genera el PDF, lo descarga y hace el POST.
 * Montar con <c>key</c> por apertura para reiniciar el formulario.
 */
export function EnviarEstadoCuentaDialog({
  open,
  onOpenChange,
  cliente,
  emailSugerido,
  isPending,
  onConfirm,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  cliente: string;
  emailSugerido: string | null;
  isPending: boolean;
  onConfirm: (datos: { destinatario: string; mensaje?: string }) => void;
}) {
  const [destinatario, setDestinatario] = useState(emailSugerido ?? '');
  const [mensaje, setMensaje] = useState('');
  const correoValido = CORREO_VALIDO.test(destinatario.trim());

  return (
    <Dialog open={open} onOpenChange={(next) => !isPending && onOpenChange(next)}>
      <DialogContent size="sm">
        <DialogHeader>
          <DialogTitle>Emitir y enviar estado de cuenta</DialogTitle>
          <DialogDescription>
            {cliente}: se genera el PDF del estado de cuenta, se descarga en tu equipo y se envía
            al destinatario. El envío queda registrado con el usuario, la fecha y los totales.
          </DialogDescription>
        </DialogHeader>
        <DialogBody>
          <FormGrid>
            <FormCampo span={12} className="space-y-1">
              <Label htmlFor="destinatario-estado-cuenta" className="text-xs">
                Destinatario *
              </Label>
              <Input
                id="destinatario-estado-cuenta"
                type="email"
                autoComplete="email"
                value={destinatario}
                maxLength={320}
                onChange={(e) => setDestinatario(e.target.value)}
                placeholder="cobranza@cliente.com"
                aria-invalid={destinatario.length > 0 && !correoValido}
              />
              {destinatario.length > 0 && !correoValido && (
                <p className="text-xs text-danger-fg">Captura un solo correo válido.</p>
              )}
            </FormCampo>
            <FormCampo span={12} className="space-y-1">
              <Label htmlFor="mensaje-estado-cuenta" className="text-xs">
                Mensaje (opcional)
              </Label>
              <TextAreaField
                value={mensaje}
                onChange={(v) => setMensaje(v ?? '')}
                maxLength={2000}
                textareaProps={{
                  id: 'mensaje-estado-cuenta',
                  placeholder: 'p. ej. Le compartimos su estado de cuenta al día de hoy.',
                }}
              />
            </FormCampo>
          </FormGrid>
        </DialogBody>
        <DialogFooter>
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpenChange(false)}
            disabled={isPending}
          >
            Cancelar
          </Button>
          <Button
            type="button"
            disabled={!correoValido || isPending}
            title={correoValido ? undefined : 'Captura el correo del destinatario.'}
            onClick={() =>
              onConfirm({
                destinatario: destinatario.trim(),
                mensaje: mensaje.trim() || undefined,
              })
            }
          >
            {isPending ? 'Enviando…' : 'Enviar'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
