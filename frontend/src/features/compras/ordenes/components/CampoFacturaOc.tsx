import { Field } from '@/components/erp';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { useUsosCfdi, useFormasPago } from '@/modules/catalogos/api';

export type CampoFactura = 'usoCfdiClave' | 'metodoPago' | 'formaPagoClave';
const ETIQUETAS_FACTURA = {
  usoCfdiClave: 'Uso de CFDI', metodoPago: 'Método de pago', formaPagoClave: 'Forma de pago',
};

export function CampoFacturaOc({ campo, value, onChange, error }: {
  campo: CampoFactura; value?: string | null; onChange: (value: string) => void; error?: string;
}) {
  const usos = useUsosCfdi();
  const formas = useFormasPago();
  const catalogo = campo === 'usoCfdiClave' ? usos : formas;
  const opciones = campo === 'metodoPago'
    ? [{ claveSat: 'PUE', descripcion: 'Pago en una sola exhibición' },
      { claveSat: 'PPD', descripcion: 'Pago en parcialidades o diferido' }]
    : (catalogo.data ?? []).filter(x => x.activa);
  return <Field label={ETIQUETAS_FACTURA[campo]} error={error}>
    {(props) => <>
      <Select value={value || 'sin-capturar'} onValueChange={v => onChange(v === 'sin-capturar' ? '' : v)}>
        <SelectTrigger {...props}><SelectValue placeholder="Selecciona una opción" /></SelectTrigger>
        <SelectContent>
          <SelectItem value="sin-capturar">Por capturar</SelectItem>
          {opciones.map(x => <SelectItem key={x.claveSat} value={x.claveSat}>{x.claveSat} – {x.descripcion}</SelectItem>)}
          {value && !opciones.some(x => x.claveSat === value) && <SelectItem value={value} disabled>{value} – Selecciona una opción activa</SelectItem>}
        </SelectContent>
      </Select>
      {campo !== 'metodoPago' && catalogo.isError && <p className="text-xs text-danger-fg">No se pudo cargar el catálogo. <button type="button" className="underline" onClick={() => { void catalogo.refetch(); }}>Volver a cargar</button></p>}
    </>}
  </Field>;
}
