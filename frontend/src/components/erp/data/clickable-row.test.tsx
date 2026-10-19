import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { clickableRowProps } from './clickable-row';

describe('clickableRowProps', () => {
  it('no pone role y Enter/Espacio navegan solo con foco en la fila', () => {
    const nav = vi.fn();
    render(
      <table><tbody>
        <tr data-testid="r" {...clickableRowProps(nav)}><td><input aria-label="x" /></td></tr>
      </tbody></table>,
    );
    const row = screen.getByTestId('r');
    expect(row).not.toHaveAttribute('role', 'button');
    expect(row).toHaveAttribute('tabindex', '0');
    fireEvent.keyDown(row, { key: 'Enter' });
    fireEvent.keyDown(row, { key: ' ' });
    expect(nav).toHaveBeenCalledTimes(2);
    fireEvent.keyDown(screen.getByLabelText('x'), { key: 'Enter' });
    expect(nav).toHaveBeenCalledTimes(2);
  });
});
