import { Component, input } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MoneyPipe } from '../../../shared/money.pipe';
import { DRAWDOWN_MODELS, DrawdownModel, PropAccountRules, drawdownLabel } from '../models/prop-account.model';

// The form group both the new-account form and the rule and phase forms use
export function rulesGroup(fb: FormBuilder, rules?: PropAccountRules) {
  return fb.nonNullable.group({
    drawdownModel: [rules?.drawdownModel ?? ('Static' as DrawdownModel), Validators.required],
    maxDrawdown: [rules?.maxDrawdown ?? (null as number | null), Validators.min(0.01)],
    trailingLocksAtStart: [rules?.trailingLocksAtStart ?? false],
    dailyLossLimit: [rules?.dailyLossLimit ?? (null as number | null), Validators.min(0.01)],
    profitTarget: [rules?.profitTarget ?? (null as number | null), Validators.min(0.01)],
    minimumTradingDays: [rules?.minimumTradingDays ?? (null as number | null), [Validators.min(1), Validators.max(1000)]],
    payoutSplitPercent: [rules?.payoutSplitPercent ?? (null as number | null), [Validators.min(0.01), Validators.max(100)]],
    purchaseFee: [rules?.purchaseFee ?? (null as number | null), Validators.min(0.01)],
    resetFee: [rules?.resetFee ?? (null as number | null), Validators.min(0.01)],
    monthlyFee: [rules?.monthlyFee ?? (null as number | null), Validators.min(0.01)],
    notes: [rules?.notes ?? '', Validators.maxLength(500)]
  });
}

export type RulesGroup = ReturnType<typeof rulesGroup>;

// What the API takes: a limit left out of a model that has none is not sent, and blank notes are empty
export function rulesFrom(group: RulesGroup): PropAccountRules {
  const v = group.getRawValue();
  const none = v.drawdownModel === 'None';
  return {
    ...v,
    maxDrawdown: none ? null : v.maxDrawdown,
    trailingLocksAtStart: v.drawdownModel.startsWith('Trailing') && v.trailingLocksAtStart,
    notes: v.notes?.trim() || null
  };
}

// The rule inputs, inside whichever form holds the group
@Component({
  selector: 'app-prop-rules-fields',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <fieldset class="rules" [formGroup]="asGroup()">
      <legend>Rules (in {{ currency() }}; leave empty what the firm does not set)</legend>
      <div class="form-grid">
        <div class="field">
          <label [for]="id('model')">Drawdown model</label>
          <select [id]="id('model')" formControlName="drawdownModel">
            @for (m of models; track m.value) { <option [value]="m.value">{{ m.label }}</option> }
          </select>
        </div>
        @if (group().controls.drawdownModel.value !== 'None') {
          <div class="field"><label [for]="id('max')">Drawdown limit</label><input [id]="id('max')" type="number" min="0.01" step="0.01" formControlName="maxDrawdown" /></div>
        }
        @if (group().controls.drawdownModel.value.startsWith('Trailing')) {
          <label class="check"><input type="checkbox" formControlName="trailingLocksAtStart" />Stops trailing once it reaches the starting balance</label>
        }
        <div class="field"><label [for]="id('daily')">Daily loss limit</label><input [id]="id('daily')" type="number" min="0.01" step="0.01" formControlName="dailyLossLimit" /></div>
        <div class="field"><label [for]="id('target')">Profit target</label><input [id]="id('target')" type="number" min="0.01" step="0.01" formControlName="profitTarget" /></div>
        <div class="field"><label [for]="id('days')">Minimum trading days</label><input [id]="id('days')" type="number" min="1" max="1000" step="1" formControlName="minimumTradingDays" /></div>
        <div class="field"><label [for]="id('split')">Your payout split (%)</label><input [id]="id('split')" type="number" min="0.01" max="100" step="0.01" formControlName="payoutSplitPercent" /></div>
        <div class="field"><label [for]="id('purchase')">Purchase fee</label><input [id]="id('purchase')" type="number" min="0.01" step="0.01" formControlName="purchaseFee" /></div>
        <div class="field"><label [for]="id('reset')">Reset fee</label><input [id]="id('reset')" type="number" min="0.01" step="0.01" formControlName="resetFee" /></div>
        <div class="field"><label [for]="id('monthly')">Monthly fee</label><input [id]="id('monthly')" type="number" min="0.01" step="0.01" formControlName="monthlyFee" /></div>
        <div class="field wide"><label [for]="id('notes')">Rule notes (optional)</label><input [id]="id('notes')" formControlName="notes" maxlength="500" placeholder="e.g. no news trading, consistency rule" /></div>
      </div>
      <p class="small secondary">Fees here are the firm's stated prices, for reference. They are not recorded as paid.</p>
    </fieldset>
  `,
  styles: [`
    .rules { border: 0; padding: 0; margin: var(--space-4) 0 0; }
    legend { font-size: var(--text-sm); font-weight: 500; margin-bottom: var(--space-2); }
    .wide { grid-column: 1 / -1; }
    .check { display: flex; gap: var(--space-2); align-items: center; font-size: var(--text-sm); }
    .check input { height: auto; }
  `]
})
export class PropRulesFieldsComponent {
  readonly group = input.required<RulesGroup>();
  readonly currency = input.required<string>();
  readonly prefix = input('rules');
  readonly models = DRAWDOWN_MODELS;

  asGroup(): FormGroup { return this.group() as unknown as FormGroup; }
  id(name: string): string { return `${this.prefix()}-${name}`; }
}

// One version of the rules, as read by a person
@Component({
  selector: 'app-prop-rules-summary',
  standalone: true,
  imports: [MoneyPipe],
  template: `
    <dl class="rule-list">
      <div><dt>Drawdown</dt><dd>
        {{ label(rules().drawdownModel) }}
        @if (rules().maxDrawdown !== null) { · {{ rules().maxDrawdown! | money: currency() }} }
        @if (rules().trailingLocksAtStart) { · stops trailing at the starting balance }
      </dd></div>
      <div><dt>Daily loss limit</dt><dd>@if (rules().dailyLossLimit !== null) { {{ rules().dailyLossLimit! | money: currency() }} } @else { Not set }</dd></div>
      <div><dt>Profit target</dt><dd>@if (rules().profitTarget !== null) { {{ rules().profitTarget! | money: currency() }} } @else { Not set }</dd></div>
      <div><dt>Minimum trading days</dt><dd>{{ rules().minimumTradingDays ?? 'Not set' }}</dd></div>
      <div><dt>Your payout split</dt><dd>@if (rules().payoutSplitPercent !== null) { {{ rules().payoutSplitPercent }}% } @else { Not set }</dd></div>
      <div><dt>Stated fees</dt><dd>
        @if (rules().purchaseFee === null && rules().resetFee === null && rules().monthlyFee === null) { Not set }
        @if (rules().purchaseFee !== null) { <span class="fee">Purchase {{ rules().purchaseFee! | money: currency() }}</span> }
        @if (rules().resetFee !== null) { <span class="fee">Reset {{ rules().resetFee! | money: currency() }}</span> }
        @if (rules().monthlyFee !== null) { <span class="fee">Monthly {{ rules().monthlyFee! | money: currency() }}</span> }
      </dd></div>
      @if (rules().notes) { <div class="wide"><dt>Notes</dt><dd>{{ rules().notes }}</dd></div> }
    </dl>
  `,
  styles: [`
    .rule-list { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: var(--space-3) var(--space-5); }
    .wide { grid-column: 1 / -1; }
    dt { font-size: var(--text-xs); color: var(--text-secondary); }
    dd { margin: 0; font-size: var(--text-sm); }
    .fee { display: block; }
  `]
})
export class PropRulesSummaryComponent {
  readonly rules = input.required<PropAccountRules>();
  readonly currency = input.required<string>();

  label(model: DrawdownModel): string { return drawdownLabel(model); }
}
