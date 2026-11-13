import { useTamanoPagina } from '@/components/erp/data/useTamanoPagina';
import { PaginacionTabla } from '@/components/erp/data/PaginacionTabla';
import { useState } from 'react';

export default function Ejemplo() {
  const { limit } = useTamanoPagina();
  const [offset, setOffset] = useState(0);
  return (
    <div>
      <PaginacionTabla total={23} offset={offset} limit={limit} onChange={setOffset} />
      <p>Anterior se deshabilita al inicio; siguiente al llegar al último registro.</p>
    </div>
  );
}
