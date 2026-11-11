import { useRegistrarConformidadProforma } from '@/features/facturacion/api/useProformas';
import type {
  MedioConformidadProforma,
  ProformaPedidoResponse,
} from '@/features/facturacion/api/types';
import { MEDIOS_CONFORMIDAD_PROFORMA } from '@/features/facturacion/lib/proforma-vigencia';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { mensajeErrorAccion } from '@/lib/api/error';
import { useState } from 'react';
import { toast } from 'sonner';

export interface ConformidadProformaDialogProps {
  pedidoId: string;
  proforma: ProformaPedidoResponse | null;
  onCerrar: () => void;
}

/**
 * M-234 · «Registrar conformidad»: el cliente aprobó UNA versión de la proforma. Se anota quién, cuándo
 * y por qué medio; no aprueba otras versiones y una proforma vencida no se puede aprobar (el servidor
 * responde con el motivo y se muestra aquí).
 */
export function ConformidadProformaDialog({ pedidoId, proforma, onCerrar }: ConformidadProformaDialogProps) {
  const registrar = useRegistrarConformidadProforma();
  const [quien, setQuien] = useState('');
  const [medio, setMedio] = useState<MedioConformidadProforma>('Correo');
  const [fecha, setFecha] = useState('');
  const [notas, setNotas] = useState('');
  const [error, setError] = useState<string | null>(null);

  function cerrar() {
    setQuien('');
    setMedio('Correo');
    setFecha('');
    setNotas('');
    setError(null);
    registrar.reset();
    onCerrar();
  }

  function enviar() {
    if (proforma == null) return;
    setError(null);
    registrar.mutate(
      {
        pedidoId,
        proformaId: proforma.id,
        body: {
          quien: quien.trim(),
          medio,
          ...(fecha ? { fecha } : {}),
          ...(notas.trim() ? { notas: notas.trim() } : {}),
        },
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success(`Conformidad registrada en la proforma ${proforma.folio}.`);
          cerrar();
        },
        onError: (e) => setError(mensajeErrorAccion(e)),
      },
    );
  }

  return (
    <Dialog open={proforma != null} onOpenChange={(abierto) => !abierto && cerrar()}>
      <DialogContent size="md" closeLabel="Cerrar" className="rounded-2xl shadow-dialog">
        {proforma != null && (
          <>
            <DialogHeader>
              <DialogTitle className="text-xl">
                Registrar conformidad · <span className="font-mono">{proforma.folio}</span>
              </DialogTitle>
              <DialogDescription className="text-xs text-ink-muted">
                Aplica solo a esta versión de la proforma.
              </DialogDescription>
            </DialogHeader>
            <DialogBody className="space-y-3">
              <div className="space-y-1.5">
                <Label htmlFor="conformidad-quien">Quién dio la conformidad</Label>
                <Input
                  id="conformidad-quien"
                  value={quien}
                  maxLength={200}
                  placeholder="Nombre de la persona del cliente"
                  onChange={(e) => setQuien(e.target.value)}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="conformidad-medio">Medio</Label>
                <Select value={medio} onValueChange={(v) => setMedio(v as MedioConformidadProforma)}>
                  <SelectTrigger id="conformidad-medio" aria-label="Medio" className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {MEDIOS_CONFORMIDAD_PROFORMA.map((m) => (
                      <SelectItem key={m.value} value={m.value}>
                        {m.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="conformidad-fecha">Fecha (vacía = hoy)</Label>
                <Input
                  id="conformidad-fecha"
                  type="date"
                  value={fecha}
                  onChange={(e) => setFecha(e.target.value)}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="conformidad-notas">Notas (opcional)</Label>
                <Input
                  id="conformidad-notas"
                  value={notas}
                  maxLength={500}
                  onChange={(e) => setNotas(e.target.value)}
                />
              </div>
              {error ? (
                <p role="alert" className="text-sm text-danger-fg">
                  {error}
                </p>
              ) : null}
            </DialogBody>
            <DialogFooter className="border-t border-line pt-4">
              <Button type="button" variant="ghost" onClick={cerrar}>
                Cancelar
              </Button>
              <Button type="button" disabled={registrar.isPending || quien.trim() === ''} onClick={enviar}>
                {registrar.isPending ? 'Registrando…' : 'Registrar conformidad'}
              </Button>
            </DialogFooter>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}
