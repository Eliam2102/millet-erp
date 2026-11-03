import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { apiRequest, esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

interface AccesoPortal {
  id: string;
  proveedorId: string;
  email: string;
  activo: boolean;
}

interface Props {
  proveedorId: string;
}

/**
 * Sección «Acceso al portal» de la ficha del proveedor (P8.3): correos autorizados para entrar al
 * portal de proveedores y activar o desactivar cada uno. Solo con <c>cuentas_por_pagar.portal.administrar</c>.
 */
export function ProveedorAccesoPortalSection({ proveedorId }: Props) {
  const puede = useHasPermission(PermisosCanonicos.CuentasPorPagarPortalAdministrar);
  const queryClient = useQueryClient();
  const [email, setEmail] = useState('');
  const base = `/api/v1/cuentas-por-pagar/proveedores/${proveedorId}/acceso-portal`;
  const llave = ['portal-proveedor-accesos', proveedorId];

  const accesos = useQuery({
    queryKey: llave,
    enabled: puede,
    queryFn: async ({ signal }) => (await apiRequest<AccesoPortal[]>(`${base}/`, { signal })).data,
  });

  const alFallar = (titulo: string) => (error: Error) =>
    toast.error(titulo, {
      description: esApiError(error) ? (error.problem.detail ?? error.problem.title) : 'Intenta de nuevo.',
    });

  const agregar = useMutation({
    mutationFn: async (correo: string) =>
      apiRequest<AccesoPortal>(`${base}/`, { method: 'POST', body: { email: correo }, idempotencyKey: crypto.randomUUID() }),
    onSuccess: () => {
      setEmail('');
      toast.success('Correo autorizado para el portal');
      return queryClient.invalidateQueries({ queryKey: llave });
    },
    onError: alFallar('No se pudo agregar el correo'),
  });

  const cambiar = useMutation({
    mutationFn: async (v: { id: string; activo: boolean }) =>
      apiRequest<AccesoPortal>(`${base}/${v.id}/activo`, { method: 'PUT', body: { activo: v.activo }, idempotencyKey: crypto.randomUUID() }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: llave }),
    onError: alFallar('No se pudo cambiar el acceso'),
  });

  if (!puede) return null;
  const campoId = `portal-email-${proveedorId}`;

  return (
    <section aria-labelledby={`${campoId}-titulo`} className="mt-4 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <h3 id={`${campoId}-titulo`} className="mb-1 text-sm font-semibold text-ink">
        Acceso al portal
      </h3>
      <p className="mb-3 text-xs text-ink-muted">
        Correos que pueden entrar al portal de proveedores y ver las facturas y pagos de este proveedor. Reciben un código
        de acceso por correo; un mismo correo solo puede pertenecer a un proveedor.
      </p>
      <ul className="mb-3 divide-y divide-border">
        {(accesos.data ?? []).map((a) => (
          <li key={a.id} className="flex items-center justify-between gap-3 py-2 text-sm">
            <span className="text-ink">{a.email}</span>
            <span className="flex items-center gap-2">
              <Badge variant={a.activo ? 'default' : 'secondary'}>{a.activo ? 'Activo' : 'Desactivado'}</Badge>
              <Button
                variant="ghost"
                size="sm"
                disabled={cambiar.isPending}
                onClick={() => cambiar.mutate({ id: a.id, activo: !a.activo })}
              >
                {a.activo ? 'Desactivar' : 'Activar'}
              </Button>
            </span>
          </li>
        ))}
        {accesos.isSuccess && accesos.data.length === 0 && (
          <li className="py-2 text-sm text-ink-muted">Todavía no hay correos autorizados.</li>
        )}
      </ul>
      <form
        className="flex items-end gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          if (email.trim() && !agregar.isPending) agregar.mutate(email.trim());
        }}
      >
        <div className="flex-1">
          <Label htmlFor={campoId}>Correo autorizado</Label>
          <Input id={campoId} type="email" value={email} onChange={(e) => setEmail(e.target.value)} disabled={agregar.isPending} />
        </div>
        <Button type="submit" disabled={!email.trim() || agregar.isPending}>
          {agregar.isPending ? 'Agregando…' : 'Agregar correo'}
        </Button>
      </form>
    </section>
  );
}
