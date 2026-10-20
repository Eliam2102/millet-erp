import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { RecurrenteBadge } from '@/features/compras/components/IndicadoresPendiente';

describe('RecurrenteBadge', () => {
  it('true pinta "Recurrente"', () => {
    render(<RecurrenteBadge esRecurrente={true} />);
    expect(screen.getByText('Recurrente')).toBeInTheDocument();
  });

  it('false pinta "No recurrente"', () => {
    render(<RecurrenteBadge esRecurrente={false} />);
    expect(screen.getByText('No recurrente')).toBeInTheDocument();
  });

  it.each([null, undefined])('%s no pinta nada', (valor) => {
    const { container } = render(<RecurrenteBadge esRecurrente={valor} />);
    expect(container).toBeEmptyDOMElement();
  });
});
