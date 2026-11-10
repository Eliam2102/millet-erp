import { Controller, type Control, type FieldErrors } from 'react-hook-form';
import { FormCampo } from '@/components/erp/forms/FormGrid';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';

interface ValoresTambor {
  consumoPorTamborALinea: boolean;
  kgPorPresentacion: number | null;
}

/**
 * M-172 (AC-030): marca «Se consume por tambor a línea» y kilos por presentación. La comparten el alta y la edición
 * del artículo. Con la marca, la salida de almacén pide la línea de producción y piezas enteras.
 */
export function TamborALineaCampos({
  control,
  errors,
  disabled,
}: {
  control: Control<ValoresTambor>;
  errors: FieldErrors<ValoresTambor>;
  disabled?: boolean;
}) {
  return (
    <>
      <FormCampo span={6}>
        <div className="space-y-1.5">
          <Controller
            name="consumoPorTamborALinea"
            control={control}
            render={({ field }) => (
              <label className="flex items-center gap-2 text-sm font-medium">
                <Checkbox
                  checked={field.value}
                  onCheckedChange={(v) => field.onChange(v === true)}
                  disabled={disabled}
                />
                Se consume por tambor a línea
              </label>
            )}
          />
          <p className="text-xs text-ink-muted">
            Silicón, Ködispace y similares: se descuentan al salir del almacén por tambor o cubeta a una línea de
            producción, no por pieza terminada.
          </p>
        </div>
      </FormCampo>
      <FormCampo span={6}>
        <div className="space-y-1.5">
          <label htmlFor="kg-por-presentacion" className="text-sm font-medium">
            Kilos por presentación <span className="font-normal text-ink-muted">(opcional)</span>
          </label>
          <Controller
            name="kgPorPresentacion"
            control={control}
            render={({ field }) => (
              <Input
                id="kg-por-presentacion"
                type="number"
                step="0.001"
                min={0}
                disabled={disabled}
                value={field.value ?? ''}
                onChange={(e) => field.onChange(e.target.value === '' ? null : Number(e.target.value))}
                aria-invalid={errors.kgPorPresentacion != null}
              />
            )}
          />
          {errors.kgPorPresentacion?.message != null ? (
            <p role="alert" className="text-xs text-danger-fg">
              {errors.kgPorPresentacion.message}
            </p>
          ) : (
            <p className="text-xs text-ink-muted">Tambor 200 kg, cubeta 19 kg. Dato provisional hasta M-106.</p>
          )}
        </div>
      </FormCampo>
    </>
  );
}
