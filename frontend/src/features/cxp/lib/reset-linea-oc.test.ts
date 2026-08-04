import { describe, expect, it, vi } from 'vitest';
import type { UseFormGetValues, UseFormSetValue } from 'react-hook-form';
import { resetLineaOcIds } from '@/features/cxp/lib/reset-linea-oc';
import type { CapturarFacturaValues } from '@/features/cxp/schemas/factura';

describe('resetLineaOcIds', () => {
  it('setea lineaOcId=null en todas las líneas, sin tocar otros campos', () => {
    const getValuesMock = vi
      .fn()
      .mockReturnValue([
        { lineaOcId: 'linea-a' },
        { lineaOcId: 'linea-b' },
        { lineaOcId: null },
      ]);
    const setValueMock = vi.fn();

    resetLineaOcIds(
      getValuesMock as unknown as UseFormGetValues<CapturarFacturaValues>,
      setValueMock as unknown as UseFormSetValue<CapturarFacturaValues>,
    );

    // Una llamada por línea (incluida la que ya era null: idempotente y simple).
    expect(setValueMock).toHaveBeenCalledTimes(3);
    expect(setValueMock).toHaveBeenNthCalledWith(1, 'lineas.0.lineaOcId', null, {
      shouldValidate: true,
    });
    expect(setValueMock).toHaveBeenNthCalledWith(2, 'lineas.1.lineaOcId', null, {
      shouldValidate: true,
    });
    expect(setValueMock).toHaveBeenNthCalledWith(3, 'lineas.2.lineaOcId', null, {
      shouldValidate: true,
    });
  });

  it('sin líneas no hace nada', () => {
    const getValuesMock = vi.fn().mockReturnValue(undefined);
    const setValueMock = vi.fn();

    resetLineaOcIds(
      getValuesMock as unknown as UseFormGetValues<CapturarFacturaValues>,
      setValueMock as unknown as UseFormSetValue<CapturarFacturaValues>,
    );

    expect(setValueMock).not.toHaveBeenCalled();
  });
});
