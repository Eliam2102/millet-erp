import { useState } from 'react';
import { Inbox, Plus } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { AvisoEnLinea, EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { FormCampo } from '@/components/erp/forms/FormGrid';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { hoyLocalISO } from '@/lib/datetime';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { CuentaBancariaSelector } from '../components/CuentaBancariaSelector';
import { ClienteTesoreriaSelector } from '../components/ClienteTesoreriaSelector';
import {
  ESTADO_CHEQUE_LABELS,
  EstadoChequeRecibido,
  useChequesRecibidos,
  useConfirmarCheque,
  useLineasCandidatasCheque,
  useRebotarCheque,
  useRegistrarCheque,
  type ChequeRecibidoResponse,
} from '../api/useChequesRecibidos';

const TONO: Record<EstadoChequeRecibido, 'warning' | 'success' | 'danger'> = {
  [EstadoChequeRecibido.EnFirmePendiente]: 'warning',
  [EstadoChequeRecibido.Disponible]: 'success',
  [EstadoChequeRecibido.Rebotado]: 'danger',
};

const FILTROS: { label: string; estado?: EstadoChequeRecibido }[] = [
  { label: 'En firme pendiente', estado: EstadoChequeRecibido.EnFirmePendiente },
  { label: 'Disponibles', estado: EstadoChequeRecibido.Disponible },
  { label: 'Rebotados', estado: EstadoChequeRecibido.Rebotado },
  { label: 'Todos' },
];

function mensaje(e: unknown, fallback: string) {
  return esApiError(e) ? (e.problem.detail ?? e.problem.title) : fallback;
}

/**
 * M-372 · Cheques recibidos: el cheque nace «en firme pendiente» y no suma al saldo. Se confirma contra el abono del
 * estado de cuenta (queda conciliado) o a mano con la referencia del banco. Si rebota, el ingreso se revierte con su
 * contramovimiento y CxC recibe la alerta.
 */
export function BandejaChequesRecibidos() {
  const [estado, setEstado] = useState<EstadoChequeRecibido | undefined>(EstadoChequeRecibido.EnFirmePendiente);
  const query = useChequesRecibidos(estado);
  const puede = useHasPermission(PermisosCanonicos.TesoreriaMovimientosRegistrar);
  const [nuevo, setNuevo] = useState(false);
  const [aConfirmar, setAConfirmar] = useState<ChequeRecibidoResponse | null>(null);
  const [aRebotar, setARebotar] = useState<ChequeRecibidoResponse | null>(null);
  const items = query.data ?? [];

  return (
    <div className="flex h-full min-h-0 flex-col gap-5 text-sm text-ink">
      <PageHeader
        titulo="Cheques recibidos"
        descripcion="Un cheque no suma al saldo hasta que el banco lo acredita. Confírmalo contra el estado de cuenta o con la referencia del banco; si rebota, se revierte y CxC recibe el aviso."
        acciones={
          puede && (
            <Button onClick={() => setNuevo(true)}>
              <Plus className="h-4 w-4" /> Registrar cheque
            </Button>
          )
        }
      />
      <div className="flex flex-wrap gap-2" role="group" aria-label="Filtrar por estado">
        {FILTROS.map((f) => (
          <Button
            key={f.label}
            size="sm"
            variant={estado === f.estado ? 'default' : 'outline'}
            aria-pressed={estado === f.estado}
            onClick={() => setEstado(f.estado)}
          >
            {f.label}
          </Button>
        ))}
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar los cheques"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton rows={5} columns={[{ width: 'w-24' }, { width: 'w-40' }, { width: 'w-28' }, { width: 'w-24' }, { width: 'w-28' }]} />
      ) : items.length === 0 ? (
        <EmptyState icon={<Inbox className="h-10 w-10" />} title="Sin cheques" description="Los cheques que registres aparecen aquí." />
      ) : (
        <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
          <TableContainer>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Recibido</TableHead>
                  <TableHead>Cliente</TableHead>
                  <TableHead>Cheque</TableHead>
                  <TableHead>Librador</TableHead>
                  <TableHead>Depositado en</TableHead>
                  <TableHead className="text-right">Importe</TableHead>
                  <TableHead>Estado</TableHead>
                  <TableHead className="text-right">Acciones</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.map((c) => (
                  <TableRow key={c.id} className="hover:bg-surface-subtle">
                    <TableCell>{c.fechaRecepcion}</TableCell>
                    <TableCell>{c.clienteNombre ?? '—'}</TableCell>
                    <TableCell>
                      {c.bancoEmisor} · {c.numero}
                    </TableCell>
                    <TableCell>{c.librador}</TableCell>
                    <TableCell>{c.cuenta}</TableCell>
                    <TableCell className="text-right">
                      <MoneyDisplay amount={c.importe} currency={c.moneda} strong />
                    </TableCell>
                    <TableCell>
                      <Badge variant={TONO[c.estado]}>{ESTADO_CHEQUE_LABELS[c.estado]}</Badge>
                      {c.estado === EstadoChequeRecibido.Disponible && c.extractoLineaId && (
                        <span className="block text-xs text-ink-muted">Conciliado con el estado de cuenta</span>
                      )}
                      {c.motivoRebote && (
                        <span className="block max-w-48 truncate text-xs text-ink-muted" title={c.motivoRebote}>
                          {c.motivoRebote}
                        </span>
                      )}
                    </TableCell>
                    <TableCell className="text-right">
                      {puede && c.estado !== EstadoChequeRecibido.Rebotado && (
                        <div className="flex justify-end gap-2">
                          {c.estado === EstadoChequeRecibido.EnFirmePendiente && (
                            <Button size="sm" onClick={() => setAConfirmar(c)}>
                              Confirmar
                            </Button>
                          )}
                          <Button size="sm" variant="outline" onClick={() => setARebotar(c)}>
                            Rebotó
                          </Button>
                        </div>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        </div>
      )}

      <Sheet open={nuevo} onOpenChange={setNuevo}>
        <SheetContent size="lg">
          <SheetHeader>
            <SheetTitle>Registrar cheque recibido</SheetTitle>
            <SheetDescription>Queda en firme pendiente: no suma al saldo hasta que el banco lo acredite.</SheetDescription>
          </SheetHeader>
          {nuevo && <RegistrarChequeForm cerrar={() => setNuevo(false)} />}
        </SheetContent>
      </Sheet>
      {aConfirmar && <ConfirmarChequeDialog cheque={aConfirmar} cerrar={() => setAConfirmar(null)} />}
      {aRebotar && <RebotarChequeDialog cheque={aRebotar} cerrar={() => setARebotar(null)} />}
    </div>
  );
}

function RegistrarChequeForm({ cerrar }: { cerrar: () => void }) {
  const [cuenta, setCuenta] = useState<string | null>(null);
  const [cliente, setCliente] = useState<string | null>(null);
  const [banco, setBanco] = useState('');
  const [numero, setNumero] = useState('');
  const [librador, setLibrador] = useState('');
  const [importe, setImporte] = useState('');
  const [fecha, setFecha] = useState(hoyLocalISO);
  const registrar = useRegistrarCheque();
  const key = useBodyScopedIdempotencyKey();
  const monto = Number(importe);
  const completo =
    cuenta && cliente && banco.trim() && numero.trim() && librador.trim() && monto > 0 && Number.isFinite(monto) && fecha;

  function guardar() {
    if (!completo) return;
    const command = {
      cuentaBancariaId: cuenta,
      clienteId: cliente,
      bancoEmisor: banco.trim(),
      numero: numero.trim(),
      librador: librador.trim(),
      importe: Math.round(monto * 100) / 100,
      fechaRecepcion: fecha,
    };
    registrar.mutate(
      { command, idempotencyKey: key(command) },
      {
        onSuccess: () => {
          toast.success('Cheque registrado en firme pendiente');
          cerrar();
        },
        onError: (e) => toast.error('No se pudo registrar el cheque', { description: mensaje(e, 'Vuelve a intentarlo.') }),
      },
    );
  }

  return (
    <>
      <SheetBody className="space-y-4">
        <FormSeccion numero={1} titulo="Datos del cheque">
          <FormCampo span={6}>
            <div className="space-y-1">
              <Label htmlFor="cheque-cliente">Cliente *</Label>
              <ClienteTesoreriaSelector id="cheque-cliente" value={cliente} onChange={(c) => setCliente(c?.id ?? null)} />
            </div>
          </FormCampo>
          <FormCampo span={6}>
            <div className="space-y-1">
              <Label>Depositado en *</Label>
              <CuentaBancariaSelector value={cuenta} onChange={setCuenta} paraIngresos />
            </div>
          </FormCampo>
          <FormCampo span={6}>
            <div className="space-y-1">
              <Label htmlFor="cheque-banco">Banco del cheque *</Label>
              <Input id="cheque-banco" maxLength={80} value={banco} onChange={(e) => setBanco(e.target.value)} />
            </div>
          </FormCampo>
          <FormCampo span={6}>
            <div className="space-y-1">
              <Label htmlFor="cheque-numero">Número *</Label>
              <Input id="cheque-numero" maxLength={30} value={numero} onChange={(e) => setNumero(e.target.value)} />
            </div>
          </FormCampo>
          <FormCampo span={12}>
            <div className="space-y-1">
              <Label htmlFor="cheque-librador">Librador *</Label>
              <Input id="cheque-librador" maxLength={200} value={librador} onChange={(e) => setLibrador(e.target.value)} />
            </div>
          </FormCampo>
          <FormCampo span={6}>
            <div className="space-y-1">
              <Label htmlFor="cheque-importe">Importe *</Label>
              <Input id="cheque-importe" type="number" min="0.01" step="0.01" value={importe} onChange={(e) => setImporte(e.target.value)} />
            </div>
          </FormCampo>
          <FormCampo span={6}>
            <div className="space-y-1">
              <Label htmlFor="cheque-fecha">Fecha de recepción *</Label>
              <Input id="cheque-fecha" type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} />
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
          disabled={!completo || registrar.isPending}
          title={!completo ? 'Captura cliente, cuenta, banco, número, librador, importe y fecha.' : undefined}
        >
          {registrar.isPending ? 'Registrando…' : 'Registrar cheque'}
        </Button>
      </SheetFooter>
    </>
  );
}

function ConfirmarChequeDialog({ cheque, cerrar }: { cheque: ChequeRecibidoResponse; cerrar: () => void }) {
  const lineas = useLineasCandidatasCheque(cheque.id);
  const confirmar = useConfirmarCheque();
  const key = useBodyScopedIdempotencyKey();
  const [linea, setLinea] = useState<string | null>(null);
  const [aMano, setAMano] = useState(false);
  const [fecha, setFecha] = useState(hoyLocalISO);
  const [referencia, setReferencia] = useState('');
  const [error, setError] = useState<string | null>(null);
  const listo = aMano ? referencia.trim().length > 0 && fecha : linea != null;

  function ejecutar() {
    setError(null);
    const args = aMano
      ? { id: cheque.id, fechaValor: fecha, referencia: referencia.trim() }
      : { id: cheque.id, extractoLineaId: linea! };
    confirmar.mutate(
      { ...args, idempotencyKey: key(args) },
      {
        onSuccess: () => {
          toast.success('Cheque disponible');
          cerrar();
        },
        onError: (e) => setError(mensaje(e, 'No se pudo confirmar el cheque.')),
      },
    );
  }

  const candidatas = lineas.data ?? [];
  return (
    <Dialog open onOpenChange={(o) => !o && cerrar()}>
      <DialogContent size="md">
        <DialogHeader>
          <DialogTitle>Confirmar cheque {cheque.numero}</DialogTitle>
          <DialogDescription>
            Al confirmarlo nace el ingreso por <MoneyDisplay amount={cheque.importe} currency={cheque.moneda} strong /> y
            suma al saldo de {cheque.cuenta}.
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-4">
          {error && <AvisoEnLinea tono="error">{error}</AvisoEnLinea>}
          <div className="flex gap-2" role="group" aria-label="Cómo confirmar">
            <Button size="sm" variant={!aMano ? 'default' : 'outline'} aria-pressed={!aMano} onClick={() => setAMano(false)}>
              Del estado de cuenta
            </Button>
            <Button size="sm" variant={aMano ? 'default' : 'outline'} aria-pressed={aMano} onClick={() => setAMano(true)}>
              A mano con referencia
            </Button>
          </div>
          {!aMano ? (
            lineas.isLoading ? (
              <p className="text-ink-muted">Buscando abonos…</p>
            ) : candidatas.length === 0 ? (
              <p className="text-ink-muted">
                No hay abonos sin emparejar por este importe en una conciliación abierta de la cuenta. Carga el estado de
                cuenta o confírmalo a mano.
              </p>
            ) : (
              <ul className="space-y-2" aria-label="Abonos del estado de cuenta">
                {candidatas.map((l) => (
                  <li key={l.extractoLineaId}>
                    <label className="flex cursor-pointer items-center gap-3 rounded-md border border-line-row p-2 hover:bg-surface-subtle">
                      <input
                        type="radio"
                        name="linea-cheque"
                        checked={linea === l.extractoLineaId}
                        onChange={() => setLinea(l.extractoLineaId)}
                      />
                      <span className="w-24">{l.fecha}</span>
                      <span className="flex-1 truncate">
                        {l.descripcion}
                        {l.referencia ? ` · ${l.referencia}` : ''}
                      </span>
                      <MoneyDisplay amount={l.monto} currency={cheque.moneda} />
                    </label>
                  </li>
                ))}
              </ul>
            )
          ) : (
            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1">
                <Label htmlFor="cheque-fecha-valor">Fecha en que lo acreditó *</Label>
                <Input id="cheque-fecha-valor" type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} />
              </div>
              <div className="space-y-1">
                <Label htmlFor="cheque-referencia">Referencia del banco *</Label>
                <Input id="cheque-referencia" maxLength={120} value={referencia} onChange={(e) => setReferencia(e.target.value)} />
              </div>
            </div>
          )}
        </DialogBody>
        <DialogFooter>
          <Button variant="ghost" onClick={cerrar} disabled={confirmar.isPending}>
            Cancelar
          </Button>
          <Button onClick={ejecutar} disabled={!listo || confirmar.isPending}>
            {confirmar.isPending ? 'Confirmando…' : 'Confirmar cheque'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function RebotarChequeDialog({ cheque, cerrar }: { cheque: ChequeRecibidoResponse; cerrar: () => void }) {
  const rebotar = useRebotarCheque();
  const key = useBodyScopedIdempotencyKey();
  const [fecha, setFecha] = useState(hoyLocalISO);
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<string | null>(null);
  const disponible = cheque.estado === EstadoChequeRecibido.Disponible;

  function ejecutar() {
    setError(null);
    const args = { id: cheque.id, fecha, motivo: motivo.trim() };
    rebotar.mutate(
      { ...args, idempotencyKey: key(args) },
      {
        onSuccess: () => {
          toast.success('Cheque marcado como rebotado; CxC recibe el aviso');
          cerrar();
        },
        onError: (e) => setError(mensaje(e, 'No se pudo registrar el rebote.')),
      },
    );
  }

  return (
    <Dialog open onOpenChange={(o) => !o && cerrar()}>
      <DialogContent size="sm">
        <DialogHeader>
          <DialogTitle>Cheque {cheque.numero} rebotado</DialogTitle>
          <DialogDescription>
            {disponible
              ? 'El ingreso ya se había acreditado: se revierte con un contramovimiento por el importe del cheque.'
              : 'El cheque no se había acreditado: no hay movimiento que revertir.'}{' '}
            CxC recibe la alerta para cobrarlo por otra vía.
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-4">
          {error && <AvisoEnLinea tono="error">{error}</AvisoEnLinea>}
          <div className="space-y-1">
            <Label htmlFor="rebote-fecha">Fecha de la devolución *</Label>
            <Input id="rebote-fecha" type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} />
          </div>
          <div className="space-y-1">
            <Label htmlFor="rebote-motivo">Motivo *</Label>
            <Textarea id="rebote-motivo" rows={3} maxLength={300} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
          </div>
        </DialogBody>
        <DialogFooter>
          <Button variant="ghost" onClick={cerrar} disabled={rebotar.isPending}>
            Cancelar
          </Button>
          <Button variant="destructive" onClick={ejecutar} disabled={!motivo.trim() || !fecha || rebotar.isPending}>
            {rebotar.isPending ? 'Registrando…' : 'Registrar rebote'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
