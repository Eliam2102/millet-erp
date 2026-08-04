import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { TextAreaField } from '@/components/erp/forms/TextAreaField';

describe('<TextAreaField>', () => {
  it('renderiza value en el textarea', () => {
    render(<TextAreaField value="Hola" onChange={() => {}} />);
    expect((screen.getByRole('textbox') as HTMLTextAreaElement).value).toBe(
      'Hola',
    );
  });

  it('onChange con string al tipear', () => {
    const onChange = vi.fn();
    render(<TextAreaField value="" onChange={onChange} />);
    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'Texto' } });
    expect(onChange).toHaveBeenCalledWith('Texto');
  });

  it('input vacío → null', () => {
    const onChange = vi.fn();
    render(<TextAreaField value="Hola" onChange={onChange} />);
    fireEvent.change(screen.getByRole('textbox'), { target: { value: '' } });
    expect(onChange).toHaveBeenCalledWith(null);
  });

  it('counter visible cuando length ≥ 80% del maxLength', () => {
    render(
      <TextAreaField
        value={'a'.repeat(80)}
        onChange={() => {}}
        maxLength={100}
      />,
    );
    expect(screen.getByText('80 / 100')).toBeInTheDocument();
  });

  it('counter NO visible bajo el 80%', () => {
    render(
      <TextAreaField value="hola" onChange={() => {}} maxLength={500} />,
    );
    expect(screen.queryByText(/\/\s*500/)).not.toBeInTheDocument();
  });

  it('respeta maxLength en el textarea', () => {
    render(
      <TextAreaField value="hola" onChange={() => {}} maxLength={10} />,
    );
    expect(screen.getByRole('textbox')).toHaveAttribute('maxLength', '10');
  });
});
