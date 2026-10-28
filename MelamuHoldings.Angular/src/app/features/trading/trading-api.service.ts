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
import { AdherenceReport, SaveStrategy, Strategy } from './models/strategy.model';
import { AnalyticsFilter, AnalyticsFilterOptions, filterParams } from './models/analytics-filter.model';
import { CalendarMonth, ImportMatch, JournalOverview, LinkResult, MatchDecision, TradeAttachment } from './models/journal-view.model';

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

  // ---- Strategies and adherence ----

  getStrategies(includeArchived = false): Observable<Strategy[]> {
    const params = includeArchived ? new HttpParams().set('includeArchived', true) : undefined;
    return this.http.get<Strategy[]>(`${this.base}/strategies`, { params });
  }

  getStrategy(id: number): Observable<Strategy> {
    return this.http.get<Strategy>(`${this.base}/strategies/${id}`);
  }

  saveStrategy(id: number | null, body: SaveStrategy): Observable<Strategy> {
    return id === null
      ? this.http.post<Strategy>(`${this.base}/strategies`, body)
      : this.http.put<Strategy>(`${this.base}/strategies/${id}`, body);
  }

  strategyAction(id: number, action: 'archive' | 'restore'): Observable<Strategy> {
    return this.http.post<Strategy>(`${this.base}/strategies/${id}/${action}`, {});
  }

  restoreRule(id: number, ruleId: number): Observable<Strategy> {
    return this.http.post<Strategy>(`${this.base}/strategies/${id}/rules/${ruleId}/restore`, {});
  }

  // Adherence takes the same filter as every other figure (a strategy page passes its strategyId)
  getAdherence(filter: AnalyticsFilter = {}): Observable<AdherenceReport> {
    return this.http.get<AdherenceReport>(`${this.base}/analytics/adherence`, { params: filterParams(filter) });
  }

  getFilterOptions(): Observable<AnalyticsFilterOptions> {
    return this.http.get<AnalyticsFilterOptions>(`${this.base}/analytics/filter-options`);
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

  // CSV rows that matched trades journaled by hand; by default those still waiting for a decision
  getImportMatches(): Observable<ImportMatch[]> {
    return this.http.get<ImportMatch[]>(`${this.base}/upload/matches`);
  }

  decideMatch(id: number, decision: MatchDecision): Observable<ImportMatch | LinkResult> {
    return this.http.post<ImportMatch | LinkResult>(`${this.base}/upload/matches/${id}/${decision}`, {});
  }

  // ---- Journal views ----

  getJournalOverview(): Observable<JournalOverview> {
    return this.http.get<JournalOverview>(`${this.base}/journal/overview`);
  }

  getCalendar(year: number, month: number): Observable<CalendarMonth> {
    return this.http.get<CalendarMonth>(`${this.base}/journal/calendar`, { params: new HttpParams().set('year', year).set('month', month) });
  }

  // ---- Attachments: files are fetched with the session's token, so they are read as blobs ----

  uploadAttachment(tradeId: number, file: File, caption?: string): Observable<TradeAttachment> {
    const form = new FormData();
    form.append('file', file);
    if (caption?.trim()) form.append('caption', caption.trim());
    return this.http.post<TradeAttachment>(`${this.base}/trades/${tradeId}/attachments`, form);
  }

  getAttachment(tradeId: number, id: number): Observable<Blob> {
    return this.http.get(`${this.base}/trades/${tradeId}/attachments/${id}`, { responseType: 'blob' });
  }

  deleteAttachment(tradeId: number, id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/trades/${tradeId}/attachments/${id}`);
  }

  // Analytics
  getSummary(filter: AnalyticsFilter = {}): Observable<AnalyticsSummary> {
    return this.http.get<AnalyticsSummary>(`${this.base}/analytics/summary`, { params: filterParams(filter) });
  }

  getEquityCurve(filter: AnalyticsFilter = {}): Observable<EquityPoint[]> {
    return this.http.get<EquityPoint[]>(`${this.base}/analytics/equity-curve`, { params: filterParams(filter) });
  }

  getStrategyPerformance(filter: AnalyticsFilter = {}): Observable<StrategyPerformance[]> {
    return this.http.get<StrategyPerformance[]>(`${this.base}/analytics/by-strategy`, { params: filterParams(filter) });
  }

  getPerformanceByDay(filter: AnalyticsFilter = {}): Observable<TimePerformance[]> {
    return this.http.get<TimePerformance[]>(`${this.base}/analytics/by-day`, { params: filterParams(filter) });
  }

  getPerformanceBySession(filter: AnalyticsFilter = {}): Observable<TimePerformance[]> {
    return this.http.get<TimePerformance[]>(`${this.base}/analytics/by-session`, { params: filterParams(filter) });
  }

  getBehaviour(filter: AnalyticsFilter = {}): Observable<BehaviourSummary> {
    return this.http.get<BehaviourSummary>(`${this.base}/analytics/behaviour`, { params: filterParams(filter) });
  }
}