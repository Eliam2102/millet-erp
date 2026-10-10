import { Link } from '@tanstack/react-router';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { useFormasPago } from '@/modules/catalogos/api/formas-pago';
import type { ComprobanteDetalleResponse } from '../api/types';
import { estadoCobroFactura } from '../lib/estado-cobro-factura';
import { RegistrarCobroCard } from './RegistrarCobroCard';

const importe = (valor: number, moneda: string) =>
  `${valor.toLocaleString('es-MX', { style: 'currency', currency: moneda })} ${moneda}`;

export function CobroFacturaPanel({ factura: f }: { factura: ComprobanteDetalleResponse }) {
  const estado = estadoCobroFactura(f);
  const cobro = f.cobroMostrador;
  const catalogo = useFormasPago();

  return (
    <Card role="region" aria-label="Cobro de la factura">
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          Cobro
          <Badge variant={estado === 'Cobrada' ? 'success' : estado === 'Parcial (PPD)' ? 'info' : 'neutral'}>
            {estado}
          </Badge>
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-3 text-sm text-ink">
        {f.metodoPago === 'PPD' ? (
          <>
            <p>Se cobra con complemento de pago (REP)</p>
            <dl className="grid grid-cols-2 gap-2">
              <dt className="text-ink-muted">Pagado por REP</dt>
              <dd className="text-right tabular-nums">{importe(f.pagadoPorRep, f.moneda)}</dd>
              <dt className="text-ink-muted">Saldo</dt>
              <dd className="text-right tabular-nums">{importe(f.totalPorCobrar, f.moneda)}</dd>
            </dl>
            <Link to="/facturacion/repp" className="text-brand underline">Ver bandeja de REP</Link>
          </>
        ) : f.metodoPago === 'PUE' && cobro != null ? (
          <>
            <dl className="grid grid-cols-2 gap-2">
              <dt className="text-ink-muted">Fecha de cobro</dt>
              <dd>{new Date(cobro.fechaCobro).toLocaleString('es-MX')}</dd>
              <dt className="text-ink-muted">Caja</dt>
              <dd>{cobro.sesion.cajaNombre}</dd>
              <dt className="text-ink-muted">Importe cobrado</dt>
              <dd className="text-right tabular-nums">{importe(cobro.total, f.moneda)}</dd>
            </dl>
            <h3 className="text-sm font-medium">Formas de pago</h3>
            <ul className="space-y-2">
              {cobro.formasPago.map((p, i) => (
                <li key={i} className="flex justify-between gap-3">
                  <span>
                    {catalogo.data?.find((forma) => forma.claveSat === p.formaPago)?.descripcion ?? `Forma SAT ${p.formaPago}`}
                    {p.referencia ? ` · ${p.referencia}` : ''}
                  </span>
                  <span className="text-right tabular-nums">{importe(p.importe, f.moneda)}</span>
                </li>
              ))}
            </ul>
          </>
        ) : f.metodoPago === 'PUE' ? (
          f.totalPorCobrar > 0 ? (
            f.estado === 'Timbrado' && (
              <RegistrarCobroCard comprobanteId={f.id} folio={f.folio} total={f.totalPorCobrar} moneda={f.moneda} />
            )
          ) : (
            <p>Sin monto por cobrar: las notas de crédito acreditan el total de la factura.</p>
          )
        ) : (
          <p>Método de pago por confirmar.</p>
        )}
      </CardContent>
    </Card>
  );
}
