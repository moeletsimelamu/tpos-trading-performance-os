import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  Account, AuditEvent, Balances, CurrencyInfo, Fund, Ownership, AccountType,
  Paged, RecordKind, Transaction, TransactionFilter
} from './finance.models';

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
    return this.http.get<Paged<Transaction>>(`${this.base}/transactions`, { params });
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
