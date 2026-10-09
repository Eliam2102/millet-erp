import { useState } from 'react';
import { useQuery, useMutation } from '@tanstack/react-query';
import { toast } from 'sonner';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { apiRequest, esApiError, useFormIdempotencyKey } from '@/lib/api';
export function SerieAnticipoProveedor({ proveedorId }: { proveedorId: string }) {
  const [serie, setSerie] = useState(''); const idempotencyKey = useFormIdempotencyKey();
  const query = useQuery({ queryKey: ['cxp', 'serie-anticipo', proveedorId], queryFn: async () => (await apiRequest<{ serie: string }>(`/api/v1/cuentas-por-pagar/anticipos/serie/${proveedorId}`)).data, enabled: !!proveedorId });
  const guardar = useMutation({ mutationFn: () => apiRequest(`/api/v1/cuentas-por-pagar/anticipos/serie/${proveedorId}`, { method: 'PUT', body: { serie }, idempotencyKey }),
    onSuccess: () => { query.refetch(); toast.success('Serie de anticipos configurada'); }, onError: e => toast.error(esApiError(e) ? e.problem.detail ?? e.problem.title : 'No se pudo configurar la serie.') });
  if (!proveedorId) return null;
  return <fieldset className="space-y-2 rounded-lg border border-line p-3"><legend className="text-sm font-medium text-ink">Serie de anticipos del proveedor</legend>
    <p className="text-xs text-ink-muted">Serie vigente: {query.data?.serie ?? (query.isLoading ? 'Cargando…' : 'Por confirmar')}. FANT es la serie por omisión.</p>
    <Label htmlFor="p4-serie-config">Nueva serie</Label><Input id="p4-serie-config" value={serie} maxLength={25} onChange={e => setSerie(e.target.value)} />
    <Button type="button" variant="secondary" disabled={!serie.trim() || guardar.isPending} title={!serie.trim() ? 'Captura la serie del proveedor.' : undefined} onClick={() => guardar.mutate()}>Configurar serie</Button>
  </fieldset>;
}
