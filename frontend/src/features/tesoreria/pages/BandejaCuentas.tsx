import { useState } from 'react';
import { toast } from 'sonner';
import { Landmark, Pencil, Plus } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import {
  useCambiarEstadoCuentaBancaria,
  useCuentasBancarias,
} from '@/features/tesoreria/api/useTesoreria';
import type { CuentaSaldoResponse } from '@/features/tesoreria/api/types';
import { CuentaBancariaSheet } from '@/features/tesoreria/components/CuentaBancariaSheet';
import { formatoMonto } from '@/features/tesoreria/lib/formato';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';

/**
 * Bandeja del catálogo de cuentas bancarias propias — <c>/tesoreria/cuentas</c>
 * (TES-7 revisada, P1): lista con saldo + alta/edición por Sheet + toggle
 * activar/desactivar con confirm. Número/CLABE llegan enmascarados salvo
 * <c>ver-cuenta-completa</c>; el saldo refleja lo capturado en el sistema
 * (los saldos iniciales llegan con la conciliación, T-G8).
 */
export function BandejaCuentas() {
  const puedeAdministrar = useHasPermission(
    PermisosCanonicos.TesoreriaCuentasAdministrar,
  );

  const [soloActivas, setSoloActivas] = useState(true);
  const query = useCuentasBancarias(soloActivas);
  const cambiarEstado = useCambiarEstadoCuentaBancaria();

  const [sheetAbierto, setSheetAbierto] = useState(false);
  const [editando, setEditando] = useState<CuentaSaldoResponse | null>(null);
  const [desactivando, setDesactivando] = useState<CuentaSaldoResponse | null>(
    null,
  );

  const cuentas = query.data ?? [];

  function ejecutarCambioEstado(cuenta: CuentaSaldoResponse, activa: boolean) {
    cambiarEstado.mutate(
      {
        cuentaId: cuenta.id,
        activa,
        versionEsperada: cuenta.version,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success(activa ? 'Cuenta activada' : 'Cuenta desactivada', {
            description: `${cuenta.banco} · ${cuenta.numeroCuenta}.`,
          });
        },
        onError: (error) => {
          toast.error(
            esApiError(error)
              ? error.problem.title
              : 'No se pudo cambiar el estado de la cuenta',
            {
              description: esApiError(error)
                ? (error.problem.detail ?? `Código: ${error.traceId}`)
                : undefined,
            },
          );
        },
      },
    );
  }

  return (
    <div className="space-y-4 px-4 py-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Cuentas bancarias
          </h1>
          <p className="text-sm text-muted-foreground">
            Catálogo de cuentas propias de la empresa con saldo según lo
            registrado en el sistema. El número y la CLABE se muestran
            enmascarados salvo permiso específico.
          </p>
        </div>
        <div className="flex items-center gap-4 text-sm">
          <label className="flex cursor-pointer items-center gap-2">
            <Checkbox
              checked={soloActivas}
              onCheckedChange={(v) => setSoloActivas(v === true)}
            />
            Solo activas
          </label>
          {puedeAdministrar && (
            <Button
              onClick={() => {
                setEditando(null);
                setSheetAbierto(true);
              }}
            >
              <Plus className="mr-1 h-4 w-4" />
              Nueva cuenta
            </Button>
          )}
        </div>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las cuentas bancarias"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={4}
          columns={[
            { width: 'w-40' },
            { width: 'w-36' },
            { width: 'w-36' },
            { width: 'w-16' },
            { width: 'w-24' },
            { width: 'w-20' },
            { width: 'w-28' },
          ]}
        />
      ) : cuentas.length === 0 ? (
        <EmptyState
          icon={<Landmark className="h-10 w-10" />}
          title="Sin cuentas bancarias registradas."
          description={
            puedeAdministrar
              ? 'Crea la primera cuenta con "Nueva cuenta"; con ella podrás registrar movimientos, pagos y depósitos.'
              : 'Las cuentas del catálogo aparecerán aquí cuando se registren.'
          }
        />
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-3 py-2 font-medium">Banco</th>
                <th className="px-3 py-2 font-medium">Número</th>
                <th className="px-3 py-2 font-medium">CLABE</th>
                <th className="px-3 py-2 font-medium">Moneda</th>
                <th className="px-3 py-2 font-medium">Cta. contable</th>
                <th className="px-3 py-2 text-right font-medium">Saldo</th>
                <th className="px-3 py-2 font-medium">Estado</th>
                {puedeAdministrar && <th className="px-3 py-2" />}
              </tr>
            </thead>
            <tbody className="divide-y">
              {cuentas.map((c) => (
                <tr key={c.id} className="hover:bg-muted/30">
                  <td className="max-w-56 truncate px-3 py-2">{c.banco}</td>
                  <td className="px-3 py-2 font-mono text-xs">
                    {c.numeroCuenta}
                  </td>
                  <td className="px-3 py-2 font-mono text-xs">
                    {c.clabe ?? '—'}
                  </td>
                  <td className="px-3 py-2">{c.moneda}</td>
                  <td className="px-3 py-2 font-mono text-xs">
                    {c.cuentaContableRef ?? '—'}
                  </td>
                  <td className="px-3 py-2 text-right font-mono tabular-nums">
                    {formatoMonto(c.saldo, c.moneda)}
                  </td>
                  <td className="px-3 py-2">
                    <Badge
                      variant="outline"
                      className={
                        c.activa
                          ? 'border-emerald-300 bg-emerald-50 text-emerald-700'
                          : 'border-slate-300 bg-slate-50 text-slate-600'
                      }
                    >
                      {c.activa ? 'Activa' : 'Inactiva'}
                    </Badge>
                  </td>
                  {puedeAdministrar && (
                    <td className="px-3 py-2 text-right">
                      <div className="flex justify-end gap-2">
                        <Button
                          size="sm"
                          variant="outline"
                          onClick={() => {
                            setEditando(c);
                            setSheetAbierto(true);
                          }}
                        >
                          <Pencil className="mr-1 h-3.5 w-3.5" />
                          Editar
                        </Button>
                        {c.activa ? (
                          <Button
                            size="sm"
                            variant="ghost"
                            className="text-rose-700 hover:text-rose-800"
                            disabled={cambiarEstado.isPending}
                            onClick={() => setDesactivando(c)}
                          >
                            Desactivar
                          </Button>
                        ) : (
                          <Button
                            size="sm"
                            variant="ghost"
                            disabled={cambiarEstado.isPending}
                            onClick={() => ejecutarCambioEstado(c, true)}
                          >
                            Activar
                          </Button>
                        )}
                      </div>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <CuentaBancariaSheet
        open={sheetAbierto}
        cuenta={editando}
        onOpenChange={(o) => {
          setSheetAbierto(o);
          if (!o) setEditando(null);
        }}
      />

      <AlertDialog
        open={desactivando != null}
        onOpenChange={(o) => {
          if (!o) setDesactivando(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>¿Desactivar la cuenta?</AlertDialogTitle>
            <AlertDialogDescription>
              {desactivando != null &&
                `${desactivando.banco} · ${desactivando.numeroCuenta} (${desactivando.moneda}) dejará de ofrecerse para pagos, depósitos y movimientos nuevos. El historial se conserva y puedes reactivarla cuando quieras.`}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancelar</AlertDialogCancel>
            <AlertDialogAction
              className="bg-rose-600 text-white hover:bg-rose-700"
              onClick={() => {
                if (desactivando != null)
                  ejecutarCambioEstado(desactivando, false);
                setDesactivando(null);
              }}
            >
              Desactivar
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
