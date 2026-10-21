import { Input } from '@/components/ui/input';

export default function Ejemplo() {
  return (
    <div className="space-y-3">
      <label>
        Referencia ficticia
        <Input placeholder="Ej. TRF-0412" />
      </label>
      <label>
        Referencia obligatoria *
        <Input required aria-invalid="true" aria-describedby="input-error" />
      </label>
      <p id="input-error" className="text-danger-fg">
        Escribe una referencia para continuar.
      </p>
      <label>
        Referencia bloqueada
        <Input disabled title="El ejemplo ya está autorizado" defaultValue="Autorizada" />
      </label>
    </div>
  );
}
