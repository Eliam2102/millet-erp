import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { FormCampo } from '@/components/erp/forms/FormGrid';
import { SheetFooter } from '@/components/ui/sheet';
import { SheetBody } from '@/components/ui/sheet';
import { useState } from 'react';
import { toast } from 'sonner';
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetDescription,
} from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Checkbox } from '@/components/ui/checkbox';
import { ClienteTesoreriaSelector } from './ClienteTesoreriaSelector';
import { BeneficiarioTipo } from '../api/types';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useSucursalesPermitidas } from '@/features/compras/ordenes/api/useSucursalesPermitidas';
import { CuentaBancariaSelector } from './CuentaBancariaSelector';
import { ConceptoSelector } from './ConceptoSelector';
import { useRegistrarMovimientoIngreso } from '../api/useTesoreria';
import { IngresoSchema, normalizarCuentaOrdenante } from '../lib/ingreso';
import { hoyLocalISO } from '@/lib/datetime';
import { useAuthStore } from '@/lib/auth/auth-store';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';

export function RegistrarIngresoSheet({
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
          <SheetTitle>Registrar ingreso</SheetTitle>
          <SheetDescription>
            Registra el depósito bancario en una sucursal que tengas asignada.
          </SheetDescription>
        </SheetHeader>
        {open && <IngresoForm cerrar={() => onOpenChange(false)} />}
      </SheetContent>
    </Sheet>
  );
}
function IngresoForm({ cerrar }: { cerrar: () => void }) {
  const [cuenta, setCuenta] = useState<string | null>(null);
  const [sucursal, setSucursal] = useState<string | null>(
    useAuthStore.getState().currentSucursalId,
  );
  // Sucursales de la sesión (mismos ids que la sucursal activa y que valida el
  // backend); el catálogo general usa otros ids y mostraba el GUID.
  const sucursales = useSucursalesPermitidas();
  const [concepto, setConcepto] = useState<string | null>(null);
  const [monto, setMonto] = useState('');
  const [fecha, setFecha] = useState(hoyLocalISO);
  const [referencia, setReferencia] = useState('');
  // C7.4 · CA7.9: depósito de cliente; sin cliente queda «sin identificar» para CxC.
  const [cobroCliente, setCobroCliente] = useState(false);
  const [cliente, setCliente] = useState<string | null>(null);
  // 86e3tfdve (opción A): cuenta del cliente que pagó; Facturación la usa como CtaOrdenante del REP.
  const [ordenante, setOrdenante] = useState('');
  const registrar = useRegistrarMovimientoIngreso();
  const key = useBodyScopedIdempotencyKey();
  const resultado = IngresoSchema.safeParse({
    cuentaBancariaId: cuenta,
    sucursalId: sucursal,
    monto: Number(monto),
    fechaValor: fecha,
    conceptoId: concepto ?? undefined,
    referenciaBancaria: referencia || undefined,
    beneficiarioTipo: cobroCliente ? BeneficiarioTipo.Cliente : undefined,
    beneficiarioRef: cobroCliente && cliente ? cliente : undefined,
    cuentaOrdenante: cobroCliente ? normalizarCuentaOrdenante(ordenante) : undefined,
  });
  const errorOrdenante =
    cobroCliente && !resultado.success
      ? resultado.error.issues.find((i) => i.path[0] === 'cuentaOrdenante')?.message
      : undefined;
  function guardar() {
    if (!resultado.success) return;
    registrar.mutate(
      { command: resultado.data, idempotencyKey: key(resultado.data) },
      {
        onSuccess: () => {
          toast.success('Ingreso registrado');
          cerrar();
        },
        onError: (error) =>
          toast.error('No se pudo registrar el ingreso', {
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
        <FormSeccion numero={1} titulo="Datos de la operación">
          <FormCampo span={6}>
            <div className="space-y-1">
              <Label>Sucursal *</Label>
              <Select value={sucursal ?? ''} onValueChange={(v) => setSucursal(v || null)}>
                <SelectTrigger aria-label="Sucursal del ingreso">
                  <SelectValue
                    placeholder={sucursales.isLoading ? 'Cargando sucursales…' : 'Selecciona sucursal'}
                  />
                </SelectTrigger>
                <SelectContent>
                  {(sucursales.data ?? []).map((s) => (
                    <SelectItem key={s.id} value={s.id}>
                      {s.clave} · {s.nombre}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </FormCampo>
          <FormCampo span={6}>
            <div className="space-y-1">
              <Label>Cuenta bancaria *</Label>
              <CuentaBancariaSelector paraIngresos value={cuenta} onChange={setCuenta} />
            </div>
          </FormCampo>
          <FormCampo span={12}>
            <ConceptoSelector value={concepto} onChange={setConcepto} />
          </FormCampo>
          <FormCampo span={6}>
            <div className="space-y-1">
              <Label htmlFor="ingreso-monto">Importe *</Label>
              <Input
                id="ingreso-monto"
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
              <Label htmlFor="ingreso-fecha">Fecha valor *</Label>
              <Input
                id="ingreso-fecha"
                type="date"
                value={fecha}
                onChange={(e) => setFecha(e.target.value)}
              />
            </div>
          </FormCampo>
          <FormCampo span={6}>
            <div className="space-y-1">
              <Label htmlFor="ingreso-referencia">Referencia bancaria</Label>
              <Input
                id="ingreso-referencia"
                maxLength={120}
                value={referencia}
                onChange={(e) => setReferencia(e.target.value)}
              />
            </div>
          </FormCampo>
          <FormCampo span={12}>
            <div className="space-y-2 rounded-md border border-line px-3 py-2">
              <div className="flex items-center gap-2">
                <Checkbox
                  id="ingreso-cobro-cliente"
                  checked={cobroCliente}
                  onCheckedChange={(v) => {
                    setCobroCliente(v === true);
                    if (v !== true) {
                      setCliente(null);
                      setOrdenante('');
                    }
                  }}
                />
                <Label htmlFor="ingreso-cobro-cliente">Cobro de cliente (cliente opcional)</Label>
              </div>
              {cobroCliente && (
                <>
                  <ClienteTesoreriaSelector
                    id="ingreso-cliente"
                    value={cliente}
                    onChange={(c) => setCliente(c?.id ?? null)}
                  />
                  <p className="text-2xs text-ink-muted">
                    Sin el detalle del cliente (remittance) el depósito queda «sin identificar» en
                    Cuentas por Cobrar y no se aplica hasta identificarlo.
                  </p>
                  <div className="space-y-1">
                    <Label htmlFor="ingreso-cuenta-ordenante">Cuenta ordenante del cliente</Label>
                    <Input
                      id="ingreso-cuenta-ordenante"
                      placeholder="Opcional"
                      maxLength={50}
                      autoComplete="off"
                      value={ordenante}
                      aria-invalid={!!errorOrdenante}
                      onChange={(e) => setOrdenante(e.target.value)}
                    />
                    <p className="text-2xs text-ink-muted">
                      Cuenta desde la que pagó el cliente. Se usa en el complemento de pago (REP) y
                      se muestra enmascarada.
                    </p>
                    {errorOrdenante && (
                      <p className="text-xs text-danger-fg">{errorOrdenante}</p>
                    )}
                  </div>
                </>
              )}
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
          title={
            !resultado.success
              ? (errorOrdenante ?? 'Selecciona sucursal, cuenta, fecha e importe positivo.')
              : undefined
          }
        >
          {registrar.isPending ? 'Registrando…' : 'Registrar ingreso'}
        </Button>
      </SheetFooter>
    </>
  );
}
