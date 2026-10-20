import {
  Select,
  SelectTrigger,
  SelectValue,
  SelectContent,
  SelectItem,
} from '@/components/ui/select';

export default function Ejemplo() {
  return (
    <div className="space-y-3">
      <label htmlFor="select-demo">Sucursal ficticia</label>
      <Select id="select-demo">
        <SelectTrigger>
          <SelectValue placeholder="Selecciona una sucursal" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="a">Sucursal Circuito</SelectItem>
          <SelectItem value="b">Sucursal Centro</SelectItem>
        </SelectContent>
      </Select>
      <Select disabled>
        <SelectTrigger aria-label="Sucursal bloqueada" title="Selecciona primero una empresa">
          <SelectValue placeholder="Selecciona primero una empresa" />
        </SelectTrigger>
      </Select>
    </div>
  );
}
