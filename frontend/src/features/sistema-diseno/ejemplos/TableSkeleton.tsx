import { TableSkeleton } from '@/components/erp/feedback/TableSkeleton';

export default function Ejemplo() {
  return (
    <TableSkeleton
      rows={3}
      columns={[
        { width: 'w-24', label: 'Folio' },
        { width: 'w-32', label: 'Importe' },
      ]}
    />
  );
}
