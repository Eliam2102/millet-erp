import { Fragment, useMemo, useState } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
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
import { Badge } from '@/components/ui/badge';
import { CuentaBancariaSelector } from '@/features/tesoreria/components/CuentaBancariaSelector';
import { useCrearCorrida, useCuentasBancarias, usePasivosParaCorrida } from '@/features/tesoreria/api/useTesoreria';
import { totalesSeleccion } from '@/features/tesoreria/lib/corridas';
import { formatoFecha } from '@/features/tesoreria/lib/formato';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';

/**
 * <c>Nueva corrida</c> (T9.1-A): una cuenta bancaria y una moneda por corrida; lista de pasivos autorizados y no
 * bloqueados con filtro de vencimiento; bruto, retenciones y neto por proveedor y del total elegido.
 */
export function NuevaCorrida() {
  const navigate = useNavigate();
  const keyFor = useBodyScopedIdempotencyKey();
  const crear = useCrearCorrida();
  const cuentas = useCuentasBancarias(true);
  const [cuentaId, setCuentaId] = useState<string | null>(null);
  const [venceHasta, setVenceHasta] = useState('');
  const [nombre, setNombre] = useState('');
  const [elegidos, setElegidos] = useState<ReadonlySet<string>>(new Set());
  const [aviso, setAviso] = useState<string | null>(null);

  const moneda = cuentas.data?.find((c) => c.id === cuentaId)?.moneda ?? '';
  const candidatos = usePasivosParaCorrida({ moneda, venceHasta: venceHasta || undefined }, cuentaId != null);
  const pasivos = useMemo(() => candidatos.data?.items ?? [], [candidatos.data]);
  const totales = totalesSeleccion(pasivos, elegidos);

  const porProveedor = useMemo(() => {
    const mapa = new Map<string, typeof pasivos>();
    for (const p of pasivos) mapa.set(p.proveedorId, [...(mapa.get(p.proveedorId) ?? []), p]);
    return [...mapa.values()];
  }, [pasivos]);

  function alternar(ids: string[], marcar: boolean) {
    const siguiente = new Set(elegidos);
    for (const id of ids) {
      if (marcar) siguiente.add(id);
      else siguiente.delete(id);
    }
    setElegidos(siguiente);
  }

  function guardar() {
    if (cuentaId == null) return;
    setAviso(null);
    const body = { cuentaBancariaId: cuentaId, facturas: [...elegidos].sort(), nombre: nombre.trim() };
    crear.mutate(
      { ...body, idempotencyKey: keyFor(body) },
      {
        onSuccess: (c) => {
          toast.success('Corrida creada en borrador');
          navigate({ to: '/tesoreria/corridas/$id', params: { id: c.id } });
        },
        onError: (e) =>
          esApiError(e) ? setAviso(e.problem.detail ?? e.problem.title) : toast.error('No se pudo crear la corrida.'),
      },
    );
  }

  return (
    <div className="space-y-5 text-sm text-ink">
      <PageHeader
        titulo="Nueva corrida de pagos"
        descripcion="Elige la cuenta bancaria y los pasivos autorizados por Cuentas por Pagar. Un pasivo solo puede estar en una corrida vigente."
      />

      {aviso && (
        <AvisoEnLinea tono="error" titulo="No se pudo crear la corrida">
          {aviso}
        </AvisoEnLinea>
      )}

      <section className="grid gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat md:grid-cols-3">
        <div className="space-y-1">
          <span className="text-xs text-ink-muted">Cuenta bancaria (define la moneda)</span>
          <CuentaBancariaSelector
            value={cuentaId}
            onChange={(id) => {
              setCuentaId(id);
              setElegidos(new Set());
            }}
            ocultarSaldo
          />
        </div>
        <div className="space-y-1">
          <label htmlFor="vence-hasta" className="text-xs text-ink-muted">
            Vence a más tardar el
          </label>
          <Input id="vence-hasta" type="date" value={venceHasta} onChange={(e) => setVenceHasta(e.target.value)} />
        </div>
        <div className="space-y-1">
          <label htmlFor="nombre-corrida" className="text-xs text-ink-muted">
            Nombre (opcional)
          </label>
          <Input id="nombre-corrida" value={nombre} maxLength={120} onChange={(e) => setNombre(e.target.value)} placeholder="Se genera si lo dejas vacío" />
        </div>
      </section>

      {cuentaId == null ? (
        <EmptyState title="Elige una cuenta bancaria" description="La moneda de la cuenta define qué pasivos puedes incluir." />
      ) : candidatos.isError ? (
        <ErrorState
          title="No se pudieron cargar los pasivos"
          problem={esApiError(candidatos.error) ? candidatos.error.problem : undefined}
          onRetry={() => candidatos.refetch()}
        />
      ) : candidatos.isLoading ? (
        <TableSkeleton rows={6} columns={[{ width: 'w-8' }, { width: 'w-56' }, { width: 'w-28' }, { width: 'w-28' }, { width: 'w-28' }]} />
      ) : pasivos.length === 0 ? (
        <EmptyState title="Sin pasivos disponibles" description="No hay pasivos autorizados y libres en esta moneda con ese vencimiento." />
      ) : (
        <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
          <TableContainer>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-10"><span className="sr-only">Elegir</span></TableHead>
                  <TableHead>Proveedor y factura</TableHead>
                  <TableHead>Vence</TableHead>
                  <TableHead className="text-right">Bruto</TableHead>
                  <TableHead className="text-right">Retenciones</TableHead>
                  <TableHead className="text-right">Neto a transferir</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {porProveedor.map((grupo) => {
                  const ids = grupo.map((p) => p.facturaProveedorId);
                  const todos = ids.every((id) => elegidos.has(id));
                  const algunos = ids.some((id) => elegidos.has(id));
                  const sub = totalesSeleccion(grupo, elegidos);
                  const sinCuenta = !grupo[0].cuentaValidada;
                  const nombreProv = grupo[0].proveedorRazonSocial ?? 'Proveedor sin nombre';
                  return (
                    <Fragment key={grupo[0].proveedorId}>
                      <TableRow className="bg-surface-subtle">
                        <TableCell>
                          <Checkbox
                            aria-label={`Elegir todas las facturas de ${nombreProv}`}
                            checked={todos ? true : algunos ? 'indeterminate' : false}
                            disabled={sinCuenta}
                            onCheckedChange={(v) => alternar(ids, v === true)}
                          />
                        </TableCell>
                        <TableCell className="font-medium">
                          {nombreProv}
                          {sinCuenta && (
                            <Badge variant="warning" className="ml-2">
                              Sin cuenta bancaria validada
                            </Badge>
                          )}
                        </TableCell>
                        <TableCell />
                        <TableCell className="text-right"><MoneyDisplay amount={sub.bruto} currency={moneda} /></TableCell>
                        <TableCell className="text-right"><MoneyDisplay amount={sub.retenciones} currency={moneda} /></TableCell>
                        <TableCell className="text-right"><MoneyDisplay amount={sub.neto} currency={moneda} strong /></TableCell>
                      </TableRow>
                      {grupo.map((p) => (
                        <TableRow key={p.facturaProveedorId}>
                          <TableCell>
                            <Checkbox
                              aria-label={`Elegir factura ${p.folioProveedor ?? p.facturaProveedorId}`}
                              checked={elegidos.has(p.facturaProveedorId)}
                              disabled={sinCuenta}
                              onCheckedChange={(v) => alternar([p.facturaProveedorId], v === true)}
                            />
                          </TableCell>
                          <TableCell className="pl-6 font-mono text-xs">{p.folioProveedor ?? p.facturaProveedorId}</TableCell>
                          <TableCell className="text-xs">{formatoFecha(p.fechaVencimiento)}</TableCell>
                          <TableCell className="text-right"><MoneyDisplay amount={p.bruto} currency={p.moneda} /></TableCell>
                          <TableCell className="text-right"><MoneyDisplay amount={p.retenciones} currency={p.moneda} /></TableCell>
                          <TableCell className="text-right"><MoneyDisplay amount={p.neto} currency={p.moneda} /></TableCell>
                        </TableRow>
                      ))}
                    </Fragment>
                  );
                })}
              </TableBody>
            </Table>
          </TableContainer>
        </div>
      )}

      <section className="flex flex-wrap items-center justify-between gap-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
        <p className="text-xs text-ink-muted" aria-live="polite">
          {elegidos.size} {elegidos.size === 1 ? 'factura' : 'facturas'} de {totales.proveedores}{' '}
          {totales.proveedores === 1 ? 'proveedor' : 'proveedores'} · Bruto{' '}
          <MoneyDisplay amount={totales.bruto} currency={moneda || 'MXN'} /> · Retenciones{' '}
          <MoneyDisplay amount={totales.retenciones} currency={moneda || 'MXN'} /> · Neto{' '}
          <MoneyDisplay amount={totales.neto} currency={moneda || 'MXN'} strong />
        </p>
        <Button
          onClick={guardar}
          disabled={crear.isPending || elegidos.size === 0 || cuentaId == null}
          title={elegidos.size === 0 ? 'Elige al menos una factura.' : undefined}
        >
          {crear.isPending ? 'Creando…' : 'Crear corrida en borrador'}
        </Button>
      </section>
    </div>
  );
}
