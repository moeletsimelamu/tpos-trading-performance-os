import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

// Amounts are whole cents. Opening balances may be negative (an overdrawn account) but never zero.
// An empty value is left to Validators.required, so the same validator serves optional fields such as fees.
export function amountValidator(allowNegative: () => boolean = () => false): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value = control.value;
    if (value === null || value === '') return null;
    const n = Number(value);
    if (!Number.isFinite(n)) return { amount: 'Enter a number.' };
    if (Math.round(n * 100) / 100 !== n) return { amount: 'Use at most 2 decimal places.' };
    if (allowNegative() ? n === 0 : n <= 0) return { amount: allowNegative() ? 'Must not be zero.' : 'Must be greater than zero.' };
    return null;
  };
}

// A rate as printed by a provider: positive, at most 8 decimal places
export function rateValidator(control: AbstractControl): ValidationErrors | null {
  const value = control.value;
  if (value === null || value === '') return null;
  const n = Number(value);
  if (!Number.isFinite(n) || n <= 0) return { amount: 'Enter a rate above zero.' };
  if (Math.round(n * 1e8) / 1e8 !== n) return { amount: 'Use at most 8 decimal places.' };
  return null;
}
