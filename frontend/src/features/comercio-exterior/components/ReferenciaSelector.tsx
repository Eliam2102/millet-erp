import { useId, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { apiRequest } from '@/lib/api';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { CE_BASE } from '../api/hooks';
/** Referencias opcionales con el permiso de edición de CE, sin abrir otros módulos. */
export function ReferenciaSelector({
  tipo,
  value,
  onChange,
}: {
  tipo: 'clientes' | 'proveedores';
  value: string | null;
  onChange: (id: string | null) => void;
}) {
  const id = useId();
  const [texto, setTexto] = useState('');
  const q = useDebouncedValue(texto, 300);
  const [seleccion, setSeleccion] = useState<{ id: string; nombre: string } | null>(null);
  const query = useQuery({
    queryKey: ['comercio-exterior', 'referencias', tipo, q],
    enabled: q.trim().length >= 2,
    queryFn: async ({ signal }) =>
      (
        await apiRequest<{ id: string; nombre: string }[]>(
          `${CE_BASE}/referencias/${tipo}?q=${encodeURIComponent(q)}`,
          { signal },
        )
      ).data,
  });
  return (
    <div className="space-y-1.5">
      <Label htmlFor={id}>
        Buscar {tipo === 'clientes' ? 'cliente' : 'proveedor'} por nombre o clave
      </Label>
      <Input
        id={id}
        value={texto}
        onChange={(e) => setTexto(e.target.value)}
        placeholder="Escribe al menos 2 caracteres"
      />
      <select
        aria-label={
          tipo === 'clientes'
            ? 'Seleccionar referencia de cliente'
            : 'Seleccionar referencia de proveedor'
        }
        className="h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3 text-sm"
        value={value ?? ''}
        onChange={(e) => {
          setSeleccion(query.data?.find((x) => x.id === e.target.value) ?? null);
          onChange(e.target.value || null);
        }}
      >
        <option value="">Sin referencia</option>
        {value && !query.data?.some((x) => x.id === value) && (
          <option value={value}>
            {seleccion?.id === value ? seleccion.nombre : 'Referencia vinculada'}
          </option>
        )}
        {query.data?.map((x) => (
          <option key={x.id} value={x.id}>
            {x.nombre}
          </option>
        ))}
      </select>
      {query.isFetching && (
        <p role="status" className="text-xs text-ink-muted">
          Buscando referencias…
        </p>
      )}
      {query.isError && (
        <p role="alert" className="text-xs text-danger-fg">
          No se pudieron consultar las referencias.
        </p>
      )}
      {q.length >= 2 && query.isSuccess && query.data.length === 0 && (
        <p className="text-xs text-ink-muted">Sin coincidencias.</p>
      )}
    </div>
  );
}
