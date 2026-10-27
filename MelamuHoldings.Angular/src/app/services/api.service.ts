import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  AnalyticsSummary, EquityPoint, StrategyPerformance,
  TimePerformance, BehaviourSummary, UploadResult
} from '../models/analytics.model';
import { Trade, CreateTrade } from '../models/trade.model';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private base = environment.apiUrl;

  constructor(private http: HttpClient) {}

  // Trades
  getTrades(): Observable<Trade[]> {
    return this.http.get<Trade[]>(`${this.base}/trades`);
  }

  createTrade(trade: CreateTrade): Observable<Trade> {
    return this.http.post<Trade>(`${this.base}/trades`, trade);
  }

  updateTrade(id: number, trade: CreateTrade): Observable<void> {
    return this.http.put<void>(`${this.base}/trades/${id}`, trade);
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