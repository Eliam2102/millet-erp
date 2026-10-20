import { BarraFiltros } from '@/components/erp/data/BarraFiltros';
import { Input } from '@/components/ui/input';
import { useState } from 'react';

export default function Ejemplo() {
  const [q, setQ] = useState('');
  return (
    <BarraFiltros
      busqueda={
        <label>
          Buscar folio
          <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Folio de ejemplo" />
        </label>
      }
      total={q ? '0 documentos ficticios' : '10 documentos ficticios'}
      onLimpiar={() => setQ('')}
    />
  );
}
