import { estadoUi } from '../lib/estados-ui';
import { DialogBody } from '@/components/ui/dialog';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { BarraFiltros, TabsEstado, PaginacionTabla } from '@/components/erp';
import { usePaginacionLocal } from '../lib/use-paginacion-local';
import { PageHeader } from '@/components/erp/PageHeader';
import {
  Table,
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
} from '@/components/ui/table';
import { useState } from 'react';
import { toast } from 'sonner';
import { Landmark, Pencil, Plus } from 'lucide-react';
import { EstadoTesoreriaBadge } from '../components/Badges';
import { Button } from '@/components/ui/button';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogContent,
  AlertDialogCancel,
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
import { esApiError, codigoSoporte } from '@/lib/api';

/**
 * Bandeja del catálogo de cuentas bancarias propias — <c>/tesoreria/cuentas</c>
 * (TES-7 revisada, P1): lista con saldo + alta/edición por Sheet + toggle
 * activar/desactivar con confirm. Número/CLABE llegan enmascarados salvo
 * <c>ver-cuenta-completa</c>; el saldo refleja lo capturado en el sistema
 * (los saldos iniciales llegan con la conciliación, T-G8).
 */
export function BandejaCuentas() {
  const puedeAdministrar = useHasPermission(PermisosCanonicos.TesoreriaCuentasAdministrar);

  const [estadoVista, setEstadoVista] = useState<number | undefined>(1);
  const soloActivas = estadoVista === 1;
  const query = useCuentasBancarias(soloActivas);
  const cambiarEstado = useCambiarEstadoCuentaBancaria();

  const [sheetAbierto, setSheetAbierto] = useState(false);
  const [editando, setEditando] = useState<CuentaSaldoResponse | null>(null);
  const [desactivando, setDesactivando] = useState<CuentaSaldoResponse | null>(null);

  const cuentas = (query.data ?? []).filter((c) => estadoVista !== 2 || !c.activa);

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
            esApiError(error) ? error.problem.title : 'No se pudo cambiar el estado de la cuenta',
            {
              description: esApiError(error)
                ? (error.problem.detail ?? codigoSoporte(error))
                : undefined,
            },
          );
        },
      },
    );
  }

  const paginacion = usePaginacionLocal(cuentas, String(estadoVista));

  return (
    <div className="flex h-full min-h-0 flex-col gap-5 text-sm text-ink">
      <PageHeader
        acciones={
          <>
            {' '}
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
            )}{' '}
          </>
        }
        titulo={<>Cuentas bancarias</>}
        descripcion={
          <>
            Catálogo de cuentas propias de la empresa con saldo según lo registrado en el sistema.
            El número y la CLABE se muestran enmascarados salvo permiso específico.
          </>
        }
      />

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={[
              { valor: 1, label: estadoUi('cuenta', 'activa').texto },
              { valor: 2, label: estadoUi('cuenta', 'inactiva').texto },
            ]}
            valor={estadoVista}
            onChange={setEstadoVista}
            conteos={undefined}
          />
        }
        filtros={<BarraFiltros total={`${cuentas.length} cuentas en la consulta`} />}
        pie={
          <PaginacionTabla
            total={cuentas.length}
            offset={paginacion.offset}
            limit={10}
            tamanos={[]}
            onChange={paginacion.onChange}
          />
        }
      >
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
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50 text-left text-xs uppercase tracking-wide text-ink-muted">
                <TableRow>
                  <TableHead className="px-3 py-2 font-medium">Banco</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Número</TableHead>
                  <TableHead className="px-3 py-2 font-medium">CLABE</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Moneda</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Cta. contable</TableHead>
                  <TableHead className="px-3 py-2 text-right font-medium">Saldo</TableHead>
                  <TableHead className="px-3 py-2 font-medium">Estado</TableHead>
                  {puedeAdministrar && <TableHead className="px-3 py-2" />}
                </TableRow>
              </TableHeader>
              <TableBody className="divide-y">
                {paginacion.visibles.map((c) => (
                  <TableRow key={c.id} className="hover:bg-surface-muted/30">
                    <TableCell className="max-w-56 truncate px-3 py-2">{c.banco}</TableCell>
                    <TableCell className="px-3 py-2 font-mono text-xs">{c.numeroCuenta}</TableCell>
                    <TableCell className="px-3 py-2 font-mono text-xs">{c.clabe ?? '—'}</TableCell>
                    <TableCell className="px-3 py-2">{c.moneda}</TableCell>
                    <TableCell className="px-3 py-2 font-mono text-xs">
                      {c.cuentaContableRef ?? '—'}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      {formatoMonto(c.saldo, c.moneda)}
                    </TableCell>
                    <TableCell className="px-3 py-2">
                      <EstadoTesoreriaBadge
                        entidad="cuenta"
                        estado={c.activa ? 'activa' : 'inactiva'}
                      />
                    </TableCell>
                    {puedeAdministrar && (
                      <TableCell className="px-3 py-2 text-right">
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
                              className="text-danger-fg hover:text-danger-fg"
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
                      </TableCell>
                    )}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </ListadoModulo>

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
        <AlertDialogContent className="flex max-h-[90vh] w-[calc(100vw-2rem)] max-w-[480px] flex-col rounded-2xl border-line bg-surface-card shadow-dialog">
          <AlertDialogHeader>
            <AlertDialogTitle>¿Desactivar la cuenta?</AlertDialogTitle>
          </AlertDialogHeader>
          <DialogBody>
            <AlertDialogDescription>
              {desactivando != null &&
                `${desactivando.banco} · ${desactivando.numeroCuenta} (${desactivando.moneda}) dejará de ofrecerse para pagos, depósitos y movimientos nuevos. El historial se conserva y puedes reactivarla cuando quieras.`}
            </AlertDialogDescription>
          </DialogBody>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancelar</AlertDialogCancel>
            <AlertDialogAction
              className="bg-danger text-white hover:bg-danger"
              onClick={() => {
                if (desactivando != null) ejecutarCambioEstado(desactivando, false);
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
