import { useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { apiRequest, esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { FormCampo } from '@/components/erp/forms/FormGrid';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';

type Plazo = { diasHabilesIncidenciaRecepcion: number };
const ruta = '/api/v1/comercio-exterior/configuracion';
const clave = ['comercio-exterior', 'configuracion'];
export function ConfiguracionIncidencias() {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  const [dias, setDias] = useState<number | null>(null);
  const consulta = useQuery({
    queryKey: clave,
    queryFn: async ({ signal }) => (await apiRequest<Plazo>(ruta, { signal })).data,
  });
  const guardar = useMutation({
    mutationFn: async (body: Plazo) =>
      (await apiRequest<Plazo>(ruta, { method: 'PUT', body, idempotencyKey: keyFor(body) })).data,
    onSuccess: (data) => {
      qc.setQueryData(clave, data);
      setDias(null);
    },
  });
  function submit(e: FormEvent) {
    e.preventDefault();
    guardar.mutate({
      diasHabilesIncidenciaRecepcion: dias ?? consulta.data!.diasHabilesIncidenciaRecepcion,
    });
  }
  if (consulta.isLoading) return <p role="status">Consultando configuración…</p>;
  if (consulta.isError)
    return (
      <p role="alert">
        No se pudo consultar el plazo.{' '}
        <Button variant="ghost" onClick={() => void consulta.refetch()}>
          Volver a consultar
        </Button>
      </p>
    );
  return (
    <form onSubmit={submit} className="rounded-lg bg-surface-card p-4 shadow-card-flat">
      <FormSeccion titulo="Configuración de incidencias de recepción">
        <FormCampo span={4}>
          <Label htmlFor="config-dias">Días hábiles para aclarar diferencias *</Label>
          <Input
            id="config-dias"
            type="number"
            required
            min={1}
            max={365}
            step={1}
            value={dias ?? consulta.data?.diasHabilesIncidenciaRecepcion ?? 2}
            onChange={(e) => setDias(Number(e.target.value))}
          />
        </FormCampo>
        <FormCampo span={12}>
          <p className="text-xs text-ink-muted">
            Aplica a las nuevas incidencias automáticas de esta empresa. Se cuentan lunes a viernes,
            excluyendo los festivos configurados en Administración.
          </p>
          {guardar.isError && (
            <p role="alert" className="text-danger-fg">
              {esApiError(guardar.error)
                ? (guardar.error.problem.detail ?? guardar.error.problem.title)
                : 'No se pudo guardar el plazo.'}
            </p>
          )}
          {guardar.isSuccess && <p role="status">Plazo guardado.</p>}
          <Button
            type="submit"
            disabled={guardar.isPending}
            title={guardar.isPending ? 'Guardando plazo.' : undefined}
          >
            Guardar plazo
          </Button>
        </FormCampo>
      </FormSeccion>
    </form>
  );
}
