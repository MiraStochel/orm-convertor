import { ChangeDetectionStrategy, Component, Input } from "@angular/core";
import { CommonModule } from "@angular/common";
import { ORMType } from "../../model/orm-type";
import { AdvisorMeasurements, BenchmarkMeasurementDto } from "../../model/advisor";

export interface QueryAssignmentView {
  queryId: string;
  framework: ORMType;
  label?: string;
}

@Component({
  selector: "app-result-table",
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule],
  templateUrl: "./result-table.component.html",
  styleUrls: ["./result-table.component.less"],
  standalone: true,
})
export class ResultTableComponent {
  @Input() assignments: QueryAssignmentView[] = [];
  @Input() selectedFrameworks: ORMType[] = [];
  @Input() measurements: AdvisorMeasurements | null = null;

  private readonly frameworkOptions: { key: string; value: ORMType }[] =
    Object.keys(ORMType)
      .filter((k) => isNaN(Number(k)))
      .map((k) => ({ key: k, value: (ORMType as any)[k] as ORMType }));

  get uniqueFrameworks(): ORMType[] {
    // Show columns based on measurements (preferred) or selected frameworks
    const fromMeasurements = this.measurements
      ? this.extractFrameworksFromMeasurements(this.measurements)
      : [];
    const chosen = Array.from(new Set(this.selectedFrameworks ?? []));
    return fromMeasurements.length > 0 ? fromMeasurements : chosen;
  }

  frameworkName(framework: ORMType): string {
    return (
      this.frameworkOptions.find((opt) => opt.value === framework)?.key ??
      framework.toString()
    );
  }

  // ---------- Precomputed view models ----------
  // Query order Q1..Qn
  get sortedQueryIds(): string[] {
    if (!this.measurements) return [];
    return Object.keys(this.measurements).sort((a, b) => Number(a) - Number(b));
  }

  // Rows for Memory table
  get memoryTableRows(): { queryId: string; values: (number | null)[]; min: number | null; max: number | null }[] {
    return this.sortedQueryIds.map((qid) => {
      const values = this.uniqueFrameworks.map((f) => this.memoryKb(qid, f));
      return { queryId: qid, values, min: this.rowMin(values), max: this.rowMax(values) };
    });
  }

  // Rows for Runtime table
  get runtimeTableRows(): { queryId: string; values: (number | null)[]; min: number | null; max: number | null }[] {
    return this.sortedQueryIds.map((qid) => {
      const values = this.uniqueFrameworks.map((f) => this.runtimeMs(qid, f));
      return { queryId: qid, values, min: this.rowMin(values), max: this.rowMax(values) };
    });
  }

  // Totals
  get memoryTotalsVM(): { totals: Record<ORMType, number>; min: number | null; max: number | null } {
    const totals = this.totalsMemory();
    const orderedTotals = this.uniqueFrameworks.map((f) => totals[f]);
    return { totals, min: this.rowMin(orderedTotals), max: this.rowMax(orderedTotals) };
  }

  get runtimeTotalsVM(): { totals: Record<ORMType, number>; min: number | null; max: number | null } {
    const totals = this.totalsRuntime();
    const orderedTotals = this.uniqueFrameworks.map((f) => totals[f]);
    return { totals, min: this.rowMin(orderedTotals), max: this.rowMax(orderedTotals) };
  }

  memoryKb(queryId: string, framework: ORMType): number | null {
    const m = this.getMeasurement(queryId, framework);
    return m ? Math.round(m.allocatedBytes / 1024) : null;
  }

  runtimeMs(queryId: string, framework: ORMType): number | null {
    const m = this.getMeasurement(queryId, framework);
    return m ? Math.round(m.meanDurationMilliseconds) : null;
  }

  private getMeasurement(queryId: string, framework: ORMType): BenchmarkMeasurementDto | null {
    const perFramework = this.measurements?.[queryId] as
      | Record<string, BenchmarkMeasurementDto>
      | undefined;
    if (!perFramework) return null;
    // Keys may be numeric strings or enum names (e.g., "Dapper", "EFCore").
    const direct = (perFramework as any)[framework];
    if (direct) return direct as BenchmarkMeasurementDto;
    const byString = (perFramework as any)[String(framework)];
    if (byString) return byString as BenchmarkMeasurementDto;
    const enumName = this.frameworkOptions.find((o) => o.value === framework)?.key;
    if (enumName) {
      const byName = (perFramework as any)[enumName] ?? (perFramework as any)[enumName.toUpperCase()] ?? (perFramework as any)[enumName.toLowerCase()];
      if (byName) return byName as BenchmarkMeasurementDto;
    }
    return null;
  }

  private extractFrameworksFromMeasurements(meas: AdvisorMeasurements): ORMType[] {
    const first = Object.values(meas)[0] as Record<string, unknown> | undefined;
    if (!first) return [];
    const keys = Object.keys(first);
    const byName = new Map(this.frameworkOptions.map(o => [o.key.toLowerCase(), o.value] as const));
    const result: ORMType[] = [];
    for (const k of keys) {
      const asNum = Number(k);
      if (!Number.isNaN(asNum)) { result.push(asNum as ORMType); continue; }
      const nameVal = byName.get(k.toLowerCase());
      if (nameVal !== undefined) result.push(nameVal);
    }
    return Array.from(new Set(result));
  }

  rowMin(values: (number | null)[]): number | null {
    const nums = values.filter((v): v is number => v !== null);
    return nums.length > 0 ? Math.min(...nums) : null;
  }

  rowMax(values: (number | null)[]): number | null {
    const nums = values.filter((v): v is number => v !== null);
    return nums.length > 0 ? Math.max(...nums) : null;
  }

  totalsMemory(): Record<ORMType, number> {
    const totals: Record<number, number> = {};
    for (const f of this.uniqueFrameworks) totals[f] = 0;
    for (const q of this.sortedQueryIds) {
      for (const f of this.uniqueFrameworks) {
        const v = this.memoryKb(q, f);
        if (v !== null) totals[f] += v;
      }
    }
    return totals as Record<ORMType, number>;
  }

  totalsRuntime(): Record<ORMType, number> {
    const totals: Record<number, number> = {};
    for (const f of this.uniqueFrameworks) totals[f] = 0;
    for (const q of this.sortedQueryIds) {
      for (const f of this.uniqueFrameworks) {
        const v = this.runtimeMs(q, f);
        if (v !== null) totals[f] += v;
      }
    }
    return totals as Record<ORMType, number>;
  }
}
