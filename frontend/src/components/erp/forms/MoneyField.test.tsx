import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MoneyField } from '@/components/erp/forms/MoneyField';

describe('<MoneyField>', () => {
  it('renderiza con value Money y muestra el monto', () => {
    render(
      <MoneyField
        value={{ amount: 100, currency: 'MXN' }}
        onChange={() => {}}
      />,
    );
    const input = screen.getByRole('spinbutton') as HTMLInputElement;
    expect(input.value).toBe('100');
  });

  it('onChange con número parseado al tipear', () => {
    const onChange = vi.fn();
    render(
      <MoneyField
        value={{ amount: 0, currency: 'MXN' }}
        onChange={onChange}
      />,
    );
    fireEvent.change(screen.getByRole('spinbutton'), { target: { value: '250.50' } });
    expect(onChange).toHaveBeenCalledWith({ amount: 250.5, currency: 'MXN' });
  });

  it('input vacío → onChange(null)', () => {
    const onChange = vi.fn();
    render(
      <MoneyField
        value={{ amount: 100, currency: 'MXN' }}
        onChange={onChange}
      />,
    );
    fireEvent.change(screen.getByRole('spinbutton'), { target: { value: '' } });
    expect(onChange).toHaveBeenCalledWith(null);
  });

  it('default currency es MXN cuando value es null', () => {
    render(<MoneyField value={null} onChange={() => {}} />);
    // El select de moneda muestra MXN como triggered value (visible
    // text dentro del trigger).
    expect(screen.getByText('MXN')).toBeInTheDocument();
  });

  it('input numérico con inputMode=decimal', () => {
    render(<MoneyField value={null} onChange={() => {}} />);
    expect(screen.getByRole('spinbutton')).toHaveAttribute(
      'inputmode',
      'decimal',
    );
  });
});
