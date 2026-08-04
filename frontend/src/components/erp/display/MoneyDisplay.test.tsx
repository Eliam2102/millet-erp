import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';

describe('<MoneyDisplay>', () => {
  it('formatea MXN sin sufijo de código', () => {
    render(<MoneyDisplay money={{ amount: 1234.56, currency: 'MXN' }} />);
    expect(screen.getByText('$1,234.56')).toBeInTheDocument();
  });

  it('formatea USD con sufijo de código', () => {
    render(<MoneyDisplay money={{ amount: 50, currency: 'USD' }} />);
    expect(screen.getByText('$50.00 USD')).toBeInTheDocument();
  });

  it('acepta amount + currency separados (shape de LineaResponse)', () => {
    render(<MoneyDisplay amount={99.5} currency="MXN" />);
    expect(screen.getByText('$99.50')).toBeInTheDocument();
  });

  it('null/undefined renderiza guion', () => {
    render(<MoneyDisplay money={null} />);
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('amount null + currency null renderiza guion (caller pasa los dos sueltos)', () => {
    render(<MoneyDisplay amount={null} currency={null} />);
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('clase tabular-nums para alineación en tablas', () => {
    const { container } = render(
      <MoneyDisplay money={{ amount: 100, currency: 'MXN' }} />,
    );
    expect(container.querySelector('.tabular-nums')).not.toBeNull();
  });
});
