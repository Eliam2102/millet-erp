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

  it('variant=corta: "9 may" en el año actual y con año en otro', () => {
    const anio = new Date().getFullYear();
    render(<DateTimeDisplay value={`${anio}-05-09`} variant="corta" />);
    expect(screen.getByText('9 may')).toBeInTheDocument();
    render(<DateTimeDisplay value="2020-09-21" variant="corta" />);
    expect(screen.getByText('21 sep 2020')).toBeInTheDocument();
  });

  it('variant=media: "21 sep 2026"; DateOnly sin corrimiento y ISO en TZ México', () => {
    render(<DateTimeDisplay value="2026-09-21" variant="media" />);
    expect(screen.getByText('21 sep 2026')).toBeInTheDocument();
    // 2026-09-22T03:00Z → 21 sep 21:00 México.
    render(<DateTimeDisplay value="2026-09-22T03:00:00Z" variant="media" />);
    expect(screen.getAllByText('21 sep 2026')).toHaveLength(2);
  });

  it('medianoche UTC se trata como fecha de calendario (sin día anterior)', () => {
    render(<DateTimeDisplay value="2026-10-09T00:00:00+00:00" variant="media" />);
    expect(screen.getByText('9 oct 2026')).toBeInTheDocument();
  });

  it('vencida: text-danger-fg font-medium', () => {
    render(<DateTimeDisplay value="2026-09-21" variant="media" vencida />);
    const el = screen.getByText('21 sep 2026');
    expect(el).toHaveClass('text-danger-fg', 'font-medium');
  });
});
