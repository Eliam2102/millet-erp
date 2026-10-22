import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { Input } from '@/components/ui/input';
import { dinero } from '../api/types';
interface Documento {
  id: string;
  facturaId: string;
  referencia: string;
  baseMxn: number;
}
export function DocumentoProveedorSelector({
  activoId,
  credito,
  facturaId,
  value,
  onChange,
}: {
  activoId: string;
  credito: boolean;
  facturaId: string | null;
  value: string;
  onChange: (id: string) => void;
}) {
  const [q, setQ] = useState('');
  const query = useQuery({
    queryKey: ['activos-fijos', 'documentos', activoId, credito, facturaId, q],
    queryFn: async () => {
      const params = new URLSearchParams({ activoId, notaCredito: String(credito), q });
      if (facturaId) params.set('facturaId', facturaId);
      return (await apiRequest<Documento[]>(`/api/v1/activos-fijos/documentos-proveedor?${params}`))
        .data;
    },
  });
  return (
    <div className="space-y-2">
      {!credito && (
        <Input
          aria-label="Buscar factura por folio"
          placeholder="Buscar por folio"
          value={q}
          onChange={(e) => setQ(e.target.value)}
        />
      )}
      <select
        required
        aria-label={credito ? 'Nota de crédito del proveedor' : 'Factura de mejora'}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="h-ctl-md w-full rounded-md border border-line-control bg-surface-card px-2"
      >
        <option value="">
          {query.isPending ? 'Cargando documentos…' : 'Selecciona un documento'}
        </option>
        {query.data?.map((d) => (
          <option key={d.id} value={d.id}>
            {d.referencia} · {dinero(d.baseMxn)}
          </option>
        ))}
      </select>
      {query.error && (
        <p role="alert" className="text-danger-fg">
          {query.error.message}
        </p>
      )}
      {query.data?.length === 0 && (
        <p className="text-xs text-ink-muted">Sin documentos vigentes disponibles.</p>
      )}
    </div>
  );
}
