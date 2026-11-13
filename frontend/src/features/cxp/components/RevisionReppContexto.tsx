import { Link } from '@tanstack/react-router';
import { AvisoEnLinea } from '@/components/erp';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { RevisionRepp } from '../api/types';

export function RevisionReppContexto({ contexto }: { contexto: RevisionRepp | null | undefined }) {
  const puedeVerRepp = useHasPermission(PermisosCanonicos.TesoreriaReppRegistrar);
  if (!contexto?.pendiente) return null;
  return (
    <AvisoEnLinea tono="advertencia" titulo="Falta el complemento de pago del proveedor">
      <div className="space-y-2">
        <p>Esta revisión corresponde al REPP de un pago. El estado «En revisión» es el actual; no indica que la factura nunca se haya autorizado o pagado.</p>
        {contexto.ultimaAutorizacionEn && <p>Autorización anterior: <DateTimeDisplay value={contexto.ultimaAutorizacionEn} variant="datetime" />.</p>}
        {contexto.pagos.length > 0 ? (
          <ul className="space-y-2">
            {contexto.pagos.map((pago) => (
              <li key={pago.pagoId}>
                <span>Pago del <DateTimeDisplay value={pago.fechaPago} /></span>
                <div>
                  Importe: {pago.moneda ? <MoneyDisplay amount={pago.importe} currency={pago.moneda} /> : <span className="tabular-nums">{pago.importe.toLocaleString('es-MX', { minimumFractionDigits: 2 })} · Sin moneda</span>}
                  {' · '}Cubierto por REPP: {pago.moneda ? <MoneyDisplay amount={pago.cubiertoRepp} currency={pago.moneda} /> : <span className="tabular-nums">{pago.cubiertoRepp.toLocaleString('es-MX', { minimumFractionDigits: 2 })}</span>}
                </div>
              </li>
            ))}
          </ul>
        ) : <p>Revisión sin pagos identificados en el historial disponible. Solicita a Tesorería que verifique el pago y el complemento antes de liberar.</p>}
        <p>Tesorería debe registrar y validar el XML del complemento, vinculándolo al pago de esta factura. Un PDF o una aclaración escrita no sustituyen esa validación.</p>
        {puedeVerRepp ? <Link to="/tesoreria/repp" className="text-brand underline">Abrir Tesorería → REPP de proveedor</Link> : <p>Solicita a Tesorería la revisión en «REPP de proveedor». Tu perfil no tiene acceso a esa pantalla.</p>}
      </div>
    </AvisoEnLinea>
  );
}
