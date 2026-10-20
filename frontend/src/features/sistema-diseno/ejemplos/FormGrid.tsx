import { FormGrid } from '@/components/erp/forms/FormGrid';
import { FormCampo } from '@/components/erp/forms/FormGrid';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { Input } from '@/components/ui/input';

export default function Ejemplo() {
  return (
    <FormGrid>
      <FormCampo span={12}>
        <FormSeccion
          numero={1}
          titulo="Datos ficticios"
          descripcion="Los obligatorios llevan asterisco."
        >
          {([3, 4, 6, 12] as const).map((span) => (
            <FormCampo key={span} span={span}>
              <label>
                Campo de {span} columnas *<Input required placeholder="[EJEMPLO]" />
              </label>
            </FormCampo>
          ))}
        </FormSeccion>
      </FormCampo>
    </FormGrid>
  );
}
