import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ResumenImportes } from './ResumenImportes';
describe('ResumenImportes', () => {
  it('ordena importes, conserva ceros y separa pago y acciones', () => {
    const { container } = render(
      <ResumenImportes
        moneda="MXN"
        subtotal={100}
        descuento={0}
        ivaTrasladado={16}
        otrosTrasladados={[{ etiqueta: 'IEPS', monto: 2 }]}
        ivaRetenido={-4}
        isrRetenido={3}
        otrasRetenciones={[{ etiqueta: 'Otra retención', monto: 1 }]}
        total={110}
        estadoPago={[
          { etiqueta: 'Pagado', monto: 10 },
          { etiqueta: 'Saldo pendiente', monto: 100, destacado: true },
        ]}
        acciones={<button>Emitir</button>}
      />,
    );
    const resumen = screen.getByLabelText('Resumen de importes');
    expect(Array.from(resumen.querySelectorAll('dt')).map((n) => n.textContent)).toEqual([
      'Subtotal',
      'Descuento',
      'IVA trasladado',
      'IEPS',
      'IVA retenido',
      'ISR retenido',
      'Otra retención',
      'Total',
    ]);
    expect(screen.getByText('$0.00')).toBeInTheDocument();
    for (const importe of ['−$4.00', '−$3.00', '−$1.00'])
      expect(screen.getByText(importe)).toBeInTheDocument();
    expect(resumen.lastElementChild).toHaveClass('border-t', 'text-base', 'font-semibold');
    const pago = screen.getByLabelText('Estado de pago');
    expect(resumen.nextElementSibling).toBe(pago);
    expect(pago).toHaveClass('border-t');
    expect(pago.nextElementSibling).toContainElement(
      screen.getByRole('button', { name: 'Emitir' }),
    );
    expect(container.firstElementChild).toHaveClass('ml-auto', 'max-w-[380px]');
  });
  it('omite filas opcionales ausentes y formatea moneda', () => {
    const { container } = render(<ResumenImportes moneda="USD" subtotal={0} total={0} />);
    expect(container.querySelectorAll('dt')).toHaveLength(2);
    expect(screen.getAllByText('$0.00 USD')).toHaveLength(2);
    expect(screen.queryByLabelText('Estado de pago')).not.toBeInTheDocument();
  });
});
