import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { ArticuloSelector } from '@/components/erp';
import { Field } from '@/features/almacen/components/internal/Field';
import { useGuardarNormaConsumo, type NormaConsumo } from '@/features/almacen/api/useConsumoComponentes';
import { esM3 } from '@/features/almacen/lib/consumo-componentes';
import { codigoSoporte, esApiError } from '@/lib/api';

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Norma a editar; null = norma nueva para un artículo que aún no tiene. */
  editando: NormaConsumo | null;
}

/** A4.8: alta o edición de la norma de un componente. Guardar es un PUT por artículo, así que crear y editar son lo mismo. */
export function NormaConsumoSheet({ open, onOpenChange, editando }: Props) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" size="lg">
        <SheetHeader>
          <SheetTitle>{editando ? 'Editar norma de consumo' : 'Nueva norma de consumo'}</SheetTitle>
          <SheetDescription>
            Cuánto se consume del componente por cada pieza terminada en A+W y su merma. El cambio aplica a los consumos
            siguientes y a los que están en revisión; lo ya descontado no se recalcula.
          </SheetDescription>
        </SheetHeader>
        {open && <Formulario key={editando?.articuloId ?? 'nueva'} editando={editando} onCerrar={() => onOpenChange(false)} />}
      </SheetContent>
    </Sheet>
  );
}

function Formulario({ editando, onCerrar }: { editando: NormaConsumo | null; onCerrar: () => void }) {
  const guardar = useGuardarNormaConsumo();
  const [articuloId, setArticuloId] = useState<string | null>(editando?.articuloId ?? null);
  const [unidad, setUnidad] = useState<string | null>(editando?.unidadMedida ?? null);
  const [consumo, setConsumo] = useState(editando ? String(editando.consumoPorUnidad) : '1');
  const [merma, setMerma] = useState(editando ? String(editando.porcentajeMerma) : '0');
  const [factor, setFactor] = useState(editando?.factorM3PorM2 != null ? String(editando.factorM3PorM2) : '');
  const [errores, setErrores] = useState<Record<string, string>>({});
  const pideFactor = esM3(unidad);

  function enviar(e: React.FormEvent) {
    e.preventDefault();
    const c = Number(consumo);
    const m = Number(merma);
    const f = Number(factor);
    const faltan: Record<string, string> = {};
    if (!articuloId) faltan.articulo = 'Elige el componente.';
    if (!(c > 0)) faltan.consumo = 'El consumo debe ser mayor que cero.';
    if (merma.trim() === '' || !(m >= 0 && m < 100)) faltan.merma = 'La merma va de 0 a menos de 100 %.';
    if (pideFactor && !(f > 0)) faltan.factor = 'El artículo está en m³: captura el factor m² → m³.';
    setErrores(faltan);
    if (Object.keys(faltan).length > 0) return;
    guardar.mutate(
      { articuloId: articuloId!, captura: { consumoPorUnidad: c, porcentajeMerma: m, factorM3PorM2: pideFactor ? f : null } },
      {
        onSuccess: () => {
          toast.success('Norma guardada');
          onCerrar();
        },
        onError: (error) =>
          toast.error(esApiError(error) ? (error.problem.detail ?? error.problem.title) : 'No se pudo guardar la norma', {
            description: esApiError(error) ? codigoSoporte(error) : undefined,
          }),
      },
    );
  }

  return (
    <>
      <SheetBody>
        <form id="norma-consumo-form" onSubmit={enviar} className="space-y-4" noValidate>
          <Field label="Componente" required error={errores.articulo}>
            {editando ? (
              <p className="text-sm">
                <span className="font-mono text-xs">{editando.articuloClave}</span> · {editando.articuloDescripcion}
                {unidad ? <span className="text-ink-muted"> · {unidad}</span> : null}
              </p>
            ) : (
              <ArticuloSelector
                value={articuloId}
                onChange={(id) => {
                  setArticuloId(id);
                  if (!id) setUnidad(null);
                }}
                onSelect={(a) => setUnidad(a.unidadMedidaDefault)}
              />
            )}
          </Field>
          <div className="grid grid-cols-2 gap-4">
            <Field label="Consumo por unidad" required error={errores.consumo}>
              <Input type="number" min="0" step="any" value={consumo} onChange={(e) => setConsumo(e.target.value)} />
            </Field>
            <Field label="Merma %" required error={errores.merma}>
              <Input type="number" min="0" max="99.9999" step="any" value={merma} onChange={(e) => setMerma(e.target.value)} />
            </Field>
          </div>
          {pideFactor && (
            <Field label="Factor m² → m³" required error={errores.factor}>
              <Input type="number" min="0" step="any" value={factor} onChange={(e) => setFactor(e.target.value)} />
            </Field>
          )}
        </form>
      </SheetBody>
      <SheetFooter>
        <Button type="button" variant="ghost" onClick={onCerrar} disabled={guardar.isPending}>
          Cancelar
        </Button>
        <Button type="submit" form="norma-consumo-form" disabled={guardar.isPending}>
          {guardar.isPending ? 'Guardando…' : 'Guardar norma'}
        </Button>
      </SheetFooter>
    </>
  );
}
