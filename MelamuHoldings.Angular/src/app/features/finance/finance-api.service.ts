import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Account, AccountCategory, AccountType, CardFinish, PaymentCardKind, PaymentCardNetwork, TaxTreatment, Allocation, AllocationPolicy, AllocationPreview, AuditEvent, Balances, BudgetLine, BudgetMonth, ContinuityReport, UnrestrictedCashReport, ContinuitySettings, CurrencyInfo, Fund, Investments, Obligation, Overview, Ownership, Paged, PaymentCandidate, Payout, PlannedPurchase, PayoutStatus, RecordKind, RecordValuation, SaveContinuitySettings, SaveFund, SaveObligation, SavePlannedPurchase, Transaction, TransactionFilter, Valuation } from './finance.models';

export interface CreateAccountRequest {
  name: string; type: AccountType; currency: string; ownership: Ownership; institution?: string | null; notes?: string | null;
  category: AccountCategory; taxTreatment: TaxTreatment; reference: string | null; openedOn: string | null; homeFundId: number | null;
}

export type UpdateAccountRequest = Omit<CreateAccountRequest, 'type' | 'currency'>;

@Injectable({ providedIn: 'root' })
export class FinanceApiService {
  private http = inject(HttpClient);
  private base = environment.apiUrl;

  getCurrencies(): Observable<CurrencyInfo[]> {
    return this.http.get<CurrencyInfo[]>(`${this.base}/currencies`);
  }

  getBalances(asOf?: string): Observable<Balances> {
    const params = asOf ? new HttpParams().set('asOf', asOf) : undefined;
    return this.http.get<Balances>(`${this.base}/balances`, { params });
  }

  getAccounts(): Observable<Account[]> {
    return this.http.get<Account[]>(`${this.base}/financial-accounts`);
  }

  createAccount(request: CreateAccountRequest): Observable<Account> {
    return this.http.post<Account>(`${this.base}/financial-accounts`, request);
  }

  updateAccount(id: number, request: UpdateAccountRequest): Observable<Account> {
    return this.http.put<Account>(`${this.base}/financial-accounts/${id}`, request);
  }

  addPaymentCard(accountId: number, request: { kind: PaymentCardKind; name: string | null; lastFour: string | null; network: PaymentCardNetwork; finish: CardFinish }): Observable<Account> {
    return this.http.post<Account>(`${this.base}/financial-accounts/${accountId}/cards`, request);
  }

  cancelPaymentCard(accountId: number, cardId: number): Observable<Account> {
    return this.http.post<Account>(`${this.base}/financial-accounts/${accountId}/cards/${cardId}/cancel`, {});
  }

  closeAccount(id: number): Observable<Account> {
    return this.http.post<Account>(`${this.base}/financial-accounts/${id}/close`, {});
  }

  getFunds(): Observable<Fund[]> {
    return this.http.get<Fund[]>(`${this.base}/funds`);
  }

  createFund(request: SaveFund): Observable<Fund> {
    return this.http.post<Fund>(`${this.base}/funds`, request);
  }

  updateFund(id: number, request: SaveFund): Observable<Fund> {
    return this.http.put<Fund>(`${this.base}/funds/${id}`, request);
  }

  getTransactions(filter: TransactionFilter): Observable<Paged<Transaction>> {
    let params = new HttpParams().set('page', filter.page).set('pageSize', filter.pageSize);
    if (filter.from) params = params.set('from', filter.from);
    if (filter.to) params = params.set('to', filter.to);
    if (filter.type) params = params.set('type', filter.type);
    if (filter.accountId) params = params.set('accountId', filter.accountId);
    if (filter.fundId) params = params.set('fundId', filter.fundId);
    if (filter.cash) params = params.set('cash', true);
    return this.http.get<Paged<Transaction>>(`${this.base}/transactions`, { params });
  }

  // This month to date unless a period is given
  getOverview(from?: string, to?: string): Observable<Overview> {
    let params = new HttpParams();
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    return this.http.get<Overview>(`${this.base}/overview`, { params });
  }

  // The idempotency key makes a retry after a lost response safe: the server returns the original instead of posting twice
  record(kind: RecordKind, body: object, idempotencyKey: string): Observable<Transaction> {
    return this.http.post<Transaction>(`${this.base}/transactions/${kind}`, body, { headers: this.keyHeader(idempotencyKey) });
  }

  reverse(id: number, body: { date: string; reason: string }, idempotencyKey: string): Observable<Transaction> {
    return this.http.post<Transaction>(`${this.base}/transactions/${id}/reverse`, body, { headers: this.keyHeader(idempotencyKey) });
  }

  getAuditEvents(page: number, pageSize: number): Observable<Paged<AuditEvent>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<Paged<AuditEvent>>(`${this.base}/audit-events`, { params });
  }

  // ---- Payouts ----

  getPayouts(status?: PayoutStatus): Observable<Payout[]> {
    const params = status ? new HttpParams().set('status', status) : undefined;
    return this.http.get<Payout[]>(`${this.base}/payouts`, { params });
  }

  getPayout(id: number): Observable<Payout> {
    return this.http.get<Payout>(`${this.base}/payouts/${id}`);
  }

  createPayout(body: object, idempotencyKey: string): Observable<Payout> {
    return this.http.post<Payout>(`${this.base}/payouts`, body, { headers: this.keyHeader(idempotencyKey) });
  }

  approvePayout(id: number, body: { amount?: number | null; date: string }): Observable<Payout> {
    return this.http.post<Payout>(`${this.base}/payouts/${id}/approve`, body);
  }

  rejectPayout(id: number, body: { date: string; reason: string }): Observable<Payout> {
    return this.http.post<Payout>(`${this.base}/payouts/${id}/reject`, body);
  }

  receivePayout(id: number, body: object, idempotencyKey: string): Observable<Payout> {
    return this.http.post<Payout>(`${this.base}/payouts/${id}/receive`, body, { headers: this.keyHeader(idempotencyKey) });
  }

  undoReceipt(id: number, body: { date: string; reason: string }, idempotencyKey: string): Observable<Payout> {
    return this.http.post<Payout>(`${this.base}/payouts/${id}/undo-receipt`, body, { headers: this.keyHeader(idempotencyKey) });
  }

  // ---- Allocation ----

  getPolicies(): Observable<AllocationPolicy[]> {
    return this.http.get<AllocationPolicy[]>(`${this.base}/allocation-policies`);
  }

  createPolicy(body: { lines: { fundId: number; percentage: number }[]; activate: boolean }): Observable<AllocationPolicy> {
    return this.http.post<AllocationPolicy>(`${this.base}/allocation-policies`, body);
  }

  activatePolicy(id: number): Observable<AllocationPolicy> {
    return this.http.post<AllocationPolicy>(`${this.base}/allocation-policies/${id}/activate`, {});
  }

  previewAllocation(payoutId: number, policyId?: number): Observable<AllocationPreview> {
    return this.http.post<AllocationPreview>(`${this.base}/allocations/preview`, { payoutId, policyId });
  }

  confirmAllocation(payoutId: number, policyId: number, idempotencyKey: string): Observable<Allocation> {
    return this.http.post<Allocation>(`${this.base}/allocations`, { payoutId, policyId }, { headers: this.keyHeader(idempotencyKey) });
  }

  getAllocations(): Observable<Allocation[]> {
    return this.http.get<Allocation[]>(`${this.base}/allocations`);
  }

  getAllocation(id: number): Observable<Allocation> {
    return this.http.get<Allocation>(`${this.base}/allocations/${id}`);
  }

  settleLine(allocationId: number, lineId: number, body: object, idempotencyKey: string): Observable<Allocation> {
    return this.http.post<Allocation>(`${this.base}/allocations/${allocationId}/lines/${lineId}/settle`, body, { headers: this.keyHeader(idempotencyKey) });
  }

  cancelAllocation(id: number, reason: string): Observable<Allocation> {
    return this.http.post<Allocation>(`${this.base}/allocations/${id}/cancel`, { reason });
  }

  // Obligations: what the business has to pay. Payments link expenses already in the ledger; nothing here moves money.
  getObligations(includeClosed = false): Observable<Obligation[]> {
    const params = includeClosed ? new HttpParams().set('includeClosed', true) : undefined;
    return this.http.get<Obligation[]>(`${this.base}/obligations`, { params });
  }

  saveObligation(id: number | null, request: SaveObligation): Observable<Obligation> {
    return id ? this.http.put<Obligation>(`${this.base}/obligations/${id}`, request) : this.http.post<Obligation>(`${this.base}/obligations`, request);
  }

  cancelObligation(id: number): Observable<Obligation> {
    return this.http.post<Obligation>(`${this.base}/obligations/${id}/cancel`, {});
  }

  getPaymentCandidates(id: number): Observable<PaymentCandidate[]> {
    return this.http.get<PaymentCandidate[]>(`${this.base}/obligations/${id}/payment-candidates`);
  }

  recordObligationPayment(id: number, transactionId: number): Observable<Obligation> {
    return this.http.post<Obligation>(`${this.base}/obligations/${id}/payments`, { transactionId });
  }

  undoObligationPayment(id: number): Observable<Obligation> {
    return this.http.post<Obligation>(`${this.base}/obligations/${id}/payments/undo`, {});
  }

  // Continuity: coverage measured from recorded balances, and the settings it is measured by
  getUnrestrictedCash(): Observable<UnrestrictedCashReport> {
    return this.http.get<UnrestrictedCashReport>(`${this.base}/unrestricted-cash`);
  }

  getContinuity(): Observable<ContinuityReport> {
    return this.http.get<ContinuityReport>(`${this.base}/continuity`);
  }

  saveContinuitySettings(request: SaveContinuitySettings): Observable<ContinuitySettings> {
    return this.http.put<ContinuitySettings>(`${this.base}/continuity/settings`, request);
  }

  // Investments: brokerage valuations; recording one never moves money
  getInvestments(): Observable<Investments> {
    return this.http.get<Investments>(`${this.base}/investments`);
  }

  getValuations(accountId: number): Observable<Valuation[]> {
    return this.http.get<Valuation[]>(`${this.base}/investments/${accountId}/valuations`);
  }

  recordValuation(accountId: number, request: RecordValuation): Observable<Valuation> {
    return this.http.post<Valuation>(`${this.base}/investments/${accountId}/valuations`, request);
  }

  deleteValuation(accountId: number, valuationId: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/investments/${accountId}/valuations/${valuationId}`);
  }

  // Budgets: plans per month and category; never move money
  getBudgets(month?: string): Observable<BudgetMonth> {
    const params = month ? new HttpParams().set('month', month) : undefined;
    return this.http.get<BudgetMonth>(`${this.base}/budgets`, { params });
  }

  createBudgetLine(request: { month: string; category: string; amount: number; currency: string }): Observable<BudgetLine> {
    return this.http.post<BudgetLine>(`${this.base}/budgets`, request);
  }

  updateBudgetLine(id: number, request: { category: string; amount: number }): Observable<BudgetLine> {
    return this.http.put<BudgetLine>(`${this.base}/budgets/${id}`, request);
  }

  deleteBudgetLine(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/budgets/${id}`);
  }

  // Planned purchases: a purchase links an expense already in the ledger; nothing here moves money
  getPlannedPurchases(includeClosed = false): Observable<PlannedPurchase[]> {
    const params = includeClosed ? new HttpParams().set('includeClosed', true) : undefined;
    return this.http.get<PlannedPurchase[]>(`${this.base}/planned-purchases`, { params });
  }

  savePlannedPurchase(id: number | null, request: SavePlannedPurchase): Observable<PlannedPurchase> {
    return id ? this.http.put<PlannedPurchase>(`${this.base}/planned-purchases/${id}`, request) : this.http.post<PlannedPurchase>(`${this.base}/planned-purchases`, request);
  }

  commitPurchase(id: number): Observable<PlannedPurchase> {
    return this.http.post<PlannedPurchase>(`${this.base}/planned-purchases/${id}/commit`, {});
  }

  withdrawPurchaseCommitment(id: number): Observable<PlannedPurchase> {
    return this.http.post<PlannedPurchase>(`${this.base}/planned-purchases/${id}/withdraw-commitment`, {});
  }

  cancelPurchase(id: number): Observable<PlannedPurchase> {
    return this.http.post<PlannedPurchase>(`${this.base}/planned-purchases/${id}/cancel`, {});
  }

  getPurchaseCandidates(id: number): Observable<PaymentCandidate[]> {
    return this.http.get<PaymentCandidate[]>(`${this.base}/planned-purchases/${id}/expense-candidates`);
  }

  recordPurchase(id: number, transactionId: number): Observable<PlannedPurchase> {
    return this.http.post<PlannedPurchase>(`${this.base}/planned-purchases/${id}/purchase`, { transactionId });
  }

  undoPurchase(id: number): Observable<PlannedPurchase> {
    return this.http.post<PlannedPurchase>(`${this.base}/planned-purchases/${id}/purchase/undo`, {});
  }

  private keyHeader(key: string): HttpHeaders {
    return new HttpHeaders({ 'Idempotency-Key': key });
  }
}

export function newIdempotencyKey(): string {
  return crypto.randomUUID().replaceAll('-', '');
}

export function todayIso(): string {
  const now = new Date();
  return new Date(now.getTime() - now.getTimezoneOffset() * 60000).toISOString().slice(0, 10);
}
