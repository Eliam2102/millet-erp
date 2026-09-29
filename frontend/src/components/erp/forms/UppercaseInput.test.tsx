import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { useState } from 'react';
import { UppercaseInput } from '@/components/erp/forms/UppercaseInput';

describe('<UppercaseInput>', () => {
  it('entrega el valor en mayúsculas a onChange', () => {
    const onChange = vi.fn((e) => e.target.value);
    render(<UppercaseInput onChange={onChange} />);
    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'ses010101aaa' } });
    expect(onChange).toHaveReturnedWith('SES010101AAA');
  });

  it('en modo controlado muestra el texto en mayúsculas', () => {
    function Controlado() {
      const [v, setV] = useState('');
      return <UppercaseInput value={v} onChange={(e) => setV(e.target.value)} />;
    }
    render(<Controlado />);
    const input = screen.getByRole('textbox') as HTMLInputElement;
    fireEvent.change(input, { target: { value: 'abc' } });
    expect(input.value).toBe('ABC');
  });
});
