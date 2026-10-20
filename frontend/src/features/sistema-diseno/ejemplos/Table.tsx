import {
  Table,
  TableHeader,
  TableHead,
  TableBody,
  TableRow,
  TableCell,
} from '@/components/ui/table';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';

export default function Ejemplo() {
  return (
    <Table>
      <caption>Importes ficticios</caption>
      <TableHeader>
        <TableRow>
          <TableHead>Folio</TableHead>
          <TableHead className="text-right">Importe</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow>
          <TableCell className="font-mono">[DEMO-001]</TableCell>
          <TableCell className="text-right">
            <MoneyDisplay amount={100} currency="MXN" />
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
  );
}
