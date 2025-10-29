import { Injectable } from "@angular/core";
import { HttpClient } from "@angular/common/http";
import { Observable } from "rxjs";
import { ConvertRequest, ConvertResponse } from "../model/convert";
import { RequiredContentDefinition } from "../model/required-content";
import { AdvisorRunRequest, AdvisorRunResult } from "../model/advisor";

@Injectable({ providedIn: "root" })
export class OrmService {
  constructor(private http: HttpClient) { }

  private base = "/orm";

  getRequiredContent(): Observable<RequiredContentDefinition[]> {
    return this.http.get<RequiredContentDefinition[]>(`${this.base}/required-content`);
  }

  getRequiredContentAdvisor(): Observable<RequiredContentDefinition[]> {
    return this.http.get<RequiredContentDefinition[]>(`${this.base}/required-content-advisor`);
  }

  getSamples(): Observable<Record<number, string>> {
    return this.http.get<Record<number, string>>(`${this.base}/samples`);
  }

  convert(req: ConvertRequest): Observable<ConvertResponse> {
    return this.http.post<ConvertResponse>(`${this.base}/convert`, req);
  }

  runAdvisor(req: AdvisorRunRequest): Observable<AdvisorRunResult> {
    return this.http.post<AdvisorRunResult>(`${this.base}/advisor/run`, req);
  }
}
