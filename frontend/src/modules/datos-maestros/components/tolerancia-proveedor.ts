export function interpretarToleranciaMxn(valor: string, esPorcentaje = false):
  { valido: true; monto: number | null } | { valido: false } {
  const texto = valor.trim();
  if (texto === '') return { valido: true, monto: null };
  if (!/^\d{1,14}(\.\d{1,4})?$/.test(texto)) return { valido: false };
  const monto = Number(texto);
  return Number.isFinite(monto) && monto >= 0 && (!esPorcentaje || monto <= 100)
    ? { valido: true, monto }
    : { valido: false };
}
