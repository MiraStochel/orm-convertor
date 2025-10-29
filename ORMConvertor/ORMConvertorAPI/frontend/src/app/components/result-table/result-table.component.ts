import { ChangeDetectionStrategy, Component, Input } from "@angular/core";
import { CommonModule } from "@angular/common";
import { ORMType } from "../../model/orm-type";

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
  @Input() objective: number | null = null;

  private readonly frameworkOptions: { key: string; value: ORMType }[] =
    Object.keys(ORMType)
      .filter((k) => isNaN(Number(k)))
      .map((k) => ({ key: k, value: (ORMType as any)[k] as ORMType }));

  get uniqueFrameworks(): ORMType[] {
    return Array.from(new Set(this.selectedFrameworks ?? []));
  }

  frameworkName(framework: ORMType): string {
    return (
      this.frameworkOptions.find((opt) => opt.value === framework)?.key ??
      framework.toString()
    );
  }
}
