import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Controller, useForm } from 'react-hook-form';
import { Field } from '@/components/erp/forms/Field';
import { Input } from '@/components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
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

function ConControllerDirecto({ error }: { error?: string }) {
  const { control } = useForm<{ nombre: string; tipo: string; monto: number | null }>({
    defaultValues: { nombre: '', tipo: 'a', monto: null },
  });
  return (
    <>
      <Field label="Nombre" required error={error}>
        <Controller control={control} name="nombre" render={({ field }) => <Input {...field} />} />
      </Field>
      <Field label="Tipo">
        <Controller
          control={control}
          name="tipo"
          render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent><SelectItem value="a">A</SelectItem></SelectContent>
            </Select>
          )}
        />
      </Field>
      <Field label="Monto">
        <Controller
          control={control}
          name="monto"
          render={({ field }) => <DecimalField value={field.value} onChange={field.onChange} />}
        />
      </Field>
    </>
  );
}

describe('<Field>', () => {
  it('asocia label con Controller directo (Input, Select, DecimalField)', () => {
    render(<ConControllerDirecto error="Obligatorio" />);
    const nombre = screen.getByLabelText(/Nombre/);
    expect(nombre).toHaveAttribute('aria-required', 'true');
    expect(nombre).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByLabelText('Tipo')).toHaveAttribute('role', 'combobox');
    expect(screen.getByLabelText('Monto')).toBeInTheDocument();
  });

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
