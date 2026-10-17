import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { BusquedaFolio } from '@/components/erp/data/BusquedaFolio';

afterEach(() => vi.useRealTimers());

describe('<BusquedaFolio>', () => {
  it('confirma el texto una sola vez, 300 ms después de la última tecla', () => {
    vi.useFakeTimers();
    const onCommit = vi.fn();
    render(<BusquedaFolio aria-label="Buscar" valor={undefined} onCommit={onCommit} />);
    const input = screen.getByLabelText('Buscar');
    fireEvent.change(input, { target: { value: 'ENT' } });
    fireEvent.change(input, { target: { value: 'ENT-0' } });
    act(() => {
      vi.advanceTimersByTime(299);
    });
    expect(onCommit).not.toHaveBeenCalled();
    act(() => {
      vi.advanceTimersByTime(1);
    });
    expect(onCommit).toHaveBeenCalledTimes(1);
    expect(onCommit).toHaveBeenCalledWith('ENT-0');
  });

  it('vacía el input cuando el valor de la URL se limpia', () => {
    const { rerender } = render(
      <BusquedaFolio aria-label="Buscar" valor="ENT" onCommit={() => {}} />,
    );
    expect(screen.getByLabelText('Buscar')).toHaveValue('ENT');
    rerender(<BusquedaFolio aria-label="Buscar" valor={undefined} onCommit={() => {}} />);
    expect(screen.getByLabelText('Buscar')).toHaveValue('');
  });
});
