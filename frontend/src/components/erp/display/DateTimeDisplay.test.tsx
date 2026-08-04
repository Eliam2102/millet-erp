import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';

describe('<DateTimeDisplay>', () => {
  it('format default: dd/MM/yyyy desde ISO UTC', () => {
    render(<DateTimeDisplay value="2026-05-09T10:00:00Z" />);
    // 2026-05-09T10:00 UTC → 04:00 América/Mexico_City (UTC-6).
    // Para format "date" mostramos solo la fecha local.
    expect(screen.getByText('09/05/2026')).toBeInTheDocument();
  });

  it('variant=datetime: incluye hora en TZ local', () => {
    render(
      <DateTimeDisplay
        value="2026-05-09T20:30:00Z"
        variant="datetime"
      />,
    );
    // 20:30 UTC → 14:30 México (UTC-6).
    expect(screen.getByText('09/05/2026 14:30')).toBeInTheDocument();
  });

  it('variant=long: "9 de mayo de 2026"', () => {
    render(
      <DateTimeDisplay value="2026-05-09T10:00:00Z" variant="long" />,
    );
    expect(screen.getByText(/9 de mayo de 2026/i)).toBeInTheDocument();
  });

  it('DateOnly (YYYY-MM-DD) se muestra como fecha local sin shift', () => {
    render(<DateTimeDisplay value="2026-05-09" />);
    expect(screen.getByText('09/05/2026')).toBeInTheDocument();
  });

  it('null/undefined/empty renderiza guion', () => {
    const { rerender } = render(<DateTimeDisplay value={null} />);
    expect(screen.getByText('—')).toBeInTheDocument();
    rerender(<DateTimeDisplay value={undefined} />);
    expect(screen.getByText('—')).toBeInTheDocument();
    rerender(<DateTimeDisplay value="" />);
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('renderiza <time dateTime={value}> para semantics', () => {
    const { container } = render(
      <DateTimeDisplay value="2026-05-09T10:00:00Z" />,
    );
    const time = container.querySelector('time');
    expect(time).not.toBeNull();
    expect(time).toHaveAttribute('dateTime', '2026-05-09T10:00:00Z');
  });

  it('empty override personalizado', () => {
    render(<DateTimeDisplay value={null} empty="Sin fecha" />);
    expect(screen.getByText('Sin fecha')).toBeInTheDocument();
  });
});
