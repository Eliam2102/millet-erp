import { useState } from 'react';
import { useLibroQuery } from '../api/libro';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';
import { Input } from '@/components/ui/input';
export function ActivoMejoradoSelector({
  value,
  excluirId,
  onChange,
}: {
  value: string | null;
  excluirId?: string;
  onChange: (id: string | null) => void;
}) {
  const [q, setQ] = useState('');
  const query = useLibroQuery<{ id: string; numero: string; descripcion: string }[]>(
    `activos-mejorables?q=${encodeURIComponent(q)}${value ? `&seleccionado=${value}` : ''}`,
    true,
  );
  return (
    <div className="space-y-2">
      <Input
        aria-label="Buscar activo que mejora"
        placeholder="Buscar por número o descripción"
        value={q}
        onChange={(e) => setQ(e.target.value)}
      />
      <CatalogoEagerCombobox
        items={query.data?.filter((a) => a.id !== excluirId) ?? []}
        loading={query.isLoading}
        error={query.error}
        value={value}
        onChange={onChange}
        itemToLabel={(a) => `${a.numero} · ${a.descripcion}`}
        renderTrigger={(a) => `${a.numero} · ${a.descripcion}`}
        renderItem={(a) => (
          <span>
            {a.numero} · {a.descripcion}
          </span>
        )}
        ariaLabel="Activo que mejora"
        placeholder="Seleccione activo"
        emptyListText="Sin activos vigentes. Ajuste la búsqueda."
      />
    </div>
  );
}
