import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { DatePickerField } from '@/components/erp/forms/DatePickerField';
import { esDiaDeshabilitado } from '@/components/erp/forms/date-picker-utils';

describe('<DatePickerField>', () => {
  it('renderiza placeholder cuando value es null', () => {
    render(
      <DatePickerField
        value={null}
        onChange={() => {}}
        placeholder="Pick a date"
      />,
    );
    expect(screen.getByText('Pick a date')).toBeInTheDocument();
  });

  it('renderiza la fecha en formato dd MMM yyyy en español cuando hay value', () => {
    render(<DatePickerField value="2026-05-09" onChange={() => {}} />);
    // 9 de mayo 2026 → "09 may 2026" en es-MX (date-fns format short
    // mes con locale es).
    expect(screen.getByText(/09\s*may\s*2026/i)).toBeInTheDocument();
  });

  it('NO shifta de día con DateOnly en TZ negativa (parseo manual)', () => {
    // 2026-05-09 → debe mostrar 09, no 08 (ese era el bug del parseISO+TZ).
    const { container } = render(
      <DatePickerField value="2026-05-09" onChange={() => {}} />,
    );
    expect(container.textContent).toContain('09');
    expect(container.textContent).not.toContain('08');
  });

  it('value inválido (string que no parsea) muestra placeholder', () => {
    render(
      <DatePickerField
        value="not-a-date"
        onChange={() => {}}
        placeholder="Pick"
      />,
    );
    expect(screen.getByText('Pick')).toBeInTheDocument();
  });

  it('disabled propaga al trigger', () => {
    render(<DatePickerField value={null} onChange={() => {}} disabled />);
    expect(screen.getByRole('button')).toBeDisabled();
  });
});

describe('esDiaDeshabilitado (límite a granularidad de día)', () => {
  // react-day-picker pasa cada celda de día a MEDIANOCHE local; los callers
  // pasan new Date() (= ahora, con hora). El día del límite debe seguir
  // seleccionable — el off-by-one era comparar medianoche < ahora-con-hora.
  const hoy = new Date(2026, 5, 26); //        26-jun-2026 00:00 (celda del calendario)
  const ayer = new Date(2026, 5, 25);
  const manana = new Date(2026, 5, 27);
  const ahoraConHora = new Date(2026, 5, 26, 10, 30); // "hoy" 10:30 (lo que pasa new Date())

  it('HOY es seleccionable aunque minDate traiga hora (el off-by-one)', () => {
    expect(esDiaDeshabilitado(hoy, ahoraConHora)).toBe(false);
  });

  it('AYER queda deshabilitado con ese minDate', () => {
    expect(esDiaDeshabilitado(ayer, ahoraConHora)).toBe(true);
  });

  it('maxDate: el día del máximo (con hora) es seleccionable; el siguiente no', () => {
    expect(esDiaDeshabilitado(hoy, undefined, ahoraConHora)).toBe(false);
    expect(esDiaDeshabilitado(manana, undefined, ahoraConHora)).toBe(true);
  });

  it('sin límites: nunca deshabilita', () => {
    expect(esDiaDeshabilitado(hoy)).toBe(false);
  });
});
