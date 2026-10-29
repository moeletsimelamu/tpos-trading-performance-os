import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Account, AccountType, Allocation, AllocationPolicy, AllocationPreview, AuditEvent, Balances, CurrencyInfo, Fund, Overview, Ownership, Paged, Payout, PayoutStatus, RecordKind, Transaction, TransactionFilter } from './finance.models';

export interface CreateAccountRequest {
  name: string; type: AccountType; currency: string; ownership: Ownership; institution?: string; notes?: string;
}

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

  closeAccount(id: number): Observable<Account> {
    return this.http.post<Account>(`${this.base}/financial-accounts/${id}/close`, {});
  }

  getFunds(): Observable<Fund[]> {
    return this.http.get<Fund[]>(`${this.base}/funds`);
  }

  createFund(request: { name: string; description?: string }): Observable<Fund> {
    return this.http.post<Fund>(`${this.base}/funds`, request);
  }

  updateFund(id: number, request: { name: string; description?: string }): Observable<Fund> {
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
