export function interpretarToleranciaMxn(valor: string):
  { valido: true; monto: number | null } | { valido: false } {
  const texto = valor.trim();
  if (texto === '') return { valido: true, monto: null };
  if (!/^\d{1,14}(\.\d{1,4})?$/.test(texto)) return { valido: false };
  const monto = Number(texto);
  return Number.isFinite(monto) && monto >= 0
    ? { valido: true, monto }
    : { valido: false };
}
