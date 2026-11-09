import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { FormCampo } from '@/components/erp/forms/FormGrid';
import { useState } from 'react';
import { toast } from 'sonner';
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { CuentaBancariaSelector } from './CuentaBancariaSelector';
import { useCuentasBancarias, useRegistrarTraspaso } from '../api/useTesoreria';
import { TraspasoSchema } from '../lib/traspaso';
import { hoyLocalISO } from '@/lib/datetime';
import { useAuthStore } from '@/lib/auth/auth-store';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';

/** M-203: «Nuevo traspaso» entre cuentas propias (egreso en origen + ingreso en destino, ligados). */
export function NuevoTraspasoSheet({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (value: boolean) => void;
}) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent size="lg">
        <SheetHeader>
          <SheetTitle>Nuevo traspaso</SheetTitle>
          <SheetDescription>
            Mueve dinero entre dos cuentas propias en la misma moneda. No cuenta como ingreso ni
            egreso en el flujo de efectivo; cada lado se concilia con el extracto de su banco.
          </SheetDescription>
        </SheetHeader>
        {open && <TraspasoForm cerrar={() => onOpenChange(false)} />}
      </SheetContent>
    </Sheet>
  );
}

function TraspasoForm({ cerrar }: { cerrar: () => void }) {
  const [origen, setOrigen] = useState<string | null>(null);
  const [destino, setDestino] = useState<string | null>(null);
  const [monto, setMonto] = useState('');
  const [fecha, setFecha] = useState(hoyLocalISO);
  const [referencia, setReferencia] = useState('');
  const [motivo, setMotivo] = useState('');
  const cuentas = useCuentasBancarias(true);
  const registrar = useRegistrarTraspaso();
  const key = useBodyScopedIdempotencyKey();
  const monedaOrigen = cuentas.data?.find((c) => c.id === origen)?.moneda ?? null;
  const resultado = TraspasoSchema.safeParse({
    cuentaOrigenId: origen,
    cuentaDestinoId: destino,
    monto: Number(monto),
    fechaValor: fecha,
    referencia,
    motivo: motivo || undefined,
    sucursalId: useAuthStore.getState().currentSucursalId ?? undefined,
  });
  const mismaCuenta = origen !== null && origen === destino;

  function guardar() {
    if (!resultado.success) return;
    registrar.mutate(
      { command: resultado.data, idempotencyKey: key(resultado.data) },
      {
        onSuccess: () => {
          toast.success('Traspaso registrado');
          cerrar();
        },
        onError: (error) =>
          toast.error('No se pudo registrar el traspaso', {
            description: esApiError(error)
              ? (error.problem.detail ?? error.problem.title)
              : 'Vuelve a intentarlo.',
          }),
      },
    );
  }

  return (
    <>
      <SheetBody className="space-y-4">
        <FormSeccion numero={1} titulo="Datos del traspaso">
          <FormCampo span={6}>
            <div className="space-y-1">
              <Label>Cuenta origen *</Label>
              <CuentaBancariaSelector
                value={origen}
                onChange={(id) => {
                  setOrigen(id);
                  setDestino(null);
                }}
              />
            </div>
          </FormCampo>
          <FormCampo span={6}>
            <div className="space-y-1">
              <Label>Cuenta destino *</Label>
              <CuentaBancariaSelector
                value={destino}
                onChange={setDestino}
                moneda={monedaOrigen}
                disabled={!origen}
              />
              {mismaCuenta && (
                <p role="alert" className="text-xs text-danger-fg">
                  La cuenta origen y la destino deben ser distintas.
                </p>
              )}
            </div>
          </FormCampo>
          <FormCampo span={6}>
            <div className="space-y-1">
              <Label htmlFor="traspaso-monto">Importe *</Label>
              <Input
                id="traspaso-monto"
                type="number"
                min="0.01"
                step="0.01"
                value={monto}
                onChange={(e) => setMonto(e.target.value)}
              />
            </div>
          </FormCampo>
          <FormCampo span={6}>
            <div className="space-y-1">
              <Label htmlFor="traspaso-fecha">Fecha valor *</Label>
              <Input
                id="traspaso-fecha"
                type="date"
                value={fecha}
                onChange={(e) => setFecha(e.target.value)}
              />
            </div>
          </FormCampo>
          <FormCampo span={6}>
            <div className="space-y-1">
              <Label htmlFor="traspaso-referencia">Referencia *</Label>
              <Input
                id="traspaso-referencia"
                maxLength={120}
                value={referencia}
                onChange={(e) => setReferencia(e.target.value)}
              />
            </div>
          </FormCampo>
          <FormCampo span={12}>
            <div className="space-y-1">
              <Label htmlFor="traspaso-motivo">Motivo</Label>
              <Input
                id="traspaso-motivo"
                maxLength={400}
                value={motivo}
                onChange={(e) => setMotivo(e.target.value)}
              />
            </div>
          </FormCampo>
        </FormSeccion>
      </SheetBody>
      <SheetFooter>
        <Button variant="ghost" disabled={registrar.isPending} onClick={cerrar}>
          Cancelar
        </Button>
        <Button
          onClick={guardar}
          disabled={!resultado.success || registrar.isPending}
          title={!resultado.success ? 'Selecciona las dos cuentas, importe, fecha y referencia.' : undefined}
        >
          {registrar.isPending ? 'Registrando…' : 'Registrar traspaso'}
        </Button>
      </SheetFooter>
    </>
  );
}
