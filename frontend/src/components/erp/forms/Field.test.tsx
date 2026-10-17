import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Controller, useForm } from 'react-hook-form';
import { Field } from '@/components/erp/forms/Field';
import { DecimalField } from '@/components/erp/forms/DecimalField';

function ConRegister({ error }: { error?: string }) {
  const { register } = useForm<{ nombre: string }>();
  return (
    <Field label="Nombre" required hint="Ayuda" error={error}>
      <input {...register('nombre')} />
    </Field>
  );
}

function ConController({ error }: { error?: string }) {
  const { control } = useForm<{ monto: number | null }>({ defaultValues: { monto: null } });
  return (
    <Controller
      control={control}
      name="monto"
      render={({ field }) => (
        <Field label="Monto" optional error={error}>
          {(c) => <DecimalField value={field.value} onChange={field.onChange} inputProps={c} />}
        </Field>
      )}
    />
  );
}

describe('<Field>', () => {
  it('asocia label con register, aria-required y describedby solo del hint', () => {
    render(<ConRegister />);
    const input = screen.getByLabelText(/Nombre/);
    expect(input).toHaveAttribute('aria-required', 'true');
    expect(input).not.toHaveAttribute('aria-invalid');
    expect(input.getAttribute('aria-describedby')).toBe(screen.getByText('Ayuda').id);
  });

  it('con error: aria-invalid y describedby incluye el mensaje', () => {
    render(<ConRegister error="Requerido" />);
    const input = screen.getByLabelText(/Nombre/);
    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(input.getAttribute('aria-describedby')).toContain(screen.getByText('Requerido').id);
  });

  it('sin hint ni error no hay aria-describedby', () => {
    render(
      <Field label="X">
        <input />
      </Field>,
    );
    expect(screen.getByLabelText('X')).not.toHaveAttribute('aria-describedby');
  });

  it('Controller + DecimalField: el id llega al <input>', () => {
    render(<ConController error="Inválido" />);
    const input = screen.getByLabelText(/Monto/);
    expect(input.tagName).toBe('INPUT');
    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByText('(opcional)')).toBeInTheDocument();
  });

  it('respeta el id propio del hijo', () => {
    render(
      <Field label="Y">
        <input id="mio" />
      </Field>,
    );
    expect(screen.getByLabelText('Y').id).toBe('mio');
  });
});
