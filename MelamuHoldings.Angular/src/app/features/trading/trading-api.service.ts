import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AnalyticsSummary, EquityPoint, StrategyPerformance,
  TimePerformance, BehaviourSummary, UploadResult
} from './models/analytics.model';
import { SaveTrade, Trade, TradeStatus } from './models/trade.model';
import { JournalSection, JournalTemplate } from './models/journal.model';

@Injectable({ providedIn: 'root' })
export class TradingApiService {
  private base = environment.apiUrl;

  constructor(private http: HttpClient) {}

  // Trades
  getTrades(status?: TradeStatus): Observable<Trade[]> {
    const params = status ? new HttpParams().set('status', status) : undefined;
    return this.http.get<Trade[]>(`${this.base}/trades`, { params });
  }

  getTrade(id: number): Observable<Trade> {
    return this.http.get<Trade>(`${this.base}/trades/${id}`);
  }

  // The key makes a retried save return the trade already created instead of a second copy
  createTrade(trade: SaveTrade, idempotencyKey: string): Observable<Trade> {
    return this.http.post<Trade>(`${this.base}/trades`, trade, { headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }) });
  }

  updateTrade(id: number, trade: SaveTrade): Observable<Trade> {
    return this.http.put<Trade>(`${this.base}/trades/${id}`, trade);
  }

  // ---- Journal templates ----

  getTemplates(includeArchived = false): Observable<JournalTemplate[]> {
    const params = includeArchived ? new HttpParams().set('includeArchived', true) : undefined;
    return this.http.get<JournalTemplate[]>(`${this.base}/journal-templates`, { params });
  }

  getTemplate(id: number): Observable<JournalTemplate> {
    return this.http.get<JournalTemplate>(`${this.base}/journal-templates/${id}`);
  }

  getTemplateVersion(id: number, version: number): Observable<JournalSection[]> {
    return this.http.get<JournalSection[]>(`${this.base}/journal-templates/${id}/versions/${version}`);
  }

  saveTemplate(id: number | null, body: { name: string; description?: string | null; sections: JournalSection[] }): Observable<JournalTemplate> {
    return id === null
      ? this.http.post<JournalTemplate>(`${this.base}/journal-templates`, body)
      : this.http.put<JournalTemplate>(`${this.base}/journal-templates/${id}`, body);
  }

  templateAction(id: number, action: 'duplicate' | 'archive' | 'restore' | 'make-default', body: object = {}): Observable<JournalTemplate> {
    return this.http.post<JournalTemplate>(`${this.base}/journal-templates/${id}/${action}`, body);
  }

  reviewTrade(id: number, reviewNotes: string): Observable<Trade> {
    return this.http.post<Trade>(`${this.base}/trades/${id}/review`, { reviewNotes });
  }

  deleteTrade(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/trades/${id}`);
  }

  // Upload
  uploadCsv(file: File): Observable<UploadResult> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<UploadResult>(`${this.base}/upload`, form);
  }

  getUploadLogs(): Observable<UploadResult[]> {
    return this.http.get<UploadResult[]>(`${this.base}/upload/logs`);
  }

  // Analytics
  getSummary(): Observable<AnalyticsSummary> {
    return this.http.get<AnalyticsSummary>(`${this.base}/analytics/summary`);
  }

  getEquityCurve(): Observable<EquityPoint[]> {
    return this.http.get<EquityPoint[]>(`${this.base}/analytics/equity-curve`);
  }

  getStrategyPerformance(): Observable<StrategyPerformance[]> {
    return this.http.get<StrategyPerformance[]>(`${this.base}/analytics/by-strategy`);
  }

  getPerformanceByDay(): Observable<TimePerformance[]> {
    return this.http.get<TimePerformance[]>(`${this.base}/analytics/by-day`);
  }

  getPerformanceBySession(): Observable<TimePerformance[]> {
    return this.http.get<TimePerformance[]>(`${this.base}/analytics/by-session`);
  }

  getBehaviour(): Observable<BehaviourSummary> {
    return this.http.get<BehaviourSummary>(`${this.base}/analytics/behaviour`);
  }
}