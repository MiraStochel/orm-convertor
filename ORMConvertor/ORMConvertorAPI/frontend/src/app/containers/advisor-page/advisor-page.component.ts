import { CommonModule, Location } from "@angular/common";
import {
  AfterViewInit,
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  OnInit,
  inject,
} from "@angular/core";
import { takeUntilDestroyed } from "@angular/core/rxjs-interop";
import { FormsModule } from "@angular/forms";
import { finalize, forkJoin, map, of, switchMap } from "rxjs";
import {
  QueryAssignmentView,
  ResultTableComponent,
} from "../../components/result-table/result-table.component";
import { ContentDisplayComponent } from "../../components/content-display/content-display.component";
import { ContentType } from "../../model/content-type";
import { SourceUnit } from "../../model/convert";
import { AdvisorRunResult } from "../../model/advisor";
import { ORMType } from "../../model/orm-type";
import {
  RequiredContentDefinition,
  RequiredContentUnit,
} from "../../model/required-content";
import { ContentTypeToStringPipe } from "../../pipes/content-type-to-string.pipe";
import { OrmService } from "../../services/orm.service";

interface FrameworkConversion {
  framework: ORMType;
  sources: SourceUnit[];
}

interface UnitView {
  key: string;
  description: string;
  contentType: ContentType;
  sampleId?: number;
}

interface QueryOrderEntry {
  id: string;
  label: string;
}

@Component({
  selector: "app-advisor-page",
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ContentDisplayComponent,
    ContentTypeToStringPipe,
    ResultTableComponent,
  ],
  templateUrl: "./advisor-page.component.html",
  styleUrls: ["./advisor-page.component.less"],
})
export class AdvisorPageComponent implements OnInit, AfterViewInit {
  private destroyRef = inject(DestroyRef);

  ormTypeEnum = ORMType;
  contentTypeEnum = ContentType;

  showResults = false;
  isLoading = false;
  loadingDots = "";
  private loadingInterval?: ReturnType<typeof setInterval>;

  /**
   * Filtered list of ORM options (only enum names, excluding numeric reverse mappings).
   */
  readonly ormTypeOptions: { key: string; value: ORMType }[] = Object.keys(
    ORMType
  )
    .filter((k) => isNaN(Number(k)))
    .map((k) => ({ key: k, value: (ORMType as any)[k] as ORMType }));

  sourceOrm: ORMType = ORMType.EFCore;
  memoryLimitKb = 128_000;
  maxFrameworksToSelect = 2;
  targetOrms: ORMType[] = [];

  error = "";

  requiredContent: RequiredContentDefinition[] = [];
  entityUnits: UnitView[] = [];
  queryUnits: UnitView[] = [];
  contentByUnit: Record<string, string> = {};
  queryWeights: Record<string, number> = {};

  private queryIdCounter = 0;
  private queryOrder: QueryOrderEntry[] = [];

  samples: Map<number, string> = new Map();

  advisorResult: AdvisorRunResult | null = null;
  assignmentEntries: QueryAssignmentView[] = [];
  convertedByFramework: FrameworkConversion[] = [];

  constructor(
    private ormService: OrmService,
    private elRef: ElementRef,
    private location: Location
  ) {}

  ngOnInit(): void {
    this.ormService
      .getRequiredContentAdvisor()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((required) => {
        this.requiredContent = required;
        this.updateRequiredUnits();
      });

    this.ormService
      .getAdvisorSamples()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((samples) => {
        this.samples = new Map(
          Object.entries(samples).map(([k, v]) => [Number(k), v as string])
        );
      });
  }

  onSourceOrmChange(newOrm: string): void {
    this.sourceOrm = +newOrm as ORMType;
    this.updateRequiredUnits();
  }

  /**
   * Toggle selection of a target ORM framework.
   * @param ormValue - The ORM type value
   * @param checked - Whether the checkbox is checked
   */
  onTargetOrmToggle(ormValue: ORMType, checked: boolean): void {
    if (checked) {
      if (!this.targetOrms.includes(ormValue)) {
        this.targetOrms = [...this.targetOrms, ormValue];
      }
    } else {
      this.targetOrms = this.targetOrms.filter((o) => o !== ormValue);
    }
  }

  handleTargetFrameworkChange(ormValue: ORMType, event: Event): void {
    const checkbox = event.target as HTMLInputElement | null;
    if (!checkbox) {
      return;
    }

    this.onTargetOrmToggle(ormValue, checkbox.checked);
  }

  selectAllTargets(): void {
    this.targetOrms = this.ormTypeOptions.map((o) => o.value);
  }

  clearTargets(): void {
    this.targetOrms = [];
  }

  addQuery(): void {
    this.queryUnits = [...this.queryUnits, this.createQueryUnit()];
    this.queryOrder = this.buildQueryOrder();
    setTimeout(() => this.resizeAll(), 0);
  }

  removeQuery(key: string): void {
    if (this.queryUnits.length <= 1) {
      return;
    }

    this.queryUnits = this.queryUnits.filter((q) => q.key !== key);
    delete this.contentByUnit[key];
    delete this.queryWeights[key];
    this.queryOrder = this.buildQueryOrder();
    setTimeout(() => this.resizeAll(), 0);
  }

  private updateRequiredUnits(): void {
    const definition = this.requiredContent.find(
      (r) => r.ormType === this.sourceOrm
    );
    const requiredUnits = definition?.required ?? [];

    this.contentByUnit = {};
    this.queryWeights = {};
    this.queryIdCounter = 0;
    this.queryOrder = [];

    this.entityUnits = requiredUnits
      .filter((u) => u.contentType !== ContentType.CSharpQuery)
      .map((u) => this.createEntityUnit(u));

    const queryTemplates = requiredUnits.filter(
      (u) => u.contentType === ContentType.CSharpQuery
    );

    this.queryUnits =
      queryTemplates.length > 0
        ? queryTemplates.map((template) => this.createQueryUnit(template))
        : [this.createQueryUnit()];

    this.queryOrder = this.buildQueryOrder();
    setTimeout(() => this.resizeAll(), 0);
  }

  private ensureContentSlot(key: string): void {
    if (!(key in this.contentByUnit)) {
      this.contentByUnit[key] = "";
    }
  }

  private createEntityUnit(unit: RequiredContentUnit): UnitView {
    const key = `entity-${unit.id}`;
    this.ensureContentSlot(key);
    return {
      key,
      description: unit.description,
      contentType: unit.contentType,
      sampleId: unit.id,
    };
  }

  private createQueryUnit(template?: RequiredContentUnit): UnitView {
    const key = `query-${++this.queryIdCounter}`;
    const description = template?.description ?? "Query Method";
    const contentType =
      template?.contentType ?? ContentType.CSharpQuery;
    this.ensureContentSlot(key);
    if (!(key in this.queryWeights)) {
      this.queryWeights[key] = 1;
    }
    return {
      key,
      description,
      contentType,
      sampleId: template?.id,
    };
  }

  convert(): void {
    this.queryOrder = this.buildQueryOrder();

    const entities: SourceUnit[] = this.entityUnits.map((unit) => ({
      contentType: unit.contentType,
      content: this.contentByUnit[unit.key] ?? "",
    }));

    const queries = this.queryUnits.map((unit, index) => {
      const queryId = (index + 1).toString();
      const querySource: SourceUnit = {
        contentType: unit.contentType,
        content: this.contentByUnit[unit.key] ?? "",
      };
      const weight = Math.max(
        1,
        Math.trunc(this.queryWeights[unit.key] ?? 1)
      );
      return {
        id: queryId,
        query: querySource,
        weight,
      };
    });

    const combinedSources: SourceUnit[] = [
      ...entities.map((e) => ({ ...e })),
      ...queries.map((q) => ({ ...q.query })),
    ];

    const maxFrameworks = Math.max(1, Math.trunc(this.maxFrameworksToSelect));
    const memoryBytes =
      Math.max(0, Math.trunc(this.memoryLimitKb ?? 0)) * 1024;

    this.isLoading = true;
    this.startLoadingAnimation();

    this.error = "";
    this.showResults = false;
    this.advisorResult = null;
    this.assignmentEntries = [];
    this.convertedByFramework = [];

    this.queryOrder = queries.map((query, index) => ({
      id: query.id,
      label: this.queryUnits[index]?.description ?? `Query ${index + 1}`,
    }));

    const request = {
      sourceOrm: this.sourceOrm,
      entities,
      queries,
      maxMemoryBytes: memoryBytes,
      maxFrameworksToSelect: maxFrameworks,
      targetFrameworks:
        this.targetOrms.length > 0
          ? Array.from(new Set(this.targetOrms))
          : undefined,
    };

    this.ormService
      .runAdvisor(request)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        switchMap((result) => {
          this.advisorResult = result;
          const labelByQuery = new Map(
            this.queryOrder.map((entry) => [entry.id, entry.label])
          );
          this.assignmentEntries = Object.entries(
            result.queryAssignments ?? {}
          )
            .map(([queryId, framework]) => ({
              queryId,
              framework,
              label: labelByQuery.get(queryId) ?? `Query ${queryId}`,
            }))
            .sort((a, b) => Number(a.queryId) - Number(b.queryId));

          const uniqueFrameworks = Array.from(
            new Set(result.selectedFrameworks ?? [])
          );
          if (uniqueFrameworks.length === 0) {
            return of<FrameworkConversion[]>([]);
          }

          return forkJoin(
            uniqueFrameworks.map((framework) =>
              this.ormService
                .convert({
                  sourceOrm: this.sourceOrm,
                  targetOrm: framework,
                  sources: combinedSources,
                })
                .pipe(
                  map((response) => ({
                    framework,
                    sources: response.sources,
                  }))
                )
            )
          );
        }),
        finalize(() => {
          this.isLoading = false;
          this.stopLoadingAnimation();
        })
      )
      .subscribe({
        next: (conversions) => {
          this.convertedByFramework = conversions;
          this.showResults = true;
          setTimeout(() => this.resizeAll(), 0);
        },
        error: (err) => {
          this.error = err?.message ?? "Advisor run failed.";
          this.advisorResult = null;
          this.assignmentEntries = [];
          this.convertedByFramework = [];
        },
      });
  }

  fillWithSamples(): void {
    const updatedContent = { ...this.contentByUnit };
    const updatedWeights = { ...this.queryWeights };

    [...this.entityUnits, ...this.queryUnits].forEach((unit) => {
      if (unit.sampleId === undefined) {
        return;
      }
      const sample = this.samples.get(unit.sampleId);
      if (sample !== undefined) {
        updatedContent[unit.key] = sample;
      }
      if (
        unit.contentType === ContentType.CSharpQuery &&
        !(unit.key in updatedWeights)
      ) {
        updatedWeights[unit.key] = 1;
      }
    });

    this.contentByUnit = updatedContent;
    this.queryWeights = updatedWeights;
    setTimeout(() => this.resizeAll(), 0);
  }

  @HostListener("input", ["$event"])
  onInput(event: Event): void {
    const target = event.target as HTMLTextAreaElement;
    if (
      target &&
      target.tagName.toLowerCase() === "textarea" &&
      target.classList.contains("code-area")
    ) {
      this.resizeTextArea(target);
    }
  }

  ngAfterViewInit(): void {
    this.resizeAll();
  }

  private startLoadingAnimation(): void {
    this.loadingDots = "";
    this.stopLoadingAnimation();
    this.loadingInterval = setInterval(() => {
      if (this.loadingDots.length < 6) {
        this.loadingDots += ".";
      } else {
        this.loadingDots = "";
      }
    }, 500);
  }

  private stopLoadingAnimation(): void {
    if (this.loadingInterval) {
      clearInterval(this.loadingInterval);
      this.loadingInterval = undefined;
    }
    this.loadingDots = "";
  }

  private resizeTextArea(textarea: HTMLTextAreaElement): void {
    textarea.style.height = "auto";
    textarea.style.height = `${textarea.scrollHeight}px`;
  }

  private resizeAll(): void {
    const areas: NodeListOf<HTMLTextAreaElement> =
      this.elRef.nativeElement.querySelectorAll("textarea.code-area");
    areas.forEach((ta) => this.resizeTextArea(ta));
  }

  private buildQueryOrder(): QueryOrderEntry[] {
    return this.queryUnits.map((unit, index) => ({
      id: (index + 1).toString(),
      label: this.buildQueryLabel(unit.description, index),
    }));
  }

  private buildQueryLabel(description: string, index: number): string {
    const trimmed = description.trim();
    if (!trimmed) {
      return `Query ${index + 1}`;
    }

    if (this.queryUnits.length === 1) {
      return trimmed;
    }

    return `${trimmed} ${index + 1}`;
  }

  frameworkName(framework: ORMType): string {
    return (
      this.ormTypeOptions.find((opt) => opt.value === framework)?.key ??
      framework.toString()
    );
  }

  // Totals for the chosen assignment (sums per assigned framework per query)
  get totalAssignedRuntimeMs(): number {
    if (!this.advisorResult?.measurements) return 0;
    let total = 0;
    for (const a of this.assignmentEntries) {
      const m = this.lookupMeasurement(a.queryId, a.framework);
      if (m) total += Math.round(m.meanDurationMilliseconds ?? 0);
    }
    return total;
  }

  get totalAssignedMemoryKb(): number {
    if (!this.advisorResult?.measurements) return 0;
    let total = 0;
    for (const a of this.assignmentEntries) {
      const m = this.lookupMeasurement(a.queryId, a.framework);
      if (m) total += Math.round(((m.allocatedBytes ?? 0) as number) / 1024);
    }
    return total;
  }

  private lookupMeasurement(queryId: string, framework: ORMType):
    | { meanDurationMilliseconds: number; allocatedBytes: number }
    | null {
    const perFramework = (this.advisorResult?.measurements as any)?.[queryId] as
      | Record<string, { meanDurationMilliseconds: number; allocatedBytes: number }>
      | undefined;
    if (!perFramework) return null;
    // Try numeric key
    const direct = (perFramework as any)[framework];
    if (direct) return direct;
    // Try numeric-as-string
    const byString = (perFramework as any)[String(framework)];
    if (byString) return byString;
    // Try enum name
    const enumName = this.ormTypeOptions.find((o) => o.value === framework)?.key;
    if (enumName) {
      const byName = (perFramework as any)[enumName] ?? (perFramework as any)[enumName.toUpperCase()] ?? (perFramework as any)[enumName.toLowerCase()];
      if (byName) return byName;
    }
    return null;
  }

  back(): void {
    this.location.back();
  }
}
