import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { formatMoney, money } from '@/lib/money';
import { Table, TableHeader, TableRow, TableHead, TableBody, TableCell } from '@/components/ui/table';

interface SaldoMiembroGrupo {
  clienteId: string;
  clave: string;
  razonSocial: string;
  esPrincipal: boolean;
  monedas: { moneda: string; saldoNeto: number }[];
}
interface SaldoGrupoCliente {
  clienteId: string;
  miembros: SaldoMiembroGrupo[];
}

/**
 * Saldo por cliente y moneda del grupo de unificación por RFC (86e3x4g77). Cada miembro conserva su propio
 * saldo: nunca se suma al principal. No se muestra si el cliente no es parte de un grupo o el usuario no lee cartera.
 */
export function ClienteSaldoGrupoSection({ clienteId }: { clienteId: string }) {
  const puedeVer = useHasPermission(PermisosCanonicos.CuentasPorCobrarCarteraLeer);
  const { data } = useQuery<SaldoGrupoCliente>({
    queryKey: ['cxc', 'saldo-grupo', clienteId],
    enabled: puedeVer,
    queryFn: async ({ signal }) =>
      (await apiRequest<SaldoGrupoCliente>(`/api/v1/cuentas-por-cobrar/cartera/saldo-grupo/${clienteId}`, { signal })).data,
  });
  if (!puedeVer || !data || data.miembros.length === 0) return null;

  const filas = data.miembros.flatMap((m) =>
    (m.monedas.length > 0 ? m.monedas : [null]).map((s) => ({ m, s })),
  );
  return (
    <section className="mt-4 rounded-md border bg-surface-card p-4" aria-label="Saldo del grupo de clientes">
      <h3 className="mb-1 text-sm font-semibold">Saldo del grupo</h3>
      <p className="mb-3 text-xs text-ink-muted">
        Clientes unificados por RFC. Cada uno conserva su saldo por moneda; no se suman al principal.
      </p>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Cliente</TableHead>
            <TableHead>Moneda</TableHead>
            <TableHead className="text-right">Saldo</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {filas.map(({ m, s }) => (
            <TableRow key={`${m.clienteId}-${s?.moneda ?? ''}`}>
              <TableCell className="py-1">
                <span className="font-mono">{m.clave}</span> {m.razonSocial}
                {m.esPrincipal ? ' (principal)' : ''}
              </TableCell>
              <TableCell className="py-1 font-mono">{s?.moneda ?? '—'}</TableCell>
              <TableCell className="py-1 text-right font-mono">
                {s ? formatMoney(money(s.saldoNeto, s.moneda)) : '—'}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </section>
  );
}
