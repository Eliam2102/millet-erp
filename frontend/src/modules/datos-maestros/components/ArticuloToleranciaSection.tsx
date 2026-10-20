import { FormCampo } from '@/components/erp/forms/FormGrid';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { useActualizarToleranciaArticulo } from '../api/articulos';
import { interpretarToleranciaPorcentaje } from './tolerancia-articulo';

interface Props {
  articuloId: string;
  porcentaje: number | null;
}

export function ArticuloToleranciaSection({ articuloId, porcentaje }: Props) {
  const puedeEditar = useHasPermission(PermisosCanonicos.DatosMaestrosArticulosToleranciaEditar);
  const [editando, setEditando] = useState(false);
  const [valor, setValor] = useState('');
  const actualizar = useActualizarToleranciaArticulo();
  const resultado = interpretarToleranciaPorcentaje(valor);
  const campoId = `tolerancia-articulo-${articuloId}`;

  function guardar(event: React.FormEvent) {
    event.preventDefault();
    if (!puedeEditar || !resultado.valido || actualizar.isPending) return;
    actualizar.mutate(
      { id: articuloId, porcentaje: resultado.porcentaje, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          setEditando(false);
          toast.success('Tolerancia de recepción actualizada');
        },
        onError: (error) =>
          toast.error('No se pudo guardar la tolerancia', {
            description: esApiError(error)
              ? (error.problem.detail ?? error.problem.title)
              : 'Intenta de nuevo.',
          }),
      },
    );
  }

  return (
    <section
      aria-labelledby={`${campoId}-titulo`}
      className="mt-4 max-w-3xl rounded-lg bg-surface-card p-4 shadow-card-flat"
    >
      <div className="mb-3 flex items-center justify-between gap-3">
        <h3 id={`${campoId}-titulo`} className="text-sm font-semibold text-ink">
          Tolerancia de recepción (%)
        </h3>
        {puedeEditar && !editando && (
          <Button
            variant="ghost"
            size="sm"
            onClick={() => {
              setValor(porcentaje == null ? '' : String(porcentaje));
              setEditando(true);
            }}
          >
            Editar tolerancia
          </Button>
        )}
      </div>
      <p id={`${campoId}-ayuda`} className="mb-3 text-xs text-ink-muted">
        Porcentaje sobre la cantidad solicitada que Almacén puede recibir de más en cada línea de la
        orden de compra. Si se rebasa, la recepción se rechaza.
      </p>
      {editando && puedeEditar ? (
        <form onSubmit={guardar} className="space-y-3">
          <FormSeccion titulo="Datos del registro">
            <FormCampo span={12}>
              <Label htmlFor={campoId}>
                Tolerancia de recepción (%){' '}
                <span className="font-normal text-ink-muted">(opcional)</span>
              </Label>
            </FormCampo>
            <FormCampo span={12}>
              <Input
                id={campoId}
                inputMode="decimal"
                value={valor}
                onChange={(event) => setValor(event.target.value)}
                aria-describedby={`${campoId}-ayuda ${campoId}-opcional${!resultado.valido ? ` ${campoId}-error` : ''}`}
                aria-invalid={!resultado.valido}
                disabled={actualizar.isPending}
                className="text-right tabular-nums"
              />
            </FormCampo>
            <FormCampo span={12}>
              <p id={`${campoId}-opcional`} className="text-xs text-ink-muted">
                Deja el campo vacío para no permitir excedentes (0 %).
              </p>
            </FormCampo>
            <FormCampo span={12}>
              {!resultado.valido && (
                <p id={`${campoId}-error`} role="alert" className="text-xs text-danger-fg">
                  Ingresa un porcentaje entre 0 y 100 con hasta 2 decimales. Usa punto decimal.
                </p>
              )}
            </FormCampo>
            <FormCampo span={12}>
              <div className="flex justify-end gap-2">
                <Button
                  type="button"
                  variant="ghost"
                  size="lg"
                  disabled={actualizar.isPending}
                  onClick={() => setEditando(false)}
                >
                  Cancelar
                </Button>
                <Button
                  type="submit"
                  size="lg"
                  disabled={!resultado.valido || actualizar.isPending}
                  title={
                    actualizar.isPending
                      ? 'Guardando la tolerancia'
                      : !resultado.valido
                        ? 'Corrige el porcentaje para guardar'
                        : undefined
                  }
                >
                  {actualizar.isPending ? 'Guardando…' : 'Guardar tolerancia'}
                </Button>
              </div>
            </FormCampo>
          </FormSeccion>
        </form>
      ) : (
        <p className="text-right text-sm font-medium tabular-nums text-ink">
          {porcentaje == null ? 'Sin tolerancia (0 %)' : `${porcentaje}%`}
        </p>
      )}
    </section>
  );
}
