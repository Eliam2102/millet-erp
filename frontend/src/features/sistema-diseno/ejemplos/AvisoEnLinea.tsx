import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';

export default function Ejemplo() {
  return (
    <div className="space-y-3">
      <AvisoEnLinea tono="advertencia" titulo="Falta recepción">
        Registra la recepción ficticia para habilitar la autorización.
      </AvisoEnLinea>
      <AvisoEnLinea tono="error">
        El importe no coincide. Corrige el ejemplo antes de continuar.
      </AvisoEnLinea>
    </div>
  );
}
