import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Button } from '@/components/ui/button';
import { MoneyField } from '@/components/erp/forms/MoneyField';
import { DecimalField } from '@/components/erp/forms/DecimalField';
import { TextAreaField } from '@/components/erp/forms/TextAreaField';
import { LineaSchema, type LineaValues } from '@/features/compras/schemas/linea';

/**
 * Integration test: form de prueba con todos los fields conectados a
 * react-hook-form vía <c>Controller</c> + <c>zodResolver(LineaSchema)</c>.
 * Verifica que el wireup end-to-end funciona: setValue, validación
 * onSubmit, errores estructurales por campo.
 */

interface SubmitedValues {
  values: LineaValues;
}

function FormDePrueba({
  onSubmit,
}: {
  onSubmit: (values: LineaValues) => void;
}) {
  const form = useForm<LineaValues>({
    resolver: zodResolver(LineaSchema),
    defaultValues: {
      articuloId: '11111111-1111-4111-8111-111111111111',
      cantidad: 1,
      unidadMedida: 'PZA',
      precioEstimadoMonto: 0,
      precioEstimadoMoneda: 'MXN',
      // Fase E PR2.1: CC-Máquina obligatorio (el resolver lo exige al submit).
      centroCostoId: '0c000000-0000-0000-0000-000000000001',
    },
  });

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} noValidate>
      <Controller
        name="cantidad"
        control={form.control}
        render={({ field }) => (
          <label>
            Cantidad
            <DecimalField value={field.value} onChange={field.onChange} />
          </label>
        )}
      />
      <Controller
        name="precioEstimadoMonto"
        control={form.control}
        render={({ field }) => (
          <label>
            Precio
            <MoneyField
              value={
                field.value != null
                  ? { amount: field.value, currency: form.getValues('precioEstimadoMoneda') }
                  : null
              }
              onChange={(money) => field.onChange(money?.amount ?? null)}
            />
          </label>
        )}
      />
      <Controller
        name="notas"
        control={form.control}
        render={({ field }) => (
          <label>
            Notas
            <TextAreaField
              value={field.value}
              onChange={field.onChange}
              maxLength={500}
            />
          </label>
        )}
      />

      {/* Render de errores para que los tests los encuentren */}
      {form.formState.errors.cantidad && (
        <p data-testid="error-cantidad">
          {form.formState.errors.cantidad.message}
        </p>
      )}
      {form.formState.errors.precioEstimadoMonto && (
        <p data-testid="error-precio">
          {form.formState.errors.precioEstimadoMonto.message}
        </p>
      )}

      <Button type="submit">Enviar</Button>
    </form>
  );
}

describe('Form integration: react-hook-form + Controller + zodResolver(LineaSchema)', () => {
  it('happy path: form con valores válidos llama onSubmit con shape parseado', async () => {
    const submited: SubmitedValues[] = [];
    render(
      <FormDePrueba
        onSubmit={(values) => submited.push({ values })}
      />,
    );

    // Cantidad: cambio a 5
    const inputs = screen.getAllByRole('spinbutton');
    fireEvent.change(inputs[0], { target: { value: '5' } });
    // Precio: cambio a 100
    fireEvent.change(inputs[1], { target: { value: '100' } });

    fireEvent.click(screen.getByRole('button', { name: /enviar/i }));

    // Esperamos que el handle submit se ejecute.
    await new Promise((r) => setTimeout(r, 50));

    expect(submited).toHaveLength(1);
    expect(submited[0].values.cantidad).toBe(5);
    expect(submited[0].values.precioEstimadoMonto).toBe(100);
    expect(submited[0].values.precioEstimadoMoneda).toBe('MXN');
  });

  it('Zod rechaza cantidad ≤ 0 y muestra el error inline', async () => {
    const submited: SubmitedValues[] = [];
    render(
      <FormDePrueba
        onSubmit={(values) => submited.push({ values })}
      />,
    );

    // Cantidad: cambio a 0 (ya es default 1, lo bajamos)
    const inputs = screen.getAllByRole('spinbutton');
    fireEvent.change(inputs[0], { target: { value: '0' } });
    fireEvent.click(screen.getByRole('button', { name: /enviar/i }));
    await new Promise((r) => setTimeout(r, 50));

    expect(submited).toHaveLength(0);
    expect(screen.getByTestId('error-cantidad')).toHaveTextContent(
      /mayor a 0/i,
    );
  });

  it('Zod rechaza precio ≤ 0 y NO ejecuta onSubmit', async () => {
    const submited: SubmitedValues[] = [];
    render(
      <FormDePrueba
        onSubmit={(values) => submited.push({ values })}
      />,
    );

    // precio queda en 0 (default).
    fireEvent.click(screen.getByRole('button', { name: /enviar/i }));
    await new Promise((r) => setTimeout(r, 50));

    expect(submited).toHaveLength(0);
    expect(screen.getByTestId('error-precio')).toBeInTheDocument();
  });

  it('TextAreaField propaga null al field cuando está vacío', async () => {
    const submited: SubmitedValues[] = [];
    render(
      <FormDePrueba
        onSubmit={(values) => submited.push({ values })}
      />,
    );

    // Setear valores válidos
    const inputs = screen.getAllByRole('spinbutton');
    fireEvent.change(inputs[0], { target: { value: '5' } });
    fireEvent.change(inputs[1], { target: { value: '100' } });

    // Tipear y luego borrar las notas
    fireEvent.change(screen.getByRole('textbox'), {
      target: { value: 'Hola' },
    });
    fireEvent.change(screen.getByRole('textbox'), { target: { value: '' } });

    fireEvent.click(screen.getByRole('button', { name: /enviar/i }));
    await new Promise((r) => setTimeout(r, 50));

    expect(submited).toHaveLength(1);
    // Zod nullish acepta null/undefined; el field.value llega como null.
    expect(submited[0].values.notas == null).toBe(true);
  });
});
