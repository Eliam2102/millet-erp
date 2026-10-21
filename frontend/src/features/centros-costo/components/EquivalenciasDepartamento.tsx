import { FormCampo, FormGrid } from '@/components/erp/forms/FormGrid';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { ESTADOS_EQUIVALENCIA } from '../lib/estados-ui';
import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { apiRequest } from '@/lib/api';
import { zId } from '@/lib/z-id';
import { CentroCostoPicker } from './CentroCostoPicker';
interface Equivalencia {
  sucursalId: string;
  departamentoId: string;
  departamento: string;
  centroCostoId: string | null;
  centroCosto: string | null;
  observaciones: string | null;
  version: number;
}
export function EquivalenciasDepartamento({ puedeAdministrar }: { puedeAdministrar: boolean }) {
  const [sucursalId, setSucursalId] = useState<string | null>(null);
  const query = useQuery({
    queryKey: ['centros-costo', 'equivalencias', sucursalId],
    enabled: !!sucursalId,
    queryFn: async ({ signal }) =>
      (
        await apiRequest<Equivalencia[]>(`/api/v1/centros-costo/equivalencias/${sucursalId}`, {
          signal,
        })
      ).data,
  });
  return (
    <section className="space-y-4 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <h2 className="text-lg font-semibold text-ink">Centro de costo por departamento</h2>
      <p className="text-sm text-ink-muted">
        Define el centro de costo heredado en Compras para cada departamento. Las equivalencias de
        demostración requieren validación.
      </p>
      <FormGrid>
        <FormCampo span={6}>
          <Label htmlFor="equivalencia-sucursal">Sucursal</Label>
          <SucursalSelector
            triggerProps={{ id: 'equivalencia-sucursal' }}
            value={sucursalId}
            onChange={setSucursalId}
          />
        </FormCampo>
      </FormGrid>
      {query.isLoading && <p className="text-sm text-ink-muted">Consultando equivalencias…</p>}
      {query.isError && (
        <p role="alert" className="text-sm text-danger-fg">
          No se pudieron consultar las equivalencias de esta sucursal.
        </p>
      )}
      <div className="space-y-3">
        {query.data?.map((e) => (
          <EquivalenciaFila
            key={`${e.departamentoId}-${e.version}`}
            equivalencia={e}
            puedeAdministrar={puedeAdministrar}
          />
        ))}
      </div>
    </section>
  );
}
function EquivalenciaFila({
  equivalencia: e,
  puedeAdministrar,
}: {
  equivalencia: Equivalencia;
  puedeAdministrar: boolean;
}) {
  const [centro, setCentro] = useState<string | null>(e.centroCostoId);
  const [motivo, setMotivo] = useState(e.observaciones ?? '');
  const client = useQueryClient();
  const guardar = useMutation({
    mutationFn: async () => {
      const centroCostoId = zId().parse(centro);
      await apiRequest(`/api/v1/centros-costo/equivalencias/${e.sucursalId}/${e.departamentoId}`, {
        method: 'PUT',
        body: { centroCostoId, observaciones: motivo.trim() },
        ifMatch: String(e.version),
        idempotencyKey: crypto.randomUUID(),
      });
    },
    onSuccess: () =>
      client.invalidateQueries({ queryKey: ['centros-costo', 'equivalencias', e.sucursalId] }),
  });
  const valido =
    zId().safeParse(centro).success && motivo.trim().length > 0 && motivo.length <= 500;
  return (
    <div className="space-y-2 border-b border-line-divider pb-3">
      <p className="text-sm font-medium text-ink">
        {e.departamento}{' '}
        {e.observaciones?.includes('DEMO') && (
          <Badge variant={ESTADOS_EQUIVALENCIA.demo.tono}>{ESTADOS_EQUIVALENCIA.demo.texto}</Badge>
        )}
      </p>
      {puedeAdministrar ? (
        <FormSeccion titulo="Equivalencia del departamento">
          <FormCampo span={6}>
            <Label htmlFor={`centro-${e.departamentoId}`}>Planta o área *</Label>
            <CentroCostoPicker
              triggerProps={{ id: `centro-${e.departamentoId}` }}
              value={centro}
              onChange={setCentro}
              endpoint="/api/v1/centros-costo/equivalencias/opciones"
              initialLabel={e.centroCosto ?? undefined}
            />
          </FormCampo>
          <FormCampo span={6}>
            <Label htmlFor={`motivo-${e.departamentoId}`}>Motivo del cambio *</Label>
            <Input
              id={`motivo-${e.departamentoId}`}
              value={motivo}
              maxLength={500}
              onChange={(ev) => setMotivo(ev.target.value)}
            />
          </FormCampo>
          <FormCampo span={12}>
            <Button
              onClick={() => guardar.mutate()}
              disabled={!valido || guardar.isPending}
              title={!valido ? 'Selecciona planta o área e indica el motivo.' : undefined}
            >
              Guardar equivalencia
            </Button>
            {guardar.isError && (
              <p role="alert" className="text-sm text-danger-fg">
                {guardar.error.message}
              </p>
            )}
          </FormCampo>
        </FormSeccion>
      ) : (
        <p className="text-sm text-ink-muted">
          {e.centroCosto ?? 'Sin equivalencia'} · {e.observaciones}
        </p>
      )}
    </div>
  );
}
