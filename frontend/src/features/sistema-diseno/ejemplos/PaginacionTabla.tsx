import { PaginacionTabla } from '@/components/erp/data/PaginacionTabla';
import { useState } from 'react';

export default function Ejemplo() {
  const [offset, setOffset] = useState(0);
  return (
    <div>
      <PaginacionTabla total={23} offset={offset} limit={10} tamanos={[]} onChange={setOffset} />
      <p>Anterior se deshabilita al inicio; siguiente al llegar al último registro.</p>
    </div>
  );
}
