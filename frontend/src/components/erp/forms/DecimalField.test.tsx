import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { DecimalField } from '@/components/erp/forms/DecimalField';

describe('<DecimalField>', () => {
  it('renderiza el value como string en el input', () => {
    render(<DecimalField value={3.14159} onChange={() => {}} />);
    expect((screen.getByRole('spinbutton') as HTMLInputElement).value).toBe(
      '3.14159',
    );
  });

  it('onChange con número parseado', () => {
    const onChange = vi.fn();
    render(<DecimalField value={null} onChange={onChange} />);
    fireEvent.change(screen.getByRole('spinbutton'), { target: { value: '7.5' } });
    expect(onChange).toHaveBeenCalledWith(7.5);
  });

  it('input vacío → null (no NaN)', () => {
    const onChange = vi.fn();
    render(<DecimalField value={5} onChange={onChange} />);
    fireEvent.change(screen.getByRole('spinbutton'), { target: { value: '' } });
    expect(onChange).toHaveBeenCalledWith(null);
  });

  it('step default = 0.00001 (5 decimales)', () => {
    render(<DecimalField value={null} onChange={() => {}} />);
    expect(screen.getByRole('spinbutton')).toHaveAttribute('step', '0.00001');
  });

  it('respeta min y max props', () => {
    render(<DecimalField value={null} onChange={() => {}} min={0} max={100} />);
    const input = screen.getByRole('spinbutton');
    expect(input).toHaveAttribute('min', '0');
    expect(input).toHaveAttribute('max', '100');
  });
});
