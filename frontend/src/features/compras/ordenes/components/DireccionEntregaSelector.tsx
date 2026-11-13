import { useState } from 'react';
import { useSucursales } from '@/features/catalogos/api';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';
import { CatalogoQueryError } from '@/components/erp/selectors/CatalogoQueryError';
import { TextAreaField } from '@/components/erp';

const OTRA = 'otra-direccion';

export function DireccionEntregaSelector({ value, onChange, onOtraChange, error }: {
  value: string | null;
  onChange: (value: string | null) => void;
  onOtraChange: (otra: boolean) => void;
  error?: string;
}) {
  const query = useSucursales();
  const [seleccion, setSeleccion] = useState<string | null>(null);
  const sedes = (query.data?.items ?? []).filter(s => s.estatus === 0 && s.direccionEntrega?.trim());
  const items = [
    ...sedes.map(s => ({ id: s.id, nombre: `${s.clave} · ${s.nombre}`, direccion: s.direccionEntrega! })),
    { id: OTRA, nombre: 'Otra', direccion: null },
  ];
  const elegida = seleccion ?? (value ? sedes.find(s => s.direccionEntrega === value)?.id ?? OTRA : null);
  const esSede = elegida != null && elegida !== OTRA;

  return (
    <div className="space-y-2">
      <CatalogoEagerCombobox
        items={items}
        value={elegida}
        onChange={id => {
          const seleccionNueva = id ?? OTRA;
          setSeleccion(seleccionNueva);
          onOtraChange(seleccionNueva === OTRA);
          const sede = sedes.find(s => s.id === id);
          if (sede) onChange(sede.direccionEntrega!);
        }}
        itemToLabel={item => item.nombre}
        renderItem={item => <span>{item.nombre}</span>}
        placeholder="Selecciona sede u Otra"
        searchPlaceholder="Buscar sede de entrega…"
        ariaLabel="Sede de entrega"
      />
      {query.isLoading && <p role="status" className="text-xs text-ink-muted">Cargando domicilios de sedes… Puedes usar Otra.</p>}
      {query.error != null && <CatalogoQueryError error={query.error} />}
      <TextAreaField
        value={value}
        onChange={onChange}
        maxLength={500}
        minRows={2}
        textareaProps={{
          'aria-label': 'Dirección de entrega',
          'aria-invalid': Boolean(error),
          'aria-describedby': error ? 'direccion-entrega-error' : undefined,
          placeholder: 'Calle, número, colonia, ciudad, CP…',
          readOnly: esSede,
        }}
      />
      <p className="text-xs text-ink-muted">Se guarda una copia de la dirección en esta orden. Usa Otra para una entrega distinta.</p>
      {error && <p id="direccion-entrega-error" role="alert" className="text-xs text-danger-fg">{error}</p>}
    </div>
  );
}
